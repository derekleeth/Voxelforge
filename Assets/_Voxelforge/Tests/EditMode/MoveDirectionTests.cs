using NUnit.Framework;
using Unity.Mathematics;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    public class MoveDirectionTests
    {
        const float Tolerance = 1e-5f;

        static void AssertClose(float2 expected, float2 actual) =>
            Assert.That(math.distance(expected, actual), Is.LessThan(Tolerance), $"expected {expected}, got {actual}");

        [Test]
        public void FacingNorth_ForwardIsNorth()
        {
            AssertClose(new float2(0f, 1f), MoveDirection.ToWorld(new float2(0f, 1f), 0f));
        }

        [Test]
        public void FacingEast_ForwardIsEast_AndRightIsSouth()
        {
            AssertClose(new float2(1f, 0f), MoveDirection.ToWorld(new float2(0f, 1f), 90f));
            AssertClose(new float2(0f, -1f), MoveDirection.ToWorld(new float2(1f, 0f), 90f));
        }

        [Test]
        public void FacingSouth_ForwardIsSouth()
        {
            AssertClose(new float2(0f, -1f), MoveDirection.ToWorld(new float2(0f, 1f), 180f));
        }

        [Test]
        public void Turning_KeepsTheStickLength()
        {
            float2 move = new float2(0.3f, 0.4f);

            Assert.AreEqual(0.5f, math.length(MoveDirection.ToWorld(move, 37f)), Tolerance);
        }
    }
}
