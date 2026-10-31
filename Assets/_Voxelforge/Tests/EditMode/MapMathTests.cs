using NUnit.Framework;
using Unity.Mathematics;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.8: north-up map placement, edge arrows and marker turning.</summary>
    public class MapMathTests
    {
        const float Tolerance = 1e-4f;

        static void AssertClose(float2 expected, float2 actual) =>
            Assert.That(math.distance(expected, actual), Is.LessThan(Tolerance), $"expected {expected}, got {actual}");

        [Test]
        public void ToMap_PutsNorthUpAndEastRight()
        {
            var center = new float2(100f, -50f);
            AssertClose(new float2(0f, 20f), MapMath.ToMap(new float2(100f, -40f), center, 2f));
            AssertClose(new float2(-6f, 0f), MapMath.ToMap(new float2(97f, -50f), center, 2f));
        }

        [Test]
        public void ToWorld_UndoesToMap()
        {
            var center = new float2(-321.5f, 77f);
            var world = new float2(-300f, 12.25f);
            AssertClose(world, MapMath.ToWorld(MapMath.ToMap(world, center, 1.3f), center, 1.3f));
        }

        [Test]
        public void ClampToCircle_LeavesInsideAlone_AndPullsOutsideOntoTheRim()
        {
            var inside = new float2(3f, 4f);
            Assert.IsFalse(MapMath.ClampToCircle(ref inside, 10f));
            AssertClose(new float2(3f, 4f), inside);

            var outside = new float2(30f, 40f);
            Assert.IsTrue(MapMath.ClampToCircle(ref outside, 10f));
            AssertClose(new float2(6f, 8f), outside);
        }

        [Test]
        public void ClampToRect_KeepsTheDirection()
        {
            var p = new float2(400f, 100f);
            Assert.IsTrue(MapMath.ClampToRect(ref p, new float2(200f, 150f)));
            AssertClose(new float2(200f, 50f), p);

            var tall = new float2(-10f, -600f);
            Assert.IsTrue(MapMath.ClampToRect(ref tall, new float2(200f, 150f)));
            AssertClose(new float2(-2.5f, -150f), tall);

            var inside = new float2(199f, -149f);
            Assert.IsFalse(MapMath.ClampToRect(ref inside, new float2(200f, 150f)));
        }

        [Test]
        public void ArrowRotation_TurnsAnUpArrowToPointThatWay()
        {
            Assert.AreEqual(0f, MapMath.ArrowRotation(new float2(0f, 5f)), Tolerance, "up");
            Assert.AreEqual(90f, MapMath.ArrowRotation(new float2(-5f, 0f)), Tolerance, "left is a quarter turn anticlockwise");
            Assert.AreEqual(-90f, MapMath.ArrowRotation(new float2(5f, 0f)), Tolerance, "right");
            Assert.AreEqual(180f, math.abs(MapMath.ArrowRotation(new float2(0f, -5f))), Tolerance, "down");
        }

        [Test]
        public void HeadingRotation_FacingEastPointsRight()
        {
            // A hero facing east (yaw 90) turns the up arrow a quarter turn clockwise, which UI calls -90.
            Assert.AreEqual(-90f, MapMath.HeadingRotation(90f), Tolerance);
        }
    }
}
