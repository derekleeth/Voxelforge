using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The sounds blocks make (Roadmap 3.9, D-022): mining hits that rise in pitch as the block cracks, breaking and
    /// placing in the block's own <see cref="SurfaceSound"/>, the Mana Crystal's chime, one creak and a leaf rustle when
    /// a chopped tree falls, and a pop when a drop flies into a bag. Since Roadmap 4.6 also Earthen Rock crumbling, and
    /// spell zones: a sound when one is placed and on each pulse or strike, from its spell's look. It listens to the world's own events
    /// (<see cref="VoxelWorldManager.EditApplied"/>, <see cref="VoxelWorldManager.BlockDamaged"/>), which only the host
    /// raises; remote PCs don't see blocks until Phase 6. Presentation only.
    /// </summary>
    public sealed class WorldFoley : MonoBehaviour
    {
        [SerializeField] SoundRouter router;

        [SerializeField] SoundBook sounds;

        [SerializeField] VoxelWorldManager world;

        [Tooltip("For the pop when a drop is picked up.")]
        [SerializeField] BlockDropSpawner drops;

        [Tooltip("Data/Abilities/Looks/AbilityLookBook.asset: the sounds of spell zones and totems (Roadmap 4.6).")]
        [SerializeField] AbilityLookBook looks;

        [Tooltip("Least time between two hit sounds on one block, in seconds. A new crack stage always plays.")]
        [SerializeField, Range(0.05f, 1f)] float hitInterval = 0.22f;

        [SerializeField, Range(0f, 2f)] float hitVolume = 0.6f;

        [SerializeField, Range(0f, 2f)] float breakVolume = 0.9f;

        [SerializeField, Range(0f, 2f)] float placeVolume = 0.7f;

        [SerializeField, Range(0f, 2f)] float popVolume = 0.45f;

        [Tooltip("Most break sounds in one frame (a falling tree breaks a whole trunk, and torches with it).")]
        [SerializeField, Range(1, 8)] int maxBreaksPerFrame = 3;

        struct Hit
        {
            public float Time;
            public int Stage;
        }

        readonly Dictionary<int3, Hit> _hits = new Dictionary<int3, Hit>();
        int _breaksThisFrame;
        int _trunkBlocks;
        float3 _trunkSum;
        float3 _trunkTop;

        void OnEnable()
        {
            if (world != null)
            {
                world.EditApplied += OnEditApplied;
                world.BlockDamaged += OnBlockDamaged;
            }
            if (drops != null)
                drops.PickedUp += OnPickedUp;
            AbilityZone.Spawned += OnZoneSpawned;
        }

        void OnDisable()
        {
            if (world != null)
            {
                world.EditApplied -= OnEditApplied;
                world.BlockDamaged -= OnBlockDamaged;
            }
            if (drops != null)
                drops.PickedUp -= OnPickedUp;
            AbilityZone.Spawned -= OnZoneSpawned;
        }

        // A spell zone or totem (Roadmap 4.6): its Impact sound when placed, its Pulse sound each pulse or strike.
        void OnZoneSpawned(AbilityZone zone)
        {
            if (!TryGetLook(zone, out AbilityLook look))
                return;
            Play(look.Impact, (float3)zone.transform.position + new float3(0f, 0.5f, 0f), look.ImpactVolume, 1f, look.MaxDistance);
            zone.Pulsed += OnZonePulsed;
        }

        void OnZonePulsed(AbilityZone zone, bool struck, Vector3 point)
        {
            if (!TryGetLook(zone, out AbilityLook look))
                return;
            // A totem that strikes is only heard when it hits something; a healing zone on every pulse.
            if (struck)
                Play(look.Pulse, point, look.PulseVolume, UnityEngine.Random.Range(0.92f, 1.08f), look.MaxDistance);
            else if (zone.Ability.CreatureDamage <= 0f)
                Play(look.Pulse, (float3)zone.transform.position + new float3(0f, 1f, 0f), look.PulseVolume,
                    UnityEngine.Random.Range(0.95f, 1.05f), look.MaxDistance);
        }

        bool TryGetLook(AbilityZone zone, out AbilityLook look)
        {
            look = null;
            return zone != null && zone.Ability != null && looks != null && looks.TryGet(zone.Ability.Id, out look);
        }

        void OnBlockDamaged(VoxelHitDamage damage)
        {
            if (sounds == null)
                return;
            float now = Time.time;
            if (_hits.TryGetValue(damage.Coord, out Hit last) && damage.CrackStage <= last.Stage && now - last.Time < hitInterval)
                return;
            if (_hits.Count > 64)
                _hits.Clear(); // Old entries are blocks that healed or were left alone; nothing needs them.
            _hits[damage.Coord] = new Hit { Time = now, Stage = damage.CrackStage };

            SurfaceSound surface = sounds.SurfaceFor(damage.BlockId);
            if (surface == null)
                return;
            float3 at = (float3)damage.Coord + 0.5f;
            float pitch = SoundMath.HitPitch(damage.CrackStage, surface.HitPitchFirst, surface.HitPitchLast);
            Play(SurfaceSound.Pick(surface.Hits), at, hitVolume * surface.Volume, surface.JitteredPitch(pitch), 28f);
            Play(SurfaceSound.Pick(surface.HitExtras), at, hitVolume * 0.6f, pitch, 28f);
        }

        void OnEditApplied(VoxelEdit edit)
        {
            if (sounds == null || world.World == null)
                return;
            BlockInfo before = world.World.Blocks[edit.Before.BlockId];
            BlockInfo after = world.World.Blocks[edit.After.BlockId];
            float3 at = (float3)edit.Coord + 0.5f;

            if (before.IsTargetable && !after.IsTargetable)
            {
                _hits.Remove(edit.Coord);
                bool restOfTrunk = edit.Before.BlockId == BlockIds.TreeTrunk && _trunkBlocks++ > 0;
                if (edit.Before.BlockId == BlockIds.TreeTrunk)
                {
                    _trunkSum += at;
                    if (_trunkBlocks == 1 || at.y > _trunkTop.y)
                        _trunkTop = at;
                }
                if (restOfTrunk || _breaksThisFrame >= maxBreaksPerFrame)
                    return;
                _breaksThisFrame++;
                // Earthen Rock whose time ran out crumbles away (Roadmap 4.6) rather than breaking.
                if (edit.Before.BlockId == BlockIds.EarthenRock && edit.Instigator == Voxelforge.Core.PlayerId.World)
                {
                    Play(sounds.EarthCrumble, at, breakVolume, UnityEngine.Random.Range(0.85f, 1.1f), 32f);
                    return;
                }
                SurfaceSound surface = sounds.SurfaceFor(edit.Before.BlockId);
                if (surface == null)
                    return;
                Play(SurfaceSound.Pick(surface.Breaks), at, breakVolume * surface.Volume, surface.JitteredPitch(), 32f);
                Play(SurfaceSound.Pick(surface.BreakExtras), at, breakVolume, 1f, 40f);
            }
            else if (after.IsTargetable && !before.IsTargetable)
            {
                SurfaceSound surface = sounds.SurfaceFor(edit.After.BlockId);
                if (surface != null)
                    Play(SurfaceSound.Pick(surface.Places), at, placeVolume * surface.Volume, surface.JitteredPitch(), 24f);
            }
        }

        void OnPickedUp(float3 position, ushort blockId, int count)
        {
            if (sounds != null)
                Play(sounds.PickupPop, position, popVolume, UnityEngine.Random.Range(0.95f, 1.1f) + 0.03f * math.min(count, 8), 16f);
        }

        void LateUpdate()
        {
            // More than one trunk block in a frame: a chopped tree is falling.
            if (_trunkBlocks >= 2 && sounds != null)
            {
                Play(sounds.TreeCreak, _trunkSum / _trunkBlocks, 1f, UnityEngine.Random.Range(0.9f, 1.1f), 40f);
                Play(sounds.LeafRustle, _trunkTop + new float3(0f, 1.5f, 0f), 0.8f, UnityEngine.Random.Range(0.9f, 1.1f), 32f);
            }
            _trunkBlocks = 0;
            _trunkSum = default;
            _breaksThisFrame = 0;
        }

        void Play(AudioClip clip, float3 position, float volume, float pitch, float maxDistance)
        {
            if (router != null)
                router.Play(clip, position, volume, pitch, maxDistance);
        }
    }
}
