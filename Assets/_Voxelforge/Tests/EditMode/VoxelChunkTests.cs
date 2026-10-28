using System;
using NUnit.Framework;
using Unity.Mathematics;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    public class VoxelChunkTests
    {
        static readonly Voxel Stone = new Voxel(BlockIds.Stone);
        static readonly Voxel Dirt = new Voxel(BlockIds.Dirt);

        VoxelChunk _chunk;

        [SetUp]
        public void SetUp() => _chunk = new VoxelChunk(new ChunkCoord(1, -2, 3));

        [TearDown]
        public void TearDown() => _chunk.Dispose();

        [Test]
        public void NewChunk_IsUniformAir()
        {
            Assert.IsTrue(_chunk.IsUniform);
            Assert.AreEqual(Voxel.Air, _chunk.GetVoxel(new int3(5, 6, 7)));
            Assert.AreEqual(0, _chunk.Version);
        }

        [Test]
        public void NewChunk_WithFill_ReturnsFillEverywhere()
        {
            using var chunk = new VoxelChunk(default, Stone);

            Assert.AreEqual(Stone, chunk.GetVoxel(new int3(0, 0, 0)));
            Assert.AreEqual(Stone, chunk.GetVoxel(new int3(31, 31, 31)));
        }

        [Test]
        public void SetVoxel_SameAsUniform_StaysUniformAndDoesNotBumpVersion()
        {
            Assert.IsFalse(_chunk.SetVoxel(new int3(1, 1, 1), Voxel.Air));
            Assert.IsTrue(_chunk.IsUniform);
            Assert.AreEqual(0, _chunk.Version);
        }

        [Test]
        public void SetVoxel_DifferentBlock_AllocatesAndKeepsOtherBlocks()
        {
            Assert.IsTrue(_chunk.SetVoxel(new int3(1, 2, 3), Stone));

            Assert.IsFalse(_chunk.IsUniform);
            Assert.AreEqual(1, _chunk.Version);
            Assert.AreEqual(Stone, _chunk.GetVoxel(new int3(1, 2, 3)));
            Assert.AreEqual(Voxel.Air, _chunk.GetVoxel(new int3(3, 2, 1)));
        }

        [Test]
        public void SetVoxel_Unchanged_ReturnsFalse()
        {
            _chunk.SetVoxel(new int3(0, 0, 0), Stone);

            Assert.IsFalse(_chunk.SetVoxel(new int3(0, 0, 0), Stone));
            Assert.AreEqual(1, _chunk.Version);
        }

        [Test]
        public void Fill_ReplacesEverythingAndFreesArray()
        {
            _chunk.SetVoxel(new int3(4, 4, 4), Stone);

            _chunk.Fill(Dirt);

            Assert.IsTrue(_chunk.IsUniform);
            Assert.AreEqual(Dirt, _chunk.GetVoxel(new int3(4, 4, 4)));
            Assert.AreEqual(2, _chunk.Version);
        }

        [Test]
        public void Compact_CollapsesChunkThatBecameUniform()
        {
            _chunk.SetVoxel(new int3(9, 9, 9), Stone);
            Assert.IsFalse(_chunk.Compact());

            _chunk.SetVoxel(new int3(9, 9, 9), Voxel.Air);
            Assert.IsTrue(_chunk.Compact());

            Assert.IsTrue(_chunk.IsUniform);
            Assert.AreEqual(Voxel.Air, _chunk.UniformVoxel);
        }

        [Test]
        public void GetReadOnlyVoxels_ThrowsForUniformChunk()
        {
            Assert.Throws<InvalidOperationException>(() => _chunk.GetReadOnlyVoxels());
        }

        [Test]
        public void GetReadOnlyVoxels_MatchesGetVoxel()
        {
            var local = new int3(31, 0, 17);
            _chunk.SetVoxel(local, Dirt);

            Assert.AreEqual(Dirt, _chunk.GetReadOnlyVoxels()[VoxelMath.ToIndex(local)]);
        }

        [Test]
        public void GetVoxel_OutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _chunk.GetVoxel(new int3(32, 0, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => _chunk.GetVoxel(-1));
        }

        [Test]
        public void CopyFrom_ReplacesEveryBlockAndBumpsVersion()
        {
            var source = new Unity.Collections.NativeArray<Voxel>(VoxelConstants.ChunkVolume, Unity.Collections.Allocator.Temp);
            source[5] = Stone;
            source[VoxelConstants.ChunkVolume - 1] = Dirt;

            _chunk.CopyFrom(source);
            source.Dispose();

            Assert.IsFalse(_chunk.IsUniform);
            Assert.AreEqual(1, _chunk.Version);
            Assert.AreEqual(Stone, _chunk.GetVoxel(5));
            Assert.AreEqual(Dirt, _chunk.GetVoxel(VoxelConstants.ChunkVolume - 1));
            Assert.AreEqual(Voxel.Air, _chunk.GetVoxel(0));
        }

        [Test]
        public void CopyFrom_WrongLength_Throws()
        {
            using var source = new Unity.Collections.NativeArray<Voxel>(10, Unity.Collections.Allocator.Temp);
            Assert.Throws<ArgumentException>(() => _chunk.CopyFrom(source));
        }

        [Test]
        public void Dispose_FreesArrayAndBlocksFurtherUse()
        {
            _chunk.SetVoxel(new int3(0, 0, 0), Stone);

            _chunk.Dispose();
            _chunk.Dispose(); // Safe to call twice.

            Assert.IsTrue(_chunk.IsDisposed);
            Assert.IsTrue(_chunk.IsUniform, "The voxel array should be freed.");
            Assert.Throws<ObjectDisposedException>(() => _chunk.GetVoxel(0));
        }
    }
}
