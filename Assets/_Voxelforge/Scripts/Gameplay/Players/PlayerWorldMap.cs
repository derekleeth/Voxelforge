using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One player's World Map window (Roadmap 4.8), owner-side. The Map button (M, Numpad + on the right-hand keyboard,
    /// or Select on a gamepad) opens it in their own view (<see cref="WorldMapView"/>, CLAUDE.md Law #5). While it's open
    /// <see cref="PlayerController"/> stops the hero and hands the controls to this: move pans, the bag's next and
    /// previous buttons zoom, Jump goes back to the hero, and Place hearths home (the gamepad's only way home now). Map,
    /// Inventory or a B tap close it. Hearthing is only a request: <see cref="PlayerHearth"/> asks the host (Law #2).
    /// Interact switches the resource heat map (Roadmap 5.7) between Off, Ore and Herbs, once Mining or Herbalism is high
    /// enough; the minimap shows the same one.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerWorldMap : MonoBehaviour
    {
        PlayerController _player;
        PlayerInventory _inventory;
        PlayerClassMenu _classMenu;
        PlayerHearth _hearth;
        PlayerProfessions _professions;
        readonly WorldMapModel _model = new WorldMapModel();
        PlayerButtons _previousButtons;

        /// <summary>Owner only: the map is showing and has this player's controls.</summary>
        public bool IsOpen { get; private set; }

        public WorldMapModel Model => _model;

        /// <summary>The control scheme in use (from the HUD), for the hints.</summary>
        public string Scheme { get; set; }

        /// <summary>Every PC: the hero has a Home Totem to hearth to, for the hint.</summary>
        public bool HasHome => _hearth != null && _hearth.HasHome;

        /// <summary>
        /// Owner only: which resource heat map their World Map and minimap show (Roadmap 5.7). Off whenever the skill
        /// that unlocks it drops below the book's (a play-test reset).
        /// </summary>
        public HeatMode HeatMode
        {
            get
            {
                if (_heatMode == HeatMode.Off || _professions == null ||
                    MapHeat.IsUnlocked(_heatMode, _professions.Skills, _professions.Book))
                    return _heatMode;
                return HeatMode.Off;
            }
        }

        /// <summary>Owner only: Mining or Herbalism is high enough for a heat map, so the map says how to show it.</summary>
        public bool HeatUnlocked => _professions != null &&
                                    (MapHeat.IsUnlocked(HeatMode.Ore, _professions.Skills, _professions.Book) ||
                                     MapHeat.IsUnlocked(HeatMode.Herbs, _professions.Skills, _professions.Book));

        HeatMode _heatMode;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _inventory = GetComponent<PlayerInventory>();
            _classMenu = GetComponent<PlayerClassMenu>();
            _hearth = GetComponent<PlayerHearth>();
            _professions = GetComponent<PlayerProfessions>();
        }

        float2 Hero
        {
            get
            {
                Vector3 hero = _player.VisualRoot.position;
                return new float2(hero.x, hero.z);
            }
        }

        void Update()
        {
            if (!_player.IsSpawned || !_player.IsOwner)
            {
                IsOpen = false;
                return;
            }

            PlayerInputFrame input = _player.OwnerRawInput;
            PlayerButtons pressed = input.Buttons & ~_previousButtons;
            _previousButtons = input.Buttons;
            if (_player.ChoosingRace || !_player.HasLanded)
            {
                IsOpen = false;
                return;
            }

            if (!IsOpen)
            {
                bool otherWindow = (_inventory != null && _inventory.IsOpen) || (_classMenu != null && _classMenu.IsOpen);
                if ((pressed & PlayerButtons.Map) != 0 && !otherWindow)
                {
                    _model.Open(Hero);
                    IsOpen = true;
                }
                return;
            }

            // Closing: Map again, Inventory (which then opens the bag), or a tap of B on a gamepad.
            if ((pressed & (PlayerButtons.Map | PlayerButtons.Inventory | PlayerButtons.ClassMenu)) != 0 ||
                (Scheme == "Gamepad" && (pressed & PlayerButtons.ActionSlot9) != 0))
            {
                IsOpen = false;
                return;
            }

            _model.Tick(input.Move, Hero, Time.unscaledDeltaTime);
            if ((pressed & PlayerButtons.SelectNext) != 0)
                _model.ZoomBy(+1);
            if ((pressed & PlayerButtons.SelectPrevious) != 0)
                _model.ZoomBy(-1);
            if ((pressed & PlayerButtons.Jump) != 0)
                _model.Recenter(Hero);
            if ((pressed & PlayerButtons.Interact) != 0 && _professions != null)
                _heatMode = MapHeat.Next(HeatMode, _professions.Skills, _professions.Book);
            if ((pressed & PlayerButtons.Place) != 0 && _hearth != null)
            {
                _hearth.OwnerRequestHearth();
                IsOpen = false;
            }
        }

        /// <summary>Closes the map.</summary>
        public void Close() => IsOpen = false;
    }
}
