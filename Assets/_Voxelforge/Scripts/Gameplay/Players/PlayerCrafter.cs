using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Crafting (Roadmap 3.5). Interact opens this player's recipe list in their own split-screen view
    /// (<see cref="PlayerHud"/>, CLAUDE.md Law #5); Select Next / Previous choose a recipe and Place crafts it. The owning
    /// PC sends only the recipe number with <see cref="RequestCraftRpc"/>. <b>The host</b> checks it with
    /// <see cref="RecipeBook.Check"/>, including that a Workbench block is within reach of this hero, and swaps the
    /// inputs for the output in the <see cref="PlayerBlockBag"/> all at once (Law #2).
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerBlockBag))]
    public sealed class PlayerCrafter : NetworkBehaviour
    {
        [Tooltip("Data/Items/RecipeBook.asset.")]
        [SerializeField] RecipeBook recipes;

        [Tooltip("A recipe that needs a workbench works within this many blocks of one (a box around the hero's middle).")]
        [SerializeField, Range(1, 8)] int workbenchReach = 4;

        [Tooltip("Host: the shortest time allowed between two crafts, in seconds.")]
        [SerializeField, Range(0f, 1f)] float minCraftInterval = 0.2f;

        [Tooltip("How long the \"Crafted ...!\" message stays up, in seconds.")]
        [SerializeField, Range(0.5f, 5f)] float messageSeconds = 2f;

        // How high above the feet the hero's middle is, where workbench reach is measured from.
        const float HeroMiddle = 0.9f;

        PlayerController _player;
        PlayerBlockBag _bag;
        readonly List<ItemStack> _stacks = new List<ItemStack>();

        // Owner state.
        bool _menuOpen;
        int _cursor;
        PlayerButtons _previousButtons;
        string _message;
        float _messageUntil;

        // Host state.
        float _lastCraftTime = float.NegativeInfinity;

        public RecipeBook Recipes => recipes;

        /// <summary>Owner only: the recipe list is showing, so Select and Place drive it instead of building.</summary>
        public bool IsMenuOpen => _menuOpen;

        /// <summary>Owner only: the highlighted recipe.</summary>
        public int Cursor => _cursor;

        /// <summary>Owner only: a short message after crafting, or null.</summary>
        public string Message => Time.time < _messageUntil ? _message : null;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _bag = GetComponent<PlayerBlockBag>();
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner || recipes == null)
                return;

            PlayerButtons buttons = _player.OwnerInput.Buttons;
            PlayerButtons pressed = buttons & ~_previousButtons;
            _previousButtons = buttons;

            if ((pressed & PlayerButtons.Interact) != 0)
                _menuOpen = !_menuOpen;
            if (!_menuOpen || !_player.HasLanded)
                return;

            int count = recipes.Recipes.Count;
            if (count == 0)
                return;
            if ((pressed & PlayerButtons.SelectNext) != 0)
                _cursor = (_cursor + 1) % count;
            if ((pressed & PlayerButtons.SelectPrevious) != 0)
                _cursor = (_cursor + count - 1) % count;
            _cursor = Mathf.Clamp(_cursor, 0, count - 1);

            if ((pressed & PlayerButtons.Place) != 0 && Check(_cursor) == CraftCheck.Ok)
                RequestCraftRpc((byte)_cursor);
        }

        /// <summary>Any PC that can see blocks: can this player craft recipe <paramref name="index"/> right now?</summary>
        public CraftCheck Check(int index)
        {
            _stacks.Clear();
            for (int i = 0; i < _bag.SlotCount; i++)
                _stacks.Add(_bag.GetSlot(i));
            return recipes.Check(index, _stacks, IsNearWorkbench(), _bag.Rules);
        }

        /// <summary>True if a Workbench block is within reach. Only the host knows for sure (a remote PC has no world yet).</summary>
        public bool IsNearWorkbench() =>
            RecipeBook.IsNearBlock(_player.World, transform.position + Vector3.up * HeroMiddle, BlockIds.Workbench, workbenchReach);

        [Rpc(SendTo.Server)]
        void RequestCraftRpc(byte recipeIndex, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || recipes == null || !_player.HasLanded)
                return;
            if (Time.time - _lastCraftTime < minCraftInterval)
                return;

            CraftCheck check = Check(recipeIndex);
            if (check == CraftCheck.Ok && recipes.TryGet(recipeIndex, out RecipeDefinition recipe) &&
                _bag.ServerTryExchange(recipe.InputStacks(), recipe.Output.ToStack()))
            {
                _lastCraftTime = Time.time;
                CraftedRpc(recipeIndex);
            }
        }

        [Rpc(SendTo.Owner)]
        void CraftedRpc(byte recipeIndex)
        {
            if (recipes == null || !recipes.TryGet(recipeIndex, out RecipeDefinition recipe))
                return;
            ItemAmount output = recipe.Output;
            string name = output.item != null ? output.item.DisplayName : output.block != null ? output.block.DisplayName : "something";
            _message = output.count > 1 ? $"Crafted {output.count} {name}!" : $"Crafted {name}!";
            _messageUntil = Time.time + messageSeconds;
        }
    }
}
