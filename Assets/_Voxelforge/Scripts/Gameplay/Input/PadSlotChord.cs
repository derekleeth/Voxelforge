namespace Voxelforge.Gameplay
{
    /// <summary>
    /// How a gamepad reaches all nine action bar slots (Roadmap 4.4): the d-pad is slots 1-4 (up, right, down, left),
    /// holding B turns it into slots 5-8, and tapping B on its own is slot 9. Plain C#, so it's tested without a pad.
    /// </summary>
    public struct PadSlotChord
    {
        /// <summary>
        /// How long a B tap stays "held" as slot 9. Input frames go out every frame but the host reads them on its fixed
        /// step, so a one-frame press could be missed.
        /// </summary>
        public const float TapHoldSeconds = 0.1f;

        bool _shiftWasHeld;
        bool _shiftUsed;
        float _tapUntil;

        /// <summary>
        /// The slot held this frame (1-9), or 0 for none.
        /// </summary>
        /// <param name="shiftHeld">B is down.</param>
        /// <param name="dpadSlot">The d-pad direction held as slot 1-4, or 0.</param>
        /// <param name="now">Seconds, for the tap.</param>
        public int Update(bool shiftHeld, int dpadSlot, float now)
        {
            int slot = 0;
            if (shiftHeld)
            {
                if (!_shiftWasHeld)
                    _shiftUsed = false;
                if (dpadSlot > 0)
                {
                    _shiftUsed = true;
                    slot = dpadSlot + 4;
                }
            }
            else
            {
                // Let go of B without using the d-pad: that was a tap.
                if (_shiftWasHeld && !_shiftUsed)
                    _tapUntil = now + TapHoldSeconds;
                slot = dpadSlot;
            }
            _shiftWasHeld = shiftHeld;
            if (slot == 0 && now < _tapUntil)
                slot = 9;
            return slot;
        }
    }
}
