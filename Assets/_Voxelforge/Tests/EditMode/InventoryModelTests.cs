using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.5: the inventory window's tabs, cursor and scrolling, and the stick's repeat.</summary>
    public class InventoryModelTests
    {
        static List<ItemStack> Bag(int capacity, params (int slot, ushort id, ushort count)[] items)
        {
            var slots = new List<ItemStack>();
            for (int i = 0; i < capacity; i++)
                slots.Add(ItemStack.Empty);
            foreach ((int slot, ushort id, ushort count) in items)
                slots[slot] = new ItemStack(id, count);
            return slots;
        }

        [Test]
        public void AllTab_ShowsEverySlot_OtherTabsOnlyTheirItems()
        {
            List<ItemStack> bag = Bag(16, (2, BlockIds.Dirt, 5), (5, ItemIds.StonePickaxe, 1), (9, BlockIds.Log, 3));
            var model = new InventoryModel();
            model.Rebuild(bag, ItemRules.Default);
            Assert.AreEqual(16, model.Shown.Count);

            model.SetTab(InventoryTab.Blocks);
            model.Rebuild(bag, ItemRules.Default);
            CollectionAssert.AreEqual(new[] { 2, 9 }, model.Shown);
            Assert.AreEqual(2, model.CursorSlot, "The cursor starts on the tab's first item.");

            model.SetTab(InventoryTab.Gear);
            model.Rebuild(bag, ItemRules.Default);
            CollectionAssert.AreEqual(new[] { 5 }, model.Shown);

            model.SetTab(InventoryTab.Consumables);
            model.Rebuild(bag, ItemRules.Default);
            Assert.AreEqual(0, model.Shown.Count);
            Assert.AreEqual(0, model.CursorSocket, "An empty tab leaves the cursor on the bag sockets.");
            Assert.AreEqual(-1, model.CursorSlot);
        }

        [Test]
        public void StepTab_Wraps()
        {
            var model = new InventoryModel();
            model.StepTab(-1);
            Assert.AreEqual(InventoryTab.Materials, model.Tab);
            model.StepTab(+1);
            Assert.AreEqual(InventoryTab.All, model.Tab);
        }

        [Test]
        public void Move_StopsAtEdges_AndDropsIntoTheSockets()
        {
            var model = new InventoryModel();
            model.Rebuild(Bag(20), ItemRules.Default); // Rows of 8, 8, 4.
            model.Move(-1, -1);
            Assert.AreEqual(0, model.CursorCell, "Top-left stays put.");

            model.Move(7, 0);
            Assert.AreEqual(7, model.CursorCell);
            model.Move(1, 0);
            Assert.AreEqual(7, model.CursorCell, "No wrap at the right edge.");

            model.Move(0, 2);
            Assert.AreEqual(19, model.CursorCell, "The short last row: the nearest cell.");
            model.Move(0, 1);
            Assert.AreEqual(-1, model.CursorCell);
            Assert.AreEqual(1, model.CursorSocket, "Down from the last row: the socket under that column.");

            model.Move(5, 0);
            Assert.AreEqual(3, model.CursorSocket);
            model.Move(0, -1);
            Assert.AreEqual(-1, model.CursorSocket);
            Assert.AreEqual(19, model.CursorCell, "Back up into the grid.");
        }

        [Test]
        public void Scroll_FollowsTheCursor()
        {
            var model = new InventoryModel();
            model.Rebuild(Bag(80), ItemRules.Default); // 10 rows, 5 on screen.
            Assert.AreEqual(0, model.ScrollRow);
            model.Move(0, 5);
            Assert.AreEqual(5 * InventoryModel.Columns, model.CursorCell);
            Assert.AreEqual(1, model.ScrollRow, "Row 5 just came into view.");
            model.Move(0, 4);
            Assert.AreEqual(5, model.ScrollRow, "The last row shows at the bottom.");
            model.Move(0, -9);
            Assert.AreEqual(0, model.ScrollRow);

            model.Scroll(100);
            Assert.AreEqual(5, model.ScrollRow);
        }

        [Test]
        public void Rebuild_KeepsTheCursorInRange_WhenTheBagShrinks()
        {
            var model = new InventoryModel();
            model.Rebuild(Bag(48), ItemRules.Default);
            model.Move(0, 5);
            model.Rebuild(Bag(40), ItemRules.Default);
            Assert.AreEqual(39, model.CursorCell);
        }

        [Test]
        public void NavRepeat_StepsOnce_ThenRepeatsAfterAPause()
        {
            var nav = new NavRepeat();
            Assert.AreEqual(new int2(1, 0), nav.Update(new float2(0.9f, 0.2f), 0f), "The push steps at once.");
            Assert.AreEqual(int2.zero, nav.Update(new float2(0.9f, 0.2f), 0.2f), "Then a pause.");
            Assert.AreEqual(new int2(1, 0), nav.Update(new float2(0.9f, 0.2f), NavRepeat.FirstDelay + 0.01f));
            Assert.AreEqual(int2.zero, nav.Update(new float2(0.9f, 0.2f), NavRepeat.FirstDelay + 0.05f));
            Assert.AreEqual(new int2(1, 0), nav.Update(new float2(0.9f, 0.2f), NavRepeat.FirstDelay + NavRepeat.RepeatDelay + 0.02f));

            Assert.AreEqual(int2.zero, nav.Update(new float2(0.2f, 0.1f), 1f), "Let go.");
            Assert.AreEqual(new int2(0, -1), nav.Update(new float2(0.1f, 1f), 1.01f), "Stick up is a row up.");
        }
    }
}
