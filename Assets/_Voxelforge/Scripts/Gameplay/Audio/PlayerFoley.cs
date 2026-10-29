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
    /// fall, and the M.G.R.S. firing and its Mana Bolt landing (<see cref="ManaBoltView"/>, 3.6). Presentation only: it
    /// reads what each PC already sees and plays it through the <see cref="SoundRouter"/>.
    /// </summary>
    public sealed class PlayerFoley : MonoBehaviour
    {
        [SerializeField] SoundRouter router;

        [SerializeField] SoundBook sounds;

        [SerializeField, Range(0f, 2f)] float footstepVolume = 0.25f;

        [Tooltip("Least time between one hero's footsteps, in seconds, in case the animation plants a foot twice.")]
        [SerializeField, Range(0f, 0.5f)] float minStepInterval = 0.18f;

        [SerializeField, Range(0f, 2f)] float jumpVolume = 0.5f;

        [Tooltip("Falls shorter than this many seconds land without a thump (stepping off one block).")]
        [SerializeField, Min(0f)] float minLandAirTime = 0.3f;

        [Tooltip("Falling this fast (m/s) or faster lands at full volume.")]
        [SerializeField, Min(1f)] float heavyLandSpeed = 14f;

        [SerializeField, Range(0f, 2f)] float boltFireVolume = 0.8f;

        [SerializeField, Range(0f, 2f)] float boltImpactVolume = 0.9f;

        sealed class Hero
        {
            public PlayerController Player;
            public HeroFootIK Feet;
            public HeroAnimator Animator;
            public ManaBoltView Bolts;
            public Action<Vector3> OnFired;
            public Action<Vector3, bool> OnImpacted;
            public int StepCount;
            public float LastStepTime = float.NegativeInfinity;
            public bool WasGrounded = true;
            public float AirTime;
            public float FallSpeed;
            public bool JumpPlayed;
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
                Animator = player.GetComponentInChildren<HeroAnimator>(),
                Bolts = player.GetComponent<ManaBoltView>(),
            };
            hero.StepCount = hero.Feet != null ? hero.Feet.StepCount : 0;
            if (hero.Bolts != null)
            {
                hero.OnFired = position => Play(sounds != null ? sounds.BoltFire : null, position, boltFireVolume, 1f, 40f);
                hero.OnImpacted = (position, hitBlock) =>
                    Play(sounds != null ? sounds.BoltImpact : null, position, boltImpactVolume * (hitBlock ? 1f : 0.5f), hitBlock ? 1f : 1.3f, 40f);
                hero.Bolts.Fired += hero.OnFired;
                hero.Bolts.Impacted += hero.OnImpacted;
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
            if (hero.Bolts != null)
            {
                hero.Bolts.Fired -= hero.OnFired;
                hero.Bolts.Impacted -= hero.OnImpacted;
            }
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
                if (hero.Player.HasLanded)
                    Listen(hero);
            }
        }

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
                    Play(SurfaceSound.Pick(surface.Steps), step, footstepVolume * surface.Volume, surface.JitteredPitch(), 20f);
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
                        Play(SurfaceSound.Pick(surface.Steps), feet, math.lerp(0.6f, 1.3f, weight) * surface.Volume,
                            surface.JitteredPitch(math.lerp(0.85f, 0.65f, weight)), 24f);
                }
                hero.AirTime = 0f;
                hero.FallSpeed = 0f;
                hero.JumpPlayed = false;
            }
            hero.WasGrounded = grounded;
        }

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
