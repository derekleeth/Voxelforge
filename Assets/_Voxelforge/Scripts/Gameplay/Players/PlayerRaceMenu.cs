using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One player's race window (Roadmap 4.7), owner-side. It opens by itself in their own view
    /// (<see cref="RacePickerView"/>, CLAUDE.md Law #5) as soon as the hero has landed and the host says they haven't chosen
    /// a race, and it can't be closed until they do: race is chosen once. While it's open
    /// <see cref="PlayerController"/> stops the hero, every other window stays shut, and this has the controls: left and
    /// right pick a race card (number keys jump to one), Jump or Interact chooses it. Choosing is only a request:
    /// <see cref="PlayerRace"/> asks the host, which decides (Law #2), and the window closes when the host says it's done.
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerRace))]
    public sealed class PlayerRaceMenu : MonoBehaviour
    {
        [Tooltip("Seconds after asking before the window lets the player ask again, if the host hasn't answered.")]
        [SerializeField, Range(0.2f, 5f)] float retryAfter = 1.5f;

        PlayerController _player;
        PlayerRace _race;
        readonly ClassPickerModel _model = new ClassPickerModel();
        NavRepeat _nav;
        PlayerButtons _previousButtons;
        float _askedAt = float.NegativeInfinity;

        /// <summary>Owner only: the window is showing and has this player's controls.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Owner only: they've chosen and are waiting for the host to agree.</summary>
        public bool IsWaiting => Time.time - _askedAt < retryAfter;

        public ClassPickerModel Model => _model;

        public PlayerRace Race => _race;

        /// <summary>The control scheme in use (from the HUD), for the hints.</summary>
        public string Scheme { get; set; }

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _race = GetComponent<PlayerRace>();
        }

        void Update()
        {
            if (!_player.IsSpawned || !_player.IsOwner || _race.HasChosen)
            {
                IsOpen = false;
                return;
            }

            PlayerInputFrame input = _player.OwnerRawInput;
            PlayerButtons pressed = input.Buttons & ~_previousButtons;
            _previousButtons = input.Buttons;

            if (!IsOpen)
            {
                // Wait for the ground, so the window doesn't open over the loading fall.
                if (_player.HasLanded)
                    Open();
                return;
            }

            bool pad = Scheme == "Gamepad";
            int2 step = _nav.Update(input.Move + (pad ? PlayerInventory.DPad(input.Buttons) : float2.zero), Time.unscaledTime);
            if (step.x != 0)
                _model.Move(step.x);
            if (!pad)
            {
                for (int card = 0; card < _model.Count && card < PlayerActionBar.SlotCount; card++)
                {
                    if ((pressed & ActionSlotButtons.For(card)) != 0)
                        _model.PointAt(card);
                }
            }
            if ((pressed & (PlayerButtons.Jump | PlayerButtons.Interact)) != 0)
                Choose();
        }

        void Open()
        {
            RaceBook book = _race.Book;
            int count = book != null ? book.Races.Count : 0;
            _model.Open(count, book != null ? Mathf.Max(0, book.IndexOf(_race.Race)) : 0);
            IsOpen = true;
            _askedAt = float.NegativeInfinity;
            // Whatever was held as it opened mustn't choose at once.
            _previousButtons = _player.OwnerRawInput.Buttons;
        }

        /// <summary>Asks the host for the race under the cursor. The window stays up until the host agrees.</summary>
        public void Choose()
        {
            RaceBook book = _race.Book;
            if (IsWaiting || book == null || _model.Cursor >= book.Races.Count || book.Races[_model.Cursor] == null)
                return;
            _race.RequestChoose(book.Races[_model.Cursor].Id);
            _askedAt = Time.time;
        }
    }
}
