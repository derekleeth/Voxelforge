using NUnit.Framework;
using Voxelforge.Core;

namespace Voxelforge.Tests
{
    public class PlayerIdTests
    {
        [Test]
        public void None_IsDefaultAndInvalid()
        {
            Assert.AreEqual(PlayerId.None, default(PlayerId));
            Assert.IsFalse(PlayerId.None.IsValid);
            Assert.AreEqual("None", PlayerId.None.ToString());
        }

        [Test]
        public void SameValue_IsEqual()
        {
            Assert.AreEqual(new PlayerId(2), new PlayerId(2));
            Assert.IsTrue(new PlayerId(1) != new PlayerId(2));
            Assert.AreEqual(new PlayerId(3).GetHashCode(), new PlayerId(3).GetHashCode());
        }

        [Test]
        public void ToString_ShowsPlayerNumber()
        {
            Assert.AreEqual("P1", new PlayerId(1).ToString());
            Assert.AreEqual("P2", new PlayerId(2).ToString());
        }
    }
}
