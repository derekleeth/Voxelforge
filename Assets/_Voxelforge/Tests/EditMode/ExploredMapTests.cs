using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.8: the map's fog of war, in cells of 16 blocks that only ever get explored.</summary>
    public class ExploredMapTests
    {
        [Test]
        public void CellOf_HandlesNegativeColumns()
        {
            Assert.AreEqual(new int2(0, 0), ExploredMap.CellOf(0f, 15.9f));
            Assert.AreEqual(new int2(1, 0), ExploredMap.CellOf(16f, 0.5f));
            Assert.AreEqual(new int2(-1, -1), ExploredMap.CellOf(-0.1f, -16f));
            Assert.AreEqual(new int2(-2, 0), ExploredMap.CellOf(-16.5f, 3f));
        }

        [Test]
        public void Reveal_ClearsARoundPatch()
        {
            var map = new ExploredMap();
            var added = new List<int2>();
            int count = map.Reveal(new int2(-5, 4), 3, added);

            Assert.AreEqual(count, added.Count);
            Assert.AreEqual(map.Count, count);
            Assert.IsTrue(map.IsExplored(new int2(-5, 4)), "the hero's own cell");
            Assert.IsTrue(map.IsExplored(new int2(-2, 4)), "straight out to the radius");
            Assert.IsTrue(map.IsExplored(new int2(-5, 1)));
            Assert.IsFalse(map.IsExplored(new int2(-2, 1)), "not the corners: it's round");
            Assert.IsFalse(map.IsExplored(new int2(-1, 4)), "nothing past the radius");
        }

        [Test]
        public void Reveal_OnlyReportsNewCells_AndCountsVersions()
        {
            var map = new ExploredMap();
            map.Reveal(new int2(0, 0), 2);
            int version = map.Version;
            var added = new List<int2>();

            Assert.AreEqual(0, map.Reveal(new int2(0, 0), 2, added), "standing still explores nothing new");
            Assert.AreEqual(0, added.Count);
            Assert.AreEqual(version, map.Version);

            int moved = map.Reveal(new int2(1, 0), 2, added);
            Assert.Greater(moved, 0);
            Assert.Less(moved, 10, "one step east only adds the new edge");
            Assert.AreEqual(version + 1, map.Version);
        }

        [Test]
        public void Add_MergesABatch_AndRaisesExploredWithOnlyTheNewCells()
        {
            var map = new ExploredMap(new[] { new int2(1, 1) });
            IReadOnlyList<int2> heard = null;
            map.Explored += cells => heard = cells;

            Assert.AreEqual(2, map.Add(new[] { new int2(1, 1), new int2(2, 1), new int2(-9, 40) }));
            CollectionAssert.AreEquivalent(new[] { new int2(2, 1), new int2(-9, 40) }, heard);
            Assert.AreEqual(3, map.Count);
        }

        [Test]
        public void Snapshot_IsACopy()
        {
            var map = new ExploredMap();
            map.Reveal(int2.zero, 1);
            HashSet<int2> snapshot = map.Snapshot();
            map.Reveal(new int2(10, 10), 1);
            Assert.Less(snapshot.Count, map.Count);
        }
    }
}
