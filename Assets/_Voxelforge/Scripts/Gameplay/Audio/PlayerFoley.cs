using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The sounds heroes make, for every hero on every PC (Roadmap 3.9, D-022): footsteps that sound like the block
    /// under the foot (<see cref="HeroFootIK.StepCount"/>), a jump whoosh, a landing thump that gets heavier with the
    /// fall, and its spells (4.2): a hum while casting, then the release, impact and fizzle sounds of the spell's
    /// <see cref="AbilityLook"/>, from the <see cref="AbilityCue"/>s the host sends (<see cref="PlayerAbilityCues"/>).
    /// A sprinting hero (4.8) also has wind rushing past them, from how fast they're really moving.
    /// Presentation only: it reads what each PC already knows and plays it through the <see cref="SoundRouter"/>.
    /// </summary>
    public sealed class PlayerFoley : MonoBehaviour
    {
        [SerializeField] SoundRouter router;

        [SerializeField] SoundBook sounds;

        [Tooltip("Data/Abilities/Looks/AbilityLookBook.asset: each spell's sounds.")]
        [SerializeField] AbilityLookBook looks;

        [SerializeField, Range(0f, 2f)] float footstepVolume = 0.25f;

        [Tooltip("Least time between one hero's footsteps, in seconds, in case the animation plants a foot twice.")]
        [SerializeField, Range(0f, 0.5f)] float minStepInterval = 0.18f;

        [SerializeField, Range(0f, 2f)] float jumpVolume = 0.5f;

        [Tooltip("Falls shorter than this many seconds land without a thump (stepping off one block).")]
        [SerializeField, Min(0f)] float minLandAirTime = 0.3f;

        [Tooltip("Falling this fast (m/s) or faster lands at full volume.")]
        [SerializeField, Min(1f)] float heavyLandSpeed = 14f;

        [Tooltip("Ground speed (m/s) where sprint wind starts to be heard. Walking is 5 m/s and sprinting 8.")]
        [SerializeField, Min(0f)] float windFromSpeed = 5.8f;

        [Tooltip("Ground speed (m/s) where sprint wind is at full volume.")]
        [SerializeField, Min(0f)] float windFullSpeed = 7.8f;

        [SerializeField, Range(0f, 2f)] float windVolume = 0.35f;

        [Tooltip("How quickly the wind fades in and out, per second.")]
        [SerializeField, Min(0.1f)] float windFade = 4f;

        sealed class Hero
        {
            public PlayerController Player;
            public HeroFootIK Feet;
            /// <summary>Their race's body (Roadmap 4.7): a Thunderhoof's steps are lower and louder.</summary>
            public HeroBody Body;
            public HeroAnimator Animator;
            public PlayerAbilityCues Cues;
            public Action<AbilityCue, AbilityDefinition> OnCue;
            public PlayerHealth Health;
            public Action<HealthEvent> OnHealth;
            public int CastLoop;
            public float CastLoopVolume;
            public int StepCount;
            public float LastStepTime = float.NegativeInfinity;
            public bool WasGrounded = true;
            public float AirTime;
            public float FallSpeed;
            public bool JumpPlayed;
            public int WindLoop;
            public float Wind;
        }

        readonly List<Hero> _heroes = new List<Hero>();

        void OnEnable()
        {
            PlayerController.AnySpawned += OnSpawned;
            PlayerController.AnyDespawned += OnDespawned;
        }

        void OnDisable()
        {
            PlayerController.AnySpawned -= OnSpawned;
            PlayerController.AnyDespawned -= OnDespawned;
            for (int i = _heroes.Count - 1; i >= 0; i--)
                Forget(i);
        }

        void OnSpawned(PlayerController player)
        {
            var hero = new Hero
            {
                Player = player,
                Feet = player.GetComponentInChildren<HeroFootIK>(),
                Body = player.GetComponent<HeroBody>(),
                Animator = player.GetComponentInChildren<HeroAnimator>(),
                Cues = player.GetComponent<PlayerAbilityCues>(),
            };
            hero.StepCount = hero.Feet != null ? hero.Feet.StepCount : 0;
            if (hero.Cues != null)
            {
                hero.OnCue = (cue, ability) => OnCue(hero, cue, ability);
                hero.Cues.Received += hero.OnCue;
            }
            hero.Health = player.GetComponent<PlayerHealth>();
            if (hero.Health != null)
            {
                hero.OnHealth = e => OnHealth(hero, e);
                hero.Health.Changed += hero.OnHealth;
            }
            _heroes.Add(hero);
        }

        void OnDespawned(PlayerController player)
        {
            int index = _heroes.FindIndex(h => h.Player == player);
            if (index >= 0)
                Forget(index);
        }

        void Forget(int index)
        {
            Hero hero = _heroes[index];
            if (hero.Cues != null)
                hero.Cues.Received -= hero.OnCue;
            if (hero.Health != null)
                hero.Health.Changed -= hero.OnHealth;
            StopCastLoop(hero);
            StopWind(hero);
            _heroes.RemoveAt(index);
        }

        void LateUpdate()
        {
            if (router == null || sounds == null)
                return;
            for (int i = _heroes.Count - 1; i >= 0; i--)
            {
                Hero hero = _heroes[i];
                if (hero.Player == null)
                {
                    Forget(i);
                    continue;
                }
                if (hero.CastLoop != 0)
                    router.MoveLoop(hero.CastLoop, Chest(hero.Player), hero.CastLoopVolume);
                if (hero.Player.HasLanded)
                    Listen(hero);
            }
        }

        static float StepPitch(Hero hero) => hero.Body != null && hero.Body.Look != null ? hero.Body.Look.FootstepPitch : 1f;

        static float StepVolume(Hero hero) => hero.Body != null && hero.Body.Look != null ? hero.Body.Look.FootstepVolume : 1f;

        void Listen(Hero hero)
        {
            PlayerController player = hero.Player;
            float3 feet = player.VisualRoot.position;

            if (hero.Feet != null && hero.Feet.StepCount != hero.StepCount)
            {
                hero.StepCount = hero.Feet.StepCount;
                float3 step = hero.Feet.LastStepPosition;
                SurfaceSound surface = SurfaceUnder(player.World, step, feet);
                bool tooSoon = Time.time - hero.LastStepTime < minStepInterval;
                if (!tooSoon)
                    hero.LastStepTime = Time.time;
                if (surface != null && !tooSoon)
                    Play(SurfaceSound.Pick(surface.Steps), step, footstepVolume * surface.Volume * StepVolume(hero),
                        surface.JitteredPitch() * StepPitch(hero), 20f);
            }

            bool grounded = player.Grounded;
            float verticalSpeed = hero.Animator != null ? hero.Animator.VerticalSpeed : 0f;
            if (!grounded)
            {
                // The animator only knows the vertical speed a frame after leaving the ground, so the jump waits for it.
                hero.AirTime += Time.deltaTime;
                hero.FallSpeed = math.max(hero.FallSpeed, -verticalSpeed);
                if (!hero.JumpPlayed && hero.AirTime < 0.2f && verticalSpeed > 1.5f)
                {
                    hero.JumpPlayed = true;
                    Play(sounds.Jump, feet, jumpVolume, UnityEngine.Random.Range(0.92f, 1.08f), 20f);
                }
            }
            else if (!hero.WasGrounded)
            {
                if (hero.AirTime >= minLandAirTime)
                {
                    SurfaceSound surface = SurfaceUnder(player.World, feet, feet);
                    float weight = math.saturate(hero.FallSpeed / heavyLandSpeed);
                    if (surface != null)
                        Play(SurfaceSound.Pick(surface.Steps), feet, math.lerp(0.6f, 1.3f, weight) * surface.Volume * StepVolume(hero),
                            surface.JitteredPitch(math.lerp(0.85f, 0.65f, weight)) * StepPitch(hero), 24f);
                }
                hero.AirTime = 0f;
                hero.FallSpeed = 0f;
                hero.JumpPlayed = false;
            }
            hero.WasGrounded = grounded;
            UpdateWind(hero);
        }

        /// <summary>Roadmap 4.8: wind fades in as a hero runs faster than walking, and away when they slow down.</summary>
        void UpdateWind(Hero hero)
        {
            float speed = hero.Animator != null ? hero.Animator.Speed : 0f;
            float target = math.saturate((speed - windFromSpeed) / math.max(0.01f, windFullSpeed - windFromSpeed));
            hero.Wind = math.lerp(hero.Wind, target, 1f - math.exp(-windFade * Time.deltaTime));
            if (hero.Wind < 0.01f)
            {
                StopWind(hero);
                return;
            }
            float3 chest = Chest(hero.Player);
            if (hero.WindLoop == 0)
                hero.WindLoop = router.StartLoop(sounds.SprintWind, chest, 0f, 14f, SoundCategory.Sfx);
            router.MoveLoop(hero.WindLoop, chest, hero.Wind * windVolume);
        }

        void StopWind(Hero hero)
        {
            if (hero.WindLoop != 0 && router != null)
                router.StopLoop(hero.WindLoop);
            hero.WindLoop = 0;
        }

        /// <summary>
        /// Roadmap 4.6: a shield going on chimes with its spell's Impact sound on the hero who got it, and a hit it
        /// soaks up clinks.
        /// </summary>
        void OnHealth(Hero hero, HealthEvent e)
        {
            if (hero.Player == null || sounds == null)
                return;
            float3 chest = Chest(hero.Player);
            switch (e.Kind)
            {
                case HealthEventKind.Shielded:
                    if (looks != null && looks.TryGet(e.AbilityId, out AbilityLook look))
                        Play(look.Impact, chest, look.ImpactVolume, 1f, look.MaxDistance);
                    break;
                case HealthEventKind.Absorbed:
                    Play(sounds.ShieldBlock, chest, 0.7f, UnityEngine.Random.Range(0.92f, 1.1f), 24f);
                    break;
            }
        }

        void OnCue(Hero hero, AbilityCue cue, AbilityDefinition ability)
        {
            AbilityLook look = looks != null ? looks.For(ability) : null;
            if (router == null || look == null)
                return;
            switch (cue.Kind)
            {
                case AbilityCueKind.CastBegan:
                    StopCastLoop(hero);
                    hero.CastLoopVolume = look.CastLoopVolume;
                    hero.CastLoop = router.StartLoop(look.CastLoop, Chest(hero.Player), look.CastLoopVolume, look.MaxDistance,
                        SoundCategory.Sfx);
                    break;
                case AbilityCueKind.CastStopped:
                    StopCastLoop(hero);
                    break;
                case AbilityCueKind.Released:
                    StopCastLoop(hero);
                    Play(look.Release, Chest(hero.Player), look.ReleaseVolume, UnityEngine.Random.Range(0.96f, 1.04f), look.MaxDistance);
                    break;
                case AbilityCueKind.Impact:
                    Play(look.Impact, cue.Position, look.ImpactVolume, 1f, look.MaxDistance);
                    break;
                case AbilityCueKind.Fizzled:
                    if (look.Fizzle != null)
                        Play(look.Fizzle, cue.Position, look.ImpactVolume, 1f, look.MaxDistance);
                    else
                        Play(look.Impact, cue.Position, look.ImpactVolume * 0.5f, 1.3f, look.MaxDistance);
                    break;
            }
        }

        void StopCastLoop(Hero hero)
        {
            if (hero.CastLoop != 0 && router != null)
                router.StopLoop(hero.CastLoop);
            hero.CastLoop = 0;
        }

        static float3 Chest(PlayerController player) => (float3)player.VisualRoot.position + new float3(0f, 1.2f, 0f);

        /// <summary>
        /// The surface under <paramref name="point"/>, or under the hero's middle when the foot is over the edge.
        /// The default surface on a PC without blocks (a remote PC, until Phase 6).
        /// </summary>
        SurfaceSound SurfaceUnder(VoxelWorld world, float3 point, float3 middle)
        {
            if (world != null && (TryGetSolidBelow(world, point, out ushort blockId) || TryGetSolidBelow(world, middle, out blockId)))
                return sounds.SurfaceFor(blockId);
            return sounds.DefaultSurface;
        }

        static bool TryGetSolidBelow(VoxelWorld world, float3 point, out ushort blockId)
        {
            var block = (int3)math.floor(point - new float3(0f, 0.1f, 0f));
            blockId = 0;
            if (!world.TryGetChunk(VoxelMath.WorldToChunk(block), out _))
                return false;
            blockId = world.GetVoxel(block).BlockId;
            return world.Blocks[blockId].IsSolid;
        }

        void Play(AudioClip clip, float3 position, float volume, float pitch, float maxDistance)
        {
            if (router != null)
                router.Play(clip, position, volume, pitch, maxDistance);
        }
    }
}
