using System;
using NUnit.Framework;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 2.6: each chunk gets its own slots in the decoration buffers, and freed slots are reused.</summary>
    public class InstanceRangeAllocatorTests
    {
        [Test]
        public void Allocates_BackToBack_FromTheStart()
        {
            var allocator = new InstanceRangeAllocator(100);
            Assert.AreEqual(0, allocator.Allocate(10, out bool grew));
            Assert.IsFalse(grew);
            Assert.AreEqual(10, allocator.Allocate(5, out _));
            Assert.AreEqual(15, allocator.UsedCount);
            Assert.AreEqual(15, allocator.UsedEnd);
        }

        [Test]
        public void FreedRange_IsReused_AndMergesWithItsNeighbours()
        {
            var allocator = new InstanceRangeAllocator(100);
            int a = allocator.Allocate(10, out _);
            int b = allocator.Allocate(10, out _);
            int c = allocator.Allocate(10, out _);

            allocator.Free(a, 10);
            allocator.Free(b, 10); // Merges with a's range into one gap of 20.
            Assert.AreEqual(0, allocator.Allocate(20, out _), "The two freed ranges should have merged.");
            Assert.AreEqual(30, allocator.UsedEnd);

            allocator.Free(c, 10);
            Assert.AreEqual(20, allocator.UsedEnd, "Freeing the last range shrinks the used end.");
        }

        [Test]
        public void Grows_WhenNothingFits_KeepingExistingRanges()
        {
            var allocator = new InstanceRangeAllocator(16);
            int a = allocator.Allocate(12, out _);
            int b = allocator.Allocate(10, out bool grew);

            Assert.IsTrue(grew);
            Assert.AreEqual(0, a);
            Assert.AreEqual(12, b, "The new range starts right after the old one, using the old free tail.");
            Assert.GreaterOrEqual(allocator.Capacity, 22);
        }

        [Test]
        public void FreeingTwice_Throws()
        {
            var allocator = new InstanceRangeAllocator(16);
            int a = allocator.Allocate(4, out _);
            allocator.Free(a, 4);
            Assert.Throws<InvalidOperationException>(() => allocator.Free(a, 4));
        }
    }
}
