using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Health bars over the targets near one player (Roadmap 4.6), in their own view: a small inked bar floating over each
    /// Training Dummy being fought, projected through <b>that player's</b> camera like their combat text (CLAUDE.md
    /// Law #5). It reads a shared <see cref="TargetHealthBook"/>; the bars are pooled, so nothing is made while playing.
    /// </summary>
    sealed class TargetBarsView
    {
        const int Capacity = 8;
        const float Width = 120f;
        const float Height = 16f;
        // Bars further than this from the player's hero aren't shown, in metres.
        const float MaxDistance = 40f;
        // How far over the target's middle the bar floats, in metres (a dummy's body is CombatTarget.BodyHeight tall).
        const float OverMiddle = CombatTarget.BodyHeight * 0.5f + 0.35f;
        const float ShakeSeconds = 0.25f;
        const float FadeSeconds = 1.5f;

        sealed class Bar
        {
            public RectTransform Root;
            public Image Fill;
            public Image Lag;
            public CanvasGroup Group;
            public float Shown = 1f;
        }

        readonly Bar[] _bars = new Bar[Capacity];

        public TargetBarsView(RectTransform parent, HudSkin skin)
        {
            for (int i = 0; i < Capacity; i++)
            {
                var bar = new Bar();
                bar.Root = HudWidgets.Place(HudWidgets.Rect($"Target Bar {i}", parent), Vector2.zero, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(Width, Height));
                bar.Group = bar.Root.gameObject.AddComponent<CanvasGroup>();
                HudWidgets.Stretch(HudWidgets.Image("Back", bar.Root, skin.barBack, Color.white).rectTransform, 2f);
                // A pale strip that drains after the red, so a big hit reads as a chunk falling off.
                bar.Lag = Fill("Lag", bar.Root, skin, new Color(1f, 0.92f, 0.7f));
                bar.Fill = Fill("Fill", bar.Root, skin, skin.health);
                HudWidgets.Stretch(HudWidgets.Image("Frame", bar.Root, skin.barFrame, Color.white, Image.Type.Sliced).rectTransform, -3f);
                bar.Root.gameObject.SetActive(false);
                _bars[i] = bar;
            }
        }

        static Image Fill(string name, RectTransform parent, HudSkin skin, Color colour)
        {
            Image fill = HudWidgets.Image(name, parent, skin.barFill, colour);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            HudWidgets.Stretch(fill.rectTransform, 2f);
            return fill;
        }

        /// <param name="area">The rect this view's parent covers, in GUI pixels.</param>
        /// <param name="scale">GUI pixels per 1080p pixel.</param>
        /// <param name="hero">Where this player's hero stands; only nearby targets get a bar.</param>
        public void Tick(TargetHealthBook book, PlayerView view, Rect area, float scale, float3 hero, float now)
        {
            int used = 0;
            if (book != null)
            {
                foreach (TargetHealthBook.Entry entry in book.Entries)
                {
                    if (used >= Capacity)
                        break;
                    float3 top = entry.Middle + new float3(0f, OverMiddle, 0f);
                    float distance = math.distance(top, hero);
                    if (distance > MaxDistance || !PlayerScreenArea.TryProject(view, top, out Vector2 gui))
                        continue;
                    Place(_bars[used++], entry, gui, area, scale, distance, now);
                }
            }
            for (int i = 0; i < Capacity; i++)
                HudWidgets.SetActive(_bars[i].Root, i < used);
        }

        static void Place(Bar bar, in TargetHealthBook.Entry entry, Vector2 gui, Rect area, float scale, float distance, float now)
        {
            float share = entry.Share;
            // The pale strip catches up with the red, and snaps back up when the dummy springs back to full.
            bar.Shown = share > bar.Shown ? share : Mathf.MoveTowards(bar.Shown, share, Time.deltaTime * 0.6f);
            if (!Mathf.Approximately(bar.Fill.fillAmount, share))
                bar.Fill.fillAmount = share;
            if (!Mathf.Approximately(bar.Lag.fillAmount, bar.Shown))
                bar.Lag.fillAmount = bar.Shown;

            // A little shake on each hit; smaller with distance, and it fades out before the host forgets the target.
            float sinceHit = now - entry.LastHit;
            float shake = sinceHit < ShakeSeconds ? Mathf.Sin(sinceHit * 90f) * 4f * (1f - sinceHit / ShakeSeconds) : 0f;
            float size = Mathf.Clamp(10f / Mathf.Max(distance, 0.1f), 0.6f, 1.1f);
            bar.Root.anchoredPosition = new Vector2((gui.x - area.x) / scale + shake, (area.yMax - gui.y) / scale);
            bar.Root.localScale = new Vector3(size, size, 1f);
            float left = TargetHealthBook.ShowSeconds - sinceHit;
            bar.Group.alpha = Mathf.Clamp01(left / FadeSeconds);
        }
    }
}
