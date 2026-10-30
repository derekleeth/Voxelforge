using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;
using Object = UnityEngine.Object;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.2: the host's rules, the cues it sends, and the looks each PC plays them with, kept apart.</summary>
    public class AbilityCueTests
    {
        AbilityDefinition _bolt;
        AbilityDefinition _orb;

        [SetUp]
        public void SetUp()
        {
            _bolt = AbilityDefinition.Create(AbilityIds.ManaBolt, "Mana Bolt", manaCost: 8f, blockDamage: 45f);
            _orb = AbilityDefinition.Create(AbilityIds.ArcaneOrb, "Arcane Orb", manaCost: 30f, castTime: 1.5f, blockDamage: 120f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_bolt);
            Object.DestroyImmediate(_orb);
        }

        static AbilityCue RoundTrip(AbilityCue cue)
        {
            using var writer = new FastBufferWriter(64, Allocator.Temp);
            writer.WriteNetworkSerializable(cue);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out AbilityCue read);
            return read;
        }

        [Test]
        public void EveryCueKind_SurvivesTheWire()
        {
            AbilityCue began = RoundTrip(AbilityCue.Cast(AbilityCueKind.CastBegan, AbilityIds.ArcaneOrb, new Vector3(1f, 2f, 3f)));
            Assert.AreEqual(AbilityCueKind.CastBegan, began.Kind);
            Assert.AreEqual(AbilityIds.ArcaneOrb, began.AbilityId);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), began.Position);

            AbilityCue stopped = RoundTrip(AbilityCue.Cast(AbilityCueKind.CastStopped, AbilityIds.ArcaneOrb, Vector3.zero, CastCheck.Interrupted));
            Assert.AreEqual(CastCheck.Interrupted, stopped.Reason);

            Assert.AreEqual(AbilityCueKind.Released, RoundTrip(AbilityCue.Cast(AbilityCueKind.Released, AbilityIds.ManaBolt, Vector3.zero)).Kind);

            var bolt = new ManaBolt { Id = 7, AbilityId = AbilityIds.ManaBolt, Position = new float3(4f, 5f, 6f), Direction = new float3(0f, 0f, 1f) };
            AbilityCue launched = RoundTrip(AbilityCue.Launch(bolt));
            Assert.AreEqual(AbilityCueKind.Launched, launched.Kind);
            Assert.AreEqual(7, launched.ShotId);
            Assert.AreEqual(new Vector3(4f, 5f, 6f), launched.Position);
            Assert.AreEqual(Vector3.forward, launched.Direction);

            var hit = new ManaBoltEnd
            {
                Id = 7, AbilityId = AbilityIds.ManaBolt, HitBlock = true, Point = new float3(9f, 8f, 7f), Normal = new float3(-1f, 0f, 0f),
            };
            AbilityCue impact = RoundTrip(AbilityCue.End(hit, BlockIds.Stone, blocksBroken: 112));
            Assert.AreEqual(AbilityCueKind.Impact, impact.Kind);
            Assert.AreEqual(112, impact.BlocksBroken);
            Assert.AreEqual(7, impact.ShotId);
            Assert.AreEqual(BlockIds.Stone, impact.BlockId);
            Assert.AreEqual(Vector3.left, impact.Direction);

            var fizzle = new ManaBoltEnd { Id = 8, AbilityId = AbilityIds.ManaBolt, Point = new float3(1f, 1f, 1f), Normal = new float3(0f, 0f, -1f) };
            AbilityCue fizzled = RoundTrip(AbilityCue.End(fizzle, BlockIds.Air));
            Assert.AreEqual(AbilityCueKind.Fizzled, fizzled.Kind);
            Assert.AreEqual(Vector3.back, fizzled.Direction);
        }

        [Test]
        public void Impact_DamagesOnlyTheBlockHit_AndAFizzleDamagesNothing()
        {
            var damage = new List<VoxelDamageRequest>();
            var hit = new ManaBoltEnd { HitBlock = true, Hit = new VoxelHit { Block = new int3(3, 4, 5) }, Damage = 45f };
            AbilityImpact.Resolve(_bolt, hit, damage);
            Assert.AreEqual(1, damage.Count);
            Assert.AreEqual(new int3(3, 4, 5), damage[0].Block);
            Assert.AreEqual(45f, damage[0].Amount);

            damage.Clear();
            AbilityImpact.Resolve(_bolt, new ManaBoltEnd { Damage = 45f }, damage);
            Assert.AreEqual(0, damage.Count, "A fizzle hits nothing.");
            AbilityImpact.Resolve(null, hit, damage);
            Assert.AreEqual(0, damage.Count, "No ability, no damage.");
        }

        [Test]
        public void LookBook_FindsEachSpellsLook_AndFallsBackToTheDefault()
        {
            AbilityLook boltLook = AbilityLook.Create(_bolt, Color.cyan);
            AbilityLook orbLook = AbilityLook.Create(_orb, Color.magenta);
            AbilityDefinition unknown = AbilityDefinition.Create(999, "Unknown");
            AbilityLookBook book = AbilityLookBook.Create(boltLook, new[] { boltLook, orbLook });
            try
            {
                Assert.AreSame(orbLook, book.For(_orb));
                Assert.AreSame(boltLook, book.For(_bolt));
                Assert.AreSame(boltLook, book.For(unknown), "No look of its own: the default.");
                Assert.AreSame(boltLook, book.For(null));
                Assert.IsFalse(book.TryGet(999, out _));
            }
            finally
            {
                Object.DestroyImmediate(book);
                Object.DestroyImmediate(boltLook);
                Object.DestroyImmediate(orbLook);
                Object.DestroyImmediate(unknown);
            }
        }

        /// <summary>Things you see or hear. The rules must never hold one (CLAUDE.md, Roadmap 4.2).</summary>
        static readonly Type[] Presentation =
        {
            typeof(Color), typeof(Material), typeof(AudioClip), typeof(ParticleSystem), typeof(Light), typeof(Renderer),
            typeof(AbilityLook), typeof(AbilityLookBook), typeof(ManaBoltView), typeof(CastingView), typeof(SoundRouter),
            typeof(Sprite), typeof(Texture), typeof(HudSkin),
        };

        [TestCase(typeof(AbilityDefinition))]
        [TestCase(typeof(AbilityBook))]
        [TestCase(typeof(AbilityCaster))]
        [TestCase(typeof(ManaBoltFlight))]
        [TestCase(typeof(AbilityImpact))]
        [TestCase(typeof(AbilityCue))]
        [TestCase(typeof(PlayerCaster))]
        [TestCase(typeof(PlayerStaff))]
        [TestCase(typeof(PlayerAbilityCues))]
        [TestCase(typeof(PlayerActionBar))]
        [TestCase(typeof(ActionBarLoadout))]
        [TestCase(typeof(HeroHealth))]
        [TestCase(typeof(PlayerHealth))]
        [TestCase(typeof(HealthEvent))]
        [TestCase(typeof(SparringDummy))]
        [TestCase(typeof(CombatTargets))]
        [TestCase(typeof(CombatTarget))]
        [TestCase(typeof(TargetDamage))]
        [TestCase(typeof(ClassDefinition))]
        [TestCase(typeof(ClassBook))]
        [TestCase(typeof(PlayerClass))]
        [TestCase(typeof(PlayerSupport))]
        [TestCase(typeof(AbilityZone))]
        [TestCase(typeof(ZoneRules))]
        [TestCase(typeof(EarthWorks))]
        [TestCase(typeof(EarthTimers))]
        public void Rules_HoldNothingYouSeeOrHear(Type rules)
        {
            const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.DeclaredOnly;
            foreach (FieldInfo field in rules.GetFields(All))
            {
                Type type = field.FieldType.IsArray ? field.FieldType.GetElementType() : field.FieldType;
                if (type.IsGenericType)
                    type = type.GetGenericArguments()[0];
                foreach (Type banned in Presentation)
                    Assert.IsFalse(banned.IsAssignableFrom(type), $"{rules.Name}.{field.Name} is a {type.Name}: that belongs in a look or a view.");
            }
        }
    }
}
