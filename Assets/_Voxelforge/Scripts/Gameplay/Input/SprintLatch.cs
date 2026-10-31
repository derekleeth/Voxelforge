namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Turns the Sprint button into "sprinting this frame" for one player (Roadmap 4.8). Gamepads <b>toggle</b>: click
    /// the left stick once and keep running. Keyboards <b>hold</b> Shift. Either way, sprinting stops as soon as the hero
    /// stops moving, so a toggled sprint never surprises anyone when they next walk off. Stays on the owner's PC; the
    /// host only hears the resulting <see cref="PlayerButtons.Sprint"/> bit.
    /// </summary>
    public struct SprintLatch
    {
        /// <summary>Stick or keys pushed less than this count as standing still.</summary>
        public const float MoveDeadZone = 0.2f;

        bool _toggledOn;
        bool _wasPressed;

        /// <summary>
        /// One frame of input. <paramref name="toggle"/> picks the gamepad style; <paramref name="moveLength"/> is the
        /// length of the move input (0-1). Returns true while the hero should sprint.
        /// </summary>
        public bool Update(bool pressed, bool toggle, float moveLength)
        {
            bool clicked = pressed && !_wasPressed;
            _wasPressed = pressed;
            bool moving = moveLength >= MoveDeadZone;

            if (!toggle)
            {
                _toggledOn = false;
                return pressed && moving;
            }

            if (!moving)
                _toggledOn = false;
            else if (clicked)
                _toggledOn = !_toggledOn;
            return _toggledOn;
        }

        /// <summary>Forget any toggled sprint, e.g. when a window takes the controls.</summary>
        public void Reset() => _toggledOn = false;
    }
}
