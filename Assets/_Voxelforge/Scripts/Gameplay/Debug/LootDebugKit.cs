using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A play-test key for loot (Roadmap 5.5), host only. <b>F2</b> gives every hero one of each piece of gear and 5 of
    /// each crafting material, straight into their bag, so the bag, the rarity colours and (from step C) equipping can be
    /// tried without hunting. Whatever doesn't fit stays out.
    /// </summary>
    public sealed class LootDebugKit : MonoBehaviour
    {
        [SerializeField] NetworkManager networkManager;

        [SerializeField] PlayerRegistry players;

        [Tooltip("Data/Items/ItemRegistry.asset: which items are gear and materials.")]
        [SerializeField] ItemRegistry items;

        [SerializeField, Range(1, 99)] int materialCount = 5;

        bool IsServer => networkManager != null && networkManager.IsServer;

        void Update()
        {
            if (!IsServer || players == null || items == null)
                return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.f2Key.wasPressedThisFrame)
                return;
            foreach (PlayerController hero in players.Players)
            {
                if (hero != null && hero.TryGetComponent(out PlayerBlockBag bag))
                    Give(bag);
            }
        }

        void Give(PlayerBlockBag bag)
        {
            int missed = 0;
            foreach (ItemDefinition item in items.Items)
            {
                if (item == null)
                    continue;
                int amount = item.IsWornGear ? 1 : item.Category == ItemCategory.Materials && !ItemIds.IsBlock(item.Id) ? materialCount : 0;
                if (amount == 0)
                    continue;
                int added = bag.ServerAdd(item.Id, amount);
                if (added > 0)
                    bag.ServerTellPickedUp(item.Id, added);
                missed += amount - added;
            }
            if (missed > 0)
                Debug.Log($"[Voxelforge] F2: {missed} loot items didn't fit in {bag.name}'s bag.", this);
        }
    }
}
