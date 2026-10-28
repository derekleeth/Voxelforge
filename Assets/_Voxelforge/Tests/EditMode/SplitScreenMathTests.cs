using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    public class SplitScreenMathTests
    {
        const float Merge = 15f;
        const float Tolerance = 1e-4f;

        static void AssertClose(float3 expected, float3 actual) =>
            Assert.That(math.distance(expected, actual), Is.LessThan(Tolerance), $"expected {expected}, got {actual}");

        [Test]
        public void CloseTogether_IsMerged_FarApart_IsSplit()
        {
            Assert.IsFalse(SplitScreenMath.IsSplit(10f, Merge));
            Assert.IsFalse(SplitScreenMath.IsSplit(15f, Merge));
            Assert.IsTrue(SplitScreenMath.IsSplit(15.01f, Merge));
        }

        [Test]
        public void WhileMerged_BothCamerasLookAtTheMidpoint()
        {
            float3 a = new float3(0f, 70f, 0f);
            float3 b = new float3(8f, 72f, 6f); // 10.2 m apart
            float3 midpoint = (a + b) * 0.5f;

            AssertClose(midpoint, SplitScreenMath.FocusTarget(a, b, Merge));
            AssertClose(midpoint, SplitScreenMath.FocusTarget(b, a, Merge));
        }

        [Test]
        public void ExactlyAtTheMergeDistance_SplitCamerasStillLookAtTheMidpoint()
        {
            // The seamless split: both split cameras see exactly what the shared camera saw.
            float3 a = new float3(0f, 64f, 0f);
            float3 b = new float3(9f, 64f, 12f); // exactly 15 m apart
            float3 midpoint = (a + b) * 0.5f;

            AssertClose(midpoint, SplitScreenMath.FocusTarget(a, b, Merge));
            AssertClose(midpoint, SplitScreenMath.FocusTarget(b, a, Merge));
        }

        [Test]
        public void FarApart_EachCameraStaysHalfTheMergeDistanceFromItsPlayer_TowardTheOther()
        {
            float3 a = new float3(0f, 64f, 0f);
            float3 b = new float3(100f, 64f, 0f);

            AssertClose(new float3(7.5f, 64f, 0f), SplitScreenMath.FocusTarget(a, b, Merge));
            AssertClose(new float3(92.5f, 64f, 0f), SplitScreenMath.FocusTarget(b, a, Merge));
        }

        [Test]
        public void ViewDistance_PullsBackUntilTheSplit_ThenHolds()
        {
            Assert.AreEqual(7f, SplitScreenMath.ViewDistance(7f, 0.6f, 0f, Merge), Tolerance);
            Assert.AreEqual(13f, SplitScreenMath.ViewDistance(7f, 0.6f, 10f, Merge), Tolerance);
            Assert.AreEqual(16f, SplitScreenMath.ViewDistance(7f, 0.6f, 15f, Merge), Tolerance);
            Assert.AreEqual(16f, SplitScreenMath.ViewDistance(7f, 0.6f, 80f, Merge), Tolerance);
        }

        [Test]
        public void LookFreedom_IsZeroAtTheMerge_AndFullAfterTheBlend()
        {
            Assert.AreEqual(0f, SplitScreenMath.LookFreedom(10f, Merge, 7f), Tolerance);
            Assert.AreEqual(0f, SplitScreenMath.LookFreedom(15f, Merge, 7f), Tolerance);
            Assert.AreEqual(0.5f, SplitScreenMath.LookFreedom(18.5f, Merge, 7f), Tolerance);
            Assert.AreEqual(1f, SplitScreenMath.LookFreedom(22f, Merge, 7f), Tolerance);
            Assert.AreEqual(1f, SplitScreenMath.LookFreedom(60f, Merge, 7f), Tolerance);
        }

        [Test]
        public void Angles_TurnTheShortWayRound()
        {
            Assert.AreEqual(20f, SplitScreenMath.DeltaAngle(350f, 10f), Tolerance);
            Assert.AreEqual(-20f, SplitScreenMath.DeltaAngle(10f, 350f), Tolerance);
            Assert.AreEqual(0f, SplitScreenMath.DeltaAngle(-720f, 0f), Tolerance);
            Assert.AreEqual(360f, SplitScreenMath.MidAngle(350f, 10f), Tolerance);
            Assert.AreEqual(355f, SplitScreenMath.BlendAngle(350f, 10f, 0.25f), Tolerance);
        }

        [Test]
        public void SplitNormal_PointsFromTheFirstPlayerToTheSecond_OnScreen()
        {
            // Camera looking north: east is screen-right.
            float2 normal = SplitScreenMath.SplitNormal(new float3(0f, 64f, 0f), new float3(30f, 64f, 0f), new float3(1f, 0f, 0f), new float3(0f, 1f, 0f));

            Assert.AreEqual(1f, normal.x, Tolerance);
            Assert.AreEqual(0f, normal.y, Tolerance);
        }

        [Test]
        public void SplitNormal_TurnsWithTheCamera()
        {
            // Camera looking east: its right is south, so a player due east is straight ahead and one due south is screen-right.
            Quaternion facingEast = Quaternion.Euler(35f, 90f, 0f);
            float3 right = facingEast * Vector3.right;
            float3 up = facingEast * Vector3.up;

            float2 normal = SplitScreenMath.SplitNormal(new float3(0f, 64f, 0f), new float3(0f, 64f, -30f), right, up);

            Assert.AreEqual(1f, normal.x, Tolerance);
            Assert.AreEqual(0f, normal.y, Tolerance);
        }

        [Test]
        public void SplitNormal_HasLengthOne_EvenForDiagonals()
        {
            float2 normal = SplitScreenMath.SplitNormal(float3.zero, new float3(20f, 3f, 20f), new float3(1f, 0f, 0f), math.normalize(new float3(0f, 1f, 1f)));

            Assert.AreEqual(1f, math.length(normal), Tolerance);
        }

        [Test]
        public void EachPlayer_EndsUpOnTheirOwnSideOfTheLine()
        {
            float2 normal = math.normalize(new float2(1f, 0.5f));
            const float aspect = 16f / 9f;

            // A player shown toward the bottom-left belongs to the first player's side, top-right to the second's.
            Assert.Less(SplitScreenMath.SideOfSplit(new float2(0.2f, 0.3f), normal, aspect), 0f);
            Assert.Greater(SplitScreenMath.SideOfSplit(new float2(0.8f, 0.7f), normal, aspect), 0f);
            Assert.AreEqual(0f, SplitScreenMath.SideOfSplit(new float2(0.5f, 0.5f), normal, aspect), Tolerance);
        }

        [Test]
        public void GridRects_OnePlayerGetsTheWholeScreen()
        {
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), SplitScreenMath.GridRect(0, 1));
        }

        [Test]
        public void GridRects_FourPlayersTileTheScreen_InReadingOrder()
        {
            Assert.AreEqual(new Rect(0f, 0.5f, 0.5f, 0.5f), SplitScreenMath.GridRect(0, 4)); // top-left
            Assert.AreEqual(new Rect(0.5f, 0.5f, 0.5f, 0.5f), SplitScreenMath.GridRect(1, 4)); // top-right
            Assert.AreEqual(new Rect(0f, 0f, 0.5f, 0.5f), SplitScreenMath.GridRect(2, 4)); // bottom-left
            Assert.AreEqual(new Rect(0.5f, 0f, 0.5f, 0.5f), SplitScreenMath.GridRect(3, 4)); // bottom-right

            float area = 0f;
            for (int i = 0; i < 4; i++)
                area += SplitScreenMath.GridRect(i, 4).width * SplitScreenMath.GridRect(i, 4).height;
            Assert.AreEqual(1f, area, Tolerance);
        }
    }
}
