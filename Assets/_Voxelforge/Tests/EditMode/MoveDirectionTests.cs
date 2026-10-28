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

        [Test]
        public void YawOf_UndoesToWorld()
        {
            Assert.AreEqual(90f, MoveDirection.YawOf(new float2(1f, 0f)), Tolerance);
            Assert.AreEqual(0f, MoveDirection.YawOf(new float2(0f, 1f)), Tolerance);
            float2 world = MoveDirection.ToWorld(new float2(0f, 1f), -135f);
            Assert.AreEqual(0f, SplitScreenMath.DeltaAngle(-135f, MoveDirection.YawOf(world)), 1e-3f);
        }

        [Test]
        public void TurnToward_TakesTheShortWayRound()
        {
            Assert.AreEqual(370f, MoveDirection.TurnToward(350f, 10f, 90f), Tolerance);
            Assert.AreEqual(-10f, MoveDirection.TurnToward(10f, 350f, 90f), Tolerance);
        }

        [Test]
        public void TurnToward_IsLimitedByTurnSpeed()
        {
            Assert.AreEqual(14.4f, MoveDirection.TurnToward(0f, 180f - 0.1f, 14.4f), Tolerance);
            Assert.AreEqual(30f, MoveDirection.TurnToward(0f, 30f, 45f), Tolerance);
        }
    }
}
