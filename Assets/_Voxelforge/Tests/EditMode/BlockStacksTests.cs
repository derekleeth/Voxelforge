using NUnit.Framework;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 3.2: the block bag's counting, without any networking.</summary>
    public class BlockStacksTests
    {
        [Test]
        public void Add_StacksByBlock_InTheOrderFirstPickedUp()
        {
            var stacks = new BlockStacks();
            Assert.AreEqual(3, stacks.Add(BlockIds.Dirt, 3, out int dirt));
            Assert.AreEqual(1, stacks.Add(BlockIds.Log, 1, out int log));
            Assert.AreEqual(2, stacks.Add(BlockIds.Dirt, 2, out int dirtAgain));

            Assert.AreEqual(0, dirt);
            Assert.AreEqual(1, log);
            Assert.AreEqual(dirt, dirtAgain, "The same block keeps its slot.");
            Assert.AreEqual(5, stacks.CountOf(BlockIds.Dirt));
            Assert.AreEqual(1, stacks.CountOf(BlockIds.Log));
            Assert.AreEqual(0, stacks.CountOf(BlockIds.Stone));
        }

        [Test]
        public void Add_StopsAtTheCap()
        {
            var stacks = new BlockStacks();
            stacks.Add(BlockIds.Stone, BlockStacks.MaxPerBlock - 2, out _);
            Assert.AreEqual(2, stacks.Add(BlockIds.Stone, 5, out int index));
            Assert.AreEqual(0, index);
            Assert.AreEqual(0, stacks.Add(BlockIds.Stone, 1, out index));
            Assert.AreEqual(-1, index, "Nothing changed.");
            Assert.AreEqual(BlockStacks.MaxPerBlock, stacks.CountOf(BlockIds.Stone));
        }

        [Test]
        public void TryTake_AllOrNothing_AndEmptySlotsStay()
        {
            var stacks = new BlockStacks();
            stacks.Add(BlockIds.Dirt, 2, out _);
            stacks.Add(BlockIds.Log, 1, out _);

            Assert.IsFalse(stacks.TryTake(BlockIds.Dirt, 3, out int index));
            Assert.AreEqual(-1, index);
            Assert.AreEqual(2, stacks.CountOf(BlockIds.Dirt));

            Assert.IsTrue(stacks.TryTake(BlockIds.Dirt, 2, out index));
            Assert.AreEqual(0, index);
            Assert.AreEqual(0, stacks.CountOf(BlockIds.Dirt));
            Assert.AreEqual(2, stacks.Stacks.Count, "An empty slot stays, so Log keeps index 1.");
            Assert.AreEqual(BlockIds.Log, stacks.Stacks[1].BlockId);

            Assert.IsFalse(stacks.TryTake(BlockIds.Stone, 1, out _));
            Assert.IsFalse(stacks.TryTake(BlockIds.Log, 0, out _));
        }
    }
}
