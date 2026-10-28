using System;
using NUnit.Framework;
using Voxelforge.Core;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 3.4: a player's identity across sessions.</summary>
    public class PlayerUuidTests
    {
        [Test]
        public void None_IsNotValid()
        {
            Assert.IsFalse(PlayerUuid.None.IsValid);
            Assert.IsFalse(default(PlayerUuid).IsValid);
        }

        [Test]
        public void NewUuid_IsValid_AndUnique()
        {
            PlayerUuid a = PlayerUuid.NewUuid();
            PlayerUuid b = PlayerUuid.NewUuid();
            Assert.IsTrue(a.IsValid);
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void Guid_RoundTrips()
        {
            var guid = Guid.NewGuid();
            Assert.AreEqual(guid, new PlayerUuid(guid).ToGuid());
        }

        [Test]
        public void String_RoundTrips()
        {
            PlayerUuid uuid = PlayerUuid.NewUuid();
            Assert.IsTrue(PlayerUuid.TryParse(uuid.ToString(), out PlayerUuid parsed));
            Assert.AreEqual(uuid, parsed);
        }

        [Test]
        public void TryParse_RejectsJunk()
        {
            Assert.IsFalse(PlayerUuid.TryParse("not a uuid", out PlayerUuid parsed));
            Assert.AreEqual(PlayerUuid.None, parsed);
            Assert.IsFalse(PlayerUuid.TryParse(null, out _));
            Assert.IsFalse(PlayerUuid.TryParse(Guid.Empty.ToString(), out _), "The empty GUID is no one.");
        }

        [Test]
        public void HalvesRoundTrip_LikeTheJoinRpcSendsThem()
        {
            PlayerUuid uuid = PlayerUuid.NewUuid();
            Assert.AreEqual(uuid, new PlayerUuid(uuid.High, uuid.Low));
        }
    }
}
