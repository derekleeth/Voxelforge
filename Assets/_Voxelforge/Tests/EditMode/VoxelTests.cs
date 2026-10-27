using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    public class VoxelTests
    {
        [Test]
        public void Voxel_IsFourBytes()
        {
            Assert.AreEqual(4, UnsafeUtility.SizeOf<Voxel>());
        }

        [Test]
        public void Default_IsAir()
        {
            Assert.IsTrue(default(Voxel).IsAir);
            Assert.AreEqual(Voxel.Air, default(Voxel));
        }

        [Test]
        public void WithLight_PacksSunAndBlockLightSeparately()
        {
            Voxel voxel = new Voxel(BlockIds.Stone).WithLight(12, 5);

            Assert.AreEqual(12, voxel.SunLight);
            Assert.AreEqual(5, voxel.BlockLight);
            Assert.AreEqual(BlockIds.Stone, voxel.BlockId);
        }

        [Test]
        public void WithLight_ClampsToFifteen()
        {
            Voxel voxel = default(Voxel).WithLight(200, 99);

            Assert.AreEqual(Voxel.MaxLight, voxel.SunLight);
            Assert.AreEqual(Voxel.MaxLight, voxel.BlockLight);
        }

        [Test]
        public void WithFacing_KeepsLightAndReservedStateBits()
        {
            var voxel = new Voxel(BlockIds.Dirt, light: 0x3A, state: 0b1010_0000);

            Voxel turned = voxel.WithFacing(BlockFacing.Down);

            Assert.AreEqual(BlockFacing.Down, turned.Facing);
            Assert.AreEqual(0x3A, turned.Light);
            Assert.AreEqual(0b1010_0000, turned.State & ~0b111);
        }

        [Test]
        public void Equality_ComparesAllFields()
        {
            Assert.AreEqual(new Voxel(BlockIds.Grass, 1, 2), new Voxel(BlockIds.Grass, 1, 2));
            Assert.AreNotEqual(new Voxel(BlockIds.Grass, 1, 2), new Voxel(BlockIds.Grass, 1, 3));
            Assert.AreNotEqual(new Voxel(BlockIds.Grass), new Voxel(BlockIds.Dirt));
        }
    }
}
