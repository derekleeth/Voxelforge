using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Crafting (Roadmap 3.5). Interact (unless aimed at a Storage Chest, <see cref="PlayerChest"/>) opens this player's recipe list in their own split-screen view
    /// (<see cref="PlayerHud"/>, CLAUDE.md Law #5) on the nearest station's tab, and the hero stands still while it's open
    /// (Roadmap 5.8). The move controls choose a tab (left, right) and a recipe (up, down), as do Select Next / Previous,
    /// and Place crafts it. The owning PC sends only the recipe number with <see cref="RequestCraftRpc"/>. <b>The host</b> checks it with
    /// <see cref="RecipeBook.Check"/>, including that the recipe's station is within reach of this hero and their skill
    /// (Roadmap 5.8), swaps the inputs for the output in the <see cref="PlayerBlockBag"/> all at once (Law #2), and lets
    /// the craft raise the skill.
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerBlockBag))]
    public sealed class PlayerCrafter : NetworkBehaviour
    {
        [Tooltip("Data/Items/RecipeBook.asset.")]
        [SerializeField] RecipeBook recipes;

        [Tooltip("A recipe that needs a station works within this many blocks of one (a box around the hero's middle).")]
        [FormerlySerializedAs("workbenchReach")]
        [SerializeField, Range(1, 8)] int stationReach = 4;

        [Tooltip("Host: the shortest time allowed between two crafts, in seconds.")]
        [SerializeField, Range(0f, 1f)] float minCraftInterval = 0.2f;

        [Tooltip("How long the \"Crafted ...!\" message stays up, in seconds.")]
        [SerializeField, Range(0.5f, 5f)] float messageSeconds = 2f;

        // How high above the feet the hero's middle is, where station reach is measured from.
        const float HeroMiddle = 0.9f;
        // How long "Blacksmithing 12" stays on the window, in seconds.
        const float SkillMessageSeconds = 3f;

        PlayerController _player;
        PlayerBlockBag _bag;
        PlayerChest _chest;
        PlayerGatherer _gatherer;
        PlayerProfessions _professions;
        PlayerInventory _inventory;
        readonly List<ItemStack> _stacks = new List<ItemStack>();
        // The window checks every row every GUI event: one scan of the blocks around the hero a frame is plenty.
        CraftStationMask _nearby;
        int _nearbyFrame = -1;

        // Owner state.
        bool _menuOpen;
        CraftingList _list;
        NavRepeat _nav;
        bool _sidewaysHeld;
        PlayerButtons _previousButtons;
        string _message;
        float _messageUntil;
        string _skillMessage;
        float _skillMessageUntil;

        // Host state.
        float _lastCraftTime = float.NegativeInfinity;

        public RecipeBook Recipes => recipes;

        /// <summary>Every PC: this hero made something at a station, for its sound (Roadmap 5.8).</summary>
        public event System.Action<CraftStation> Crafted;

        /// <summary>Owner only: the recipe list is showing, so Select and Place drive it instead of building.</summary>
        public bool IsMenuOpen => _menuOpen;

        /// <summary>Owner only: the tabs and rows the window shows.</summary>
        public CraftingList List => _list ??= new CraftingList(recipes.Recipes);

        /// <summary>Owner only: the highlighted recipe's place in the book, or -1 when the tab is empty.</summary>
        public int Cursor => List.CursorRecipe;

        /// <summary>Owner only: a short message after crafting, or null.</summary>
        public string Message => Time.time < _messageUntil ? _message : null;

        /// <summary>Owner only: "Blacksmithing 12" just after a skill went up, or null.</summary>
        public string SkillMessage => Time.time < _skillMessageUntil ? _skillMessage : null;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _bag = GetComponent<PlayerBlockBag>();
            _chest = GetComponent<PlayerChest>();
            _gatherer = GetComponent<PlayerGatherer>();
            _professions = GetComponent<PlayerProfessions>();
            _inventory = GetComponent<PlayerInventory>();
        }

        public override void OnNetworkSpawn()
        {
            if (_professions != null)
                _professions.SkilledUp += OnSkilledUp;
        }

        public override void OnNetworkDespawn()
        {
            if (_professions != null)
                _professions.SkilledUp -= OnSkilledUp;
            _menuOpen = false;
            base.OnNetworkDespawn();
        }

        void OnSkilledUp(ProfessionId profession, int skill)
        {
            _skillMessage = $"{profession} {skill}";
            _skillMessageUntil = Time.time + SkillMessageSeconds;
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner || recipes == null)
                return;

            // The bag or the map opening, or being knocked out, closes the window.
            if (_menuOpen && (_player.OtherWindowOpen || _player.IsKnockedOut))
                _menuOpen = false;

            // While the window is open it has the controls (PlayerController.WindowOpen), so it reads them as pressed.
            PlayerInputFrame input = _menuOpen ? _player.OwnerRawInput : _player.OwnerInput;
            PlayerButtons buttons = input.Buttons;
            PlayerButtons pressed = buttons & ~_previousButtons;
            _previousButtons = buttons;

            // Aiming at a herb (Roadmap 5.7), Interact gathers it; at a Storage Chest (Roadmap 4.5), it opens that.
            if ((pressed & PlayerButtons.Interact) != 0)
            {
                if (_menuOpen)
                    _menuOpen = false;
                else if ((_gatherer == null || !_gatherer.OwnerTryGatherAimed()) && (_chest == null || !_chest.OwnerTryOpenAimed()))
                {
                    _menuOpen = true;
                    List.OpenAt(CraftStations.Nearest(_player.World, transform.position + Vector3.up * HeroMiddle, stationReach));
                }
            }
            if (!_menuOpen || !_player.HasLanded)
                return;

            bool pad = _inventory != null && _inventory.Scheme == "Gamepad";
            float2 stick = input.Move + (pad ? PlayerInventory.DPad(buttons) : float2.zero);
            int2 step = _nav.Update(stick, Time.unscaledTime);
            // One tab a push: rows repeat while held, but repeating tabs would spin past the one you want.
            if (step.x != 0 && !_sidewaysHeld)
                List.StepTab(step.x);
            _sidewaysHeld = math.abs(stick.x) > NavRepeat.Deadzone;
            if (step.y != 0)
                List.Move(step.y);
            if ((pressed & PlayerButtons.SelectNext) != 0)
                List.Move(1);
            if ((pressed & PlayerButtons.SelectPrevious) != 0)
                List.Move(-1);

            if ((pressed & PlayerButtons.Place) != 0 && Cursor >= 0 && Check(Cursor) == CraftCheck.Ok)
                RequestCraftRpc((byte)Cursor);
        }

        /// <summary>Any PC that can see blocks: can this player craft recipe <paramref name="index"/> right now?</summary>
        public CraftCheck Check(int index)
        {
            _stacks.Clear();
            for (int i = 0; i < _bag.SlotCount; i++)
                _stacks.Add(_bag.GetSlot(i));
            return recipes.Check(index, _stacks, NearbyStations(), Skills, _bag.Rules);
        }

        /// <summary>The stations within reach. Only the host knows for sure (a remote PC has no world yet).</summary>
        public CraftStationMask NearbyStations()
        {
            if (_nearbyFrame != Time.frameCount)
            {
                _nearbyFrame = Time.frameCount;
                _nearby = CraftStations.ScanNear(_player.World, transform.position + Vector3.up * HeroMiddle, stationReach);
            }
            return _nearby;
        }

        ProfessionSkills Skills => _professions != null ? _professions.Skills : ProfessionSkills.Start;

        /// <summary>This hero's skill in <paramref name="profession"/>.</summary>
        public int SkillIn(ProfessionId profession) => Skills.Get(profession);

        /// <summary>
        /// How likely <paramref name="recipe"/> is to raise this hero's skill: the same colours as gathering. False for a
        /// recipe of no profession.
        /// </summary>
        public bool TryGetDifficulty(RecipeDefinition recipe, out GatherDifficulty difficulty)
        {
            difficulty = default;
            if (_professions == null || _professions.Book == null || !CraftStations.TryGetProfession(recipe.Station, out ProfessionId profession))
                return false;
            difficulty = ProfessionRules.Difficulty(Skills.Get(profession), recipe.RequiredSkill, _professions.Book);
            return true;
        }

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
                if (_professions != null && CraftStations.TryGetProfession(recipe.Station, out ProfessionId profession))
                    _professions.ServerPractised(profession, recipe.RequiredSkill);
                CraftedRpc(recipeIndex);
                CraftedAtRpc(recipe.Station);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        void CraftedAtRpc(CraftStation station) => Crafted?.Invoke(station);

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
