using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Health bars over the targets near one player (Roadmap 4.6), in their own view: a small inked bar floating over each
    /// Training Dummy or creature (5.2) being fought, projected through <b>that player's</b> camera like their combat text (CLAUDE.md
    /// Law #5). It reads a shared <see cref="TargetHealthBook"/>; the bars are pooled, so nothing is made while playing.
    /// A creature's bar has its name over it (5.2 step G).
    /// </summary>
    sealed class TargetBarsView
    {
        const int Capacity = 8;
        const float Width = 120f;
        const float Height = 16f;
        // Bars further than this from the player's hero aren't shown, in metres.
        const float MaxDistance = 40f;
        const float ShakeSeconds = 0.25f;
        const float FadeSeconds = 1.5f;

        sealed class Bar
        {
            public RectTransform Root;
            public Image Fill;
            public Image Lag;
            public CanvasGroup Group;
            public TextMeshProUGUI Name;
            public TargetKey Key;
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
                bar.Name = HudWidgets.SmallLabel("Name", bar.Root, skin, 18f, Color.white, TextAlignmentOptions.Bottom);
                bar.Name.outlineWidth = 0.22f;
                bar.Name.outlineColor = skin.ink;
                HudWidgets.Place(bar.Name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 3f),
                    new Vector2(260f, 24f));
                bar.Name.text = "";
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
        /// <param name="creatures">Every creature, for the names over their bars; may be null.</param>
        public void Tick(TargetHealthBook book, CombatTargets creatures, PlayerView view, Rect area, float scale, float3 hero,
            float now)
        {
            int used = 0;
            if (book != null)
            {
                foreach (TargetHealthBook.Entry entry in book.Entries)
                {
                    if (used >= Capacity)
                        break;
                    float3 top = entry.Top;
                    float distance = math.distance(top, hero);
                    if (distance > MaxDistance || !PlayerScreenArea.TryProject(view, top, out Vector2 gui))
                        continue;
                    Bar bar = _bars[used++];
                    Name(bar, entry.Key, creatures);
                    Place(bar, entry, gui, area, scale, distance, now);
                }
            }
            for (int i = 0; i < Capacity; i++)
                HudWidgets.SetActive(_bars[i].Root, i < used);
        }

        /// <summary>A creature's name over its bar; none for a Training Dummy. Only looked up when the bar changes target.</summary>
        static void Name(Bar bar, TargetKey key, CombatTargets creatures)
        {
            if (bar.Key == key)
                return;
            bar.Key = key;
            string name = "";
            if (key.IsCreature && creatures != null && creatures.TryGetCreature(key.CreatureId, out ICombatCreature creature) &&
                creature is Mob mob && mob.Definition != null)
                name = mob.Definition.DisplayName;
            bar.Name.text = name;
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
