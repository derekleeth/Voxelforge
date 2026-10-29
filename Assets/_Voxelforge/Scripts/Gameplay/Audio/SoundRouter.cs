using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>Which volume slider a sound follows (6.3 adds the sliders).</summary>
    public enum SoundCategory : byte
    {
        Sfx,
        Ambience,
    }

    /// <summary>
    /// Plays the game's sounds for <b>every</b> local player at once (Roadmap 3.9, D-022). Unity has one
    /// <see cref="AudioListener"/>, so instead of 3D sources, every sound is a pooled 2D voice and this component does
    /// the 3D part itself, once per local player (an "ear" on their hero, turned like their camera):
    /// <list type="bullet">
    /// <item>Loudness from the nearest ear, so a torch next to Player 2 is loud however far away Player 1 is.</item>
    /// <item>Left/right from where the sound sits in the loudest ear's part of the screen
    /// (<see cref="SoundMath.RegionFor"/>): in a side-by-side split, Player 1's sounds come from the left speaker.</item>
    /// </list>
    /// Voices remember where they are and are re-mixed every frame, so loops and moving players stay right.
    /// <b>Not a singleton:</b> sound makers get it by reference (CLAUDE.md Law #1), and the ears come from each
    /// player's own view (Law #5).
    /// </summary>
    [DefaultExecutionOrder(100)] // After SplitScreenDirector has placed this frame's cameras.
    public sealed class SoundRouter : MonoBehaviour
    {
        const int MaxSlots = 4;

        [SerializeField] LocalPlayerManager localPlayers;

        [SerializeField] SplitScreenDirector cameras;

        [Tooltip("Most sounds playing at once. When full, a new sound replaces the quietest one if it's louder.")]
        [SerializeField, Range(4, 64)] int voiceCount = 32;

        [Tooltip("Sounds closer than this to a player are at full volume, in metres.")]
        [SerializeField, Min(0.1f)] float minDistance = 2f;

        [Tooltip("Height of a player's ear above their feet, in metres.")]
        [SerializeField] float earHeight = 1.6f;

        [SerializeField, Range(0f, 1f)] float sfxVolume = 1f;

        [SerializeField, Range(0f, 1f)] float ambienceVolume = 1f;

        struct Ear
        {
            public float3 Position;
            public float3 Right;
            public PanRegion Region;
        }

        sealed class Voice
        {
            public AudioSource Source;
            public float3 Position;
            public float Volume;
            public float MaxDistance;
            public SoundCategory Category;
            public int LoopId;
            /// <summary>What it's heard at right now, for choosing which voice to replace.</summary>
            public float Heard;
            public bool IsLoop => LoopId != 0;
            public bool IsBusy => IsLoop || Source.isPlaying;
        }

        readonly Ear[] _ears = new Ear[MaxSlots];
        int _earCount;
        int _earsFrame = -1;
        Voice[] _voices;
        int _nextLoopId = 1;

        public float SfxVolume { get => sfxVolume; set => sfxVolume = math.saturate(value); }
        public float AmbienceVolume { get => ambienceVolume; set => ambienceVolume = math.saturate(value); }

        /// <summary>One-off sounds playing now. For the debug overlay.</summary>
        public int ActiveVoices { get; private set; }

        /// <summary>Loops playing now. For the debug overlay.</summary>
        public int ActiveLoops { get; private set; }

        public int VoiceCount => voiceCount;

        /// <summary>Distance from <paramref name="position"/> to the nearest local player's ear. Infinity with nobody.</summary>
        public float NearestEarDistance(float3 position)
        {
            UpdateEars();
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < _earCount; i++)
                nearest = math.min(nearest, math.distance(position, _ears[i].Position));
            return nearest;
        }

        /// <summary>Plays <paramref name="clip"/> once, as if from <paramref name="position"/>.</summary>
        public void Play(AudioClip clip, float3 position, float volume = 1f, float pitch = 1f, float maxDistance = 32f,
            SoundCategory category = SoundCategory.Sfx)
        {
            if (clip == null || volume <= 0f)
                return;
            UpdateEars();
            Mix(position, maxDistance, out float gain, out float pan);
            float heard = gain * volume;
            if (heard < 0.001f)
                return;

            Voice voice = FreeVoice(heard);
            if (voice == null)
                return;
            Set(voice, position, volume, maxDistance, category, 0);
            voice.Heard = heard;
            AudioSource source = voice.Source;
            source.clip = clip;
            source.loop = false;
            source.pitch = pitch;
            source.volume = heard * CategoryVolume(category);
            source.panStereo = pan;
            source.Play();
        }

        /// <summary>
        /// Starts <paramref name="clip"/> looping at <paramref name="position"/>. Returns an ID for <see cref="MoveLoop"/>
        /// and <see cref="StopLoop"/>, or 0 if every voice is already a loop.
        /// </summary>
        public int StartLoop(AudioClip clip, float3 position, float volume = 1f, float maxDistance = 16f,
            SoundCategory category = SoundCategory.Ambience)
        {
            if (clip == null)
                return 0;
            UpdateEars();
            Voice voice = FreeVoice(float.PositiveInfinity);
            if (voice == null)
                return 0;
            int id = _nextLoopId++;
            if (_nextLoopId == int.MaxValue)
                _nextLoopId = 1;
            Set(voice, position, volume, maxDistance, category, id);
            AudioSource source = voice.Source;
            source.clip = clip;
            source.loop = true;
            source.pitch = 1f;
            source.time = UnityEngine.Random.value * clip.length; // Torches side by side don't crackle in step.
            Remix(voice);
            source.Play();
            return id;
        }

        /// <summary>Moves a loop and/or changes its volume.</summary>
        public void MoveLoop(int loopId, float3 position, float volume)
        {
            Voice voice = FindLoop(loopId);
            if (voice == null)
                return;
            voice.Position = position;
            voice.Volume = volume;
        }

        public void StopLoop(int loopId)
        {
            Voice voice = FindLoop(loopId);
            if (voice == null)
                return;
            voice.LoopId = 0;
            voice.Source.Stop();
            voice.Source.clip = null;
        }

        void Awake()
        {
            _voices = new Voice[voiceCount];
            for (int i = 0; i < voiceCount; i++)
            {
                var child = new GameObject($"Voice {i}");
                child.transform.SetParent(transform, false);
                AudioSource source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f; // We do the 3D part ourselves, per player.
                source.dopplerLevel = 0f;
                _voices[i] = new Voice { Source = source };
            }
        }

        void LateUpdate()
        {
            UpdateEars();
            int voices = 0, loops = 0;
            foreach (Voice voice in _voices)
            {
                if (!voice.IsBusy)
                    continue;
                Remix(voice);
                if (voice.IsLoop)
                    loops++;
                else
                    voices++;
            }
            ActiveVoices = voices;
            ActiveLoops = loops;
        }

        void Remix(Voice voice)
        {
            Mix(voice.Position, voice.MaxDistance, out float gain, out float pan);
            voice.Heard = gain * voice.Volume;
            voice.Source.volume = voice.Heard * CategoryVolume(voice.Category);
            voice.Source.panStereo = pan;
        }

        float CategoryVolume(SoundCategory category) => category == SoundCategory.Ambience ? ambienceVolume : sfxVolume;

        /// <summary>Loudness from the nearest ear, and pan from that ear's part of the screen.</summary>
        void Mix(float3 position, float maxDistance, out float gain, out float pan)
        {
            gain = 0f;
            pan = 0f;
            for (int i = 0; i < _earCount; i++)
            {
                float g = SoundMath.Gain(math.distance(position, _ears[i].Position), minDistance, maxDistance);
                if (g <= gain)
                    continue;
                gain = g;
                pan = SoundMath.Pan(_ears[i].Region, _ears[i].Position, _ears[i].Right, position);
            }
        }

        /// <summary>One ear per local player with a view on screen. Once per frame.</summary>
        void UpdateEars()
        {
            if (_earsFrame == Time.frameCount)
                return;
            _earsFrame = Time.frameCount;
            _earCount = 0;
            if (localPlayers == null || cameras == null)
                return;
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                if (!localPlayers.TryGetLocalPlayer(slot, out PlayerController player, out _) || !player.HasLanded ||
                    !cameras.TryGetView(player.LocalSlot, out PlayerView view))
                    continue;
                _ears[_earCount++] = new Ear
                {
                    Position = (float3)player.VisualRoot.position + new float3(0f, earHeight, 0f),
                    Right = view.Camera.transform.right,
                    Region = SoundMath.RegionFor(view),
                };
            }
        }

        /// <summary>A voice that's free, or else the quietest one-off sound if it's quieter than <paramref name="heard"/>.</summary>
        Voice FreeVoice(float heard)
        {
            Voice quietest = null;
            foreach (Voice voice in _voices)
            {
                if (!voice.IsBusy)
                    return voice;
                if (!voice.IsLoop && (quietest == null || voice.Heard < quietest.Heard))
                    quietest = voice;
            }
            if (quietest == null || quietest.Heard >= heard)
                return null;
            quietest.Source.Stop();
            return quietest;
        }

        Voice FindLoop(int loopId)
        {
            if (loopId == 0 || _voices == null)
                return null;
            foreach (Voice voice in _voices)
            {
                if (voice.LoopId == loopId)
                    return voice;
            }
            return null;
        }

        static void Set(Voice voice, float3 position, float volume, float maxDistance, SoundCategory category, int loopId)
        {
            voice.Position = position;
            voice.Volume = volume;
            voice.MaxDistance = maxDistance;
            voice.Category = category;
            voice.LoopId = loopId;
        }
    }
}
