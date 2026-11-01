using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Threat in one player's own view (Roadmap 5.3, CLAUDE.md Law #5): a red inked <b>eye</b> beside every creature that
    /// is after this player's hero, and a thin <b>threat meter</b> under their Tab target that fills from green through
    /// yellow to red as they near pulling it (full red: it's on them). It reads each creature's shared
    /// <see cref="MobThreatView"/>, so nothing is sent for it; the eyes are pooled, so nothing is made while playing.
    /// </summary>
    sealed class ThreatMarksView
    {
        const int Capacity = 8;
        const float EyeWidth = 34f;
        const float EyeHeight = 22f;
        // The eye sits left of where a creature's health bar floats (TargetBarsView), so the two read together.
        const float EyeLeft = 82f;
        const float MeterWidth = 100f;
        const float MeterHeight = 7f;
        const float MeterBelow = 16f;
        // Over the top of the creature's body, in metres, like a Training Dummy's bar.
        const float OverTop = 0.35f;
        // Eyes and meters further than this from the player's hero aren't shown, in metres.
        const float MaxDistance = 40f;
        const float PopSeconds = 0.25f;

        static readonly Color Calm = new Color(0.35f, 0.85f, 0.3f);
        static readonly Color Close = new Color(1f, 0.82f, 0.2f);
        static readonly Color Pulled = new Color(0.9f, 0.15f, 0.12f);

        static Sprite _eyeSprite;

        sealed class Eye
        {
            public RectTransform Root;
            public ulong CreatureId;
            public float SeenAt;
        }

        readonly Eye[] _eyes = new Eye[Capacity];
        readonly RectTransform _meter;
        readonly Image _meterFill;

        public ThreatMarksView(RectTransform parent, HudSkin skin)
        {
            for (int i = 0; i < Capacity; i++)
            {
                var eye = new Eye
                {
                    Root = HudWidgets.Place(HudWidgets.Rect($"Aggro Eye {i}", parent), Vector2.zero, new Vector2(0.5f, 0.5f),
                        Vector2.zero, new Vector2(EyeWidth, EyeHeight)),
                };
                HudWidgets.Stretch(HudWidgets.Image("Eye", eye.Root, EyeSprite, Color.white).rectTransform);
                eye.Root.gameObject.SetActive(false);
                _eyes[i] = eye;
            }

            _meter = HudWidgets.Place(HudWidgets.Rect("Threat Meter", parent), Vector2.zero, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(MeterWidth, MeterHeight));
            HudWidgets.Stretch(HudWidgets.Image("Back", _meter, skin.barBack, Color.white).rectTransform, 1f);
            _meterFill = HudWidgets.Image("Fill", _meter, skin.barFill, Calm);
            _meterFill.type = Image.Type.Filled;
            _meterFill.fillMethod = Image.FillMethod.Horizontal;
            HudWidgets.Stretch(_meterFill.rectTransform, 1f);
            HudWidgets.Stretch(HudWidgets.Image("Frame", _meter, skin.barFrame, Color.white, Image.Type.Sliced).rectTransform, -2f);
            _meter.gameObject.SetActive(false);
        }

        /// <param name="area">The rect this view's parent covers, in GUI pixels.</param>
        /// <param name="scale">GUI pixels per 1080p pixel.</param>
        public void Tick(PlayerController player, PlayerTargeting targeting, CombatTargets targets, PlayerView view, Rect area,
            float scale, float now)
        {
            int used = 0;
            PlayerId me = player != null ? player.PlayerId : PlayerId.None;
            float3 hero = player != null ? (float3)player.transform.position : float3.zero;
            if (targets != null && me.IsValid)
            {
                foreach (ICombatCreature creature in targets.Creatures)
                {
                    if (used >= Capacity)
                        break;
                    if (!creature.IsTargetable || creature.ThreatView.Aggro != me.Value ||
                        !TryProject(creature, hero, view, out Vector2 gui))
                        continue;
                    PlaceEye(_eyes[used++], creature.CreatureId, gui, area, scale, now);
                }
            }
            for (int i = 0; i < Capacity; i++)
            {
                if (i >= used)
                    _eyes[i].CreatureId = 0;
                HudWidgets.SetActive(_eyes[i].Root, i < used);
            }
            TickMeter(me, hero, targeting, targets, view, area, scale);
        }

        void TickMeter(PlayerId me, float3 hero, PlayerTargeting targeting, CombatTargets targets, PlayerView view, Rect area,
            float scale)
        {
            // Only while the target is fighting someone: a mob minding its own business has no threat to show.
            if (targeting == null || !me.IsValid || !targeting.TryGetTarget(targets, out ICombatCreature creature) ||
                creature.ThreatView.Aggro == 0 || !TryProject(creature, hero, view, out Vector2 gui))
            {
                HudWidgets.SetActive(_meter, false);
                return;
            }
            HudWidgets.SetActive(_meter, true);
            float share = creature.ThreatView.ShareOf(me) / (float)MobThreatView.Pull;
            _meterFill.fillAmount = share;
            _meterFill.color = MeterColor(share);
            _meter.anchoredPosition = new Vector2((gui.x - area.x) / scale, (area.yMax - gui.y) / scale - MeterBelow);
        }

        /// <summary>Green while it's safe, yellow as the hero nears pulling it, red once it's theirs (or about to be).</summary>
        public static Color MeterColor(float share) =>
            share < 0.6f ? Calm
            : share < AggroWarnings.WarnShare ? Color.Lerp(Calm, Close, (share - 0.6f) / (AggroWarnings.WarnShare - 0.6f))
            : Color.Lerp(Close, Pulled, (share - AggroWarnings.WarnShare) / (1f - AggroWarnings.WarnShare));

        static bool TryProject(ICombatCreature creature, float3 hero, PlayerView view, out Vector2 gui)
        {
            gui = default;
            float3 top = creature.Feet + new float3(0f, creature.HalfExtents.y * 2f + OverTop, 0f);
            return math.distance(top, hero) <= MaxDistance && PlayerScreenArea.TryProject(view, top, out gui);
        }

        static void PlaceEye(Eye eye, ulong creatureId, Vector2 gui, Rect area, float scale, float now)
        {
            // A creature that just turned on this hero: the eye pops open, so it's noticed.
            if (eye.CreatureId != creatureId)
            {
                eye.CreatureId = creatureId;
                eye.SeenAt = now;
            }
            float pop = 1f + 0.6f * (1f - Mathf.Clamp01((now - eye.SeenAt) / PopSeconds));
            eye.Root.anchoredPosition = new Vector2((gui.x - area.x) / scale - EyeLeft, (area.yMax - gui.y) / scale);
            eye.Root.localScale = new Vector3(pop, pop, 1f);
        }

        /// <summary>
        /// A comic eye, drawn in code the first time it's needed: an almond of white with a thick ink edge, a red iris and
        /// a black pupil with a glint.
        /// </summary>
        static Sprite EyeSprite => _eyeSprite != null ? _eyeSprite : _eyeSprite = MakeEye();

        static Sprite MakeEye()
        {
            const int width = 64, height = 42;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                name = "Aggro Eye", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[width * height];
            var ink = new Color(0.08f, 0.05f, 0.05f);
            var white = new Color(1f, 0.97f, 0.9f);
            var iris = new Color(0.85f, 0.1f, 0.08f);
            float pixel = 2f / width;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // -1..1 across, the same scale up and down, so the shapes stay round.
                float2 p = new float2((x + 0.5f) * pixel - 1f, (y + 0.5f - height * 0.5f) * pixel);
                // An almond: where two big circles overlap.
                float almond = math.max(math.length(p - new float2(0f, -0.58f)) - 1f, math.length(p + new float2(0f, -0.58f)) - 1f);
                float alpha = math.saturate(0.5f - almond / pixel);
                Color c = ink;
                if (almond < -0.12f)
                {
                    float r = math.length(p);
                    c = r < 0.14f ? ink : r < 0.36f ? iris : r < 0.42f ? ink : white;
                    if (math.length(p - new float2(-0.08f, 0.09f)) < 0.06f)
                        c = Color.white;
                }
                c.a = alpha;
                pixels[y * width + x] = c;
            }
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "Aggro Eye";
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
