namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The class window's state (Roadmap 4.6): which card the cursor is on. Plain C#, so it's tested without a scene;
    /// <see cref="PlayerClassMenu"/> moves it from the controls and <see cref="ClassPickerView"/> draws it.
    /// </summary>
    public sealed class ClassPickerModel
    {
        /// <summary>How many class cards there are.</summary>
        public int Count { get; private set; }

        /// <summary>The card the cursor is on (0 to <see cref="Count"/> - 1).</summary>
        public int Cursor { get; private set; }

        /// <summary>Opens with the cursor on <paramref name="current"/> (the hero's own class), out of <paramref name="count"/> cards.</summary>
        public void Open(int count, int current)
        {
            Count = count < 0 ? 0 : count;
            Cursor = Clamp(current);
        }

        /// <summary>Moves the cursor <paramref name="step"/> cards along, stopping at the ends.</summary>
        public void Move(int step) => Cursor = Clamp(Cursor + step);

        /// <summary>Puts the cursor on card <paramref name="card"/> (the mouse, or a number key).</summary>
        public void PointAt(int card)
        {
            if (card >= 0 && card < Count)
                Cursor = card;
        }

        int Clamp(int card) => Count == 0 ? 0 : card < 0 ? 0 : card >= Count ? Count - 1 : card;
    }
}
