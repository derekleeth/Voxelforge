using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The marker over this player's target (Roadmap 5.2), in their own view only (CLAUDE.md Law #5): a bobbing inked
    /// arrow in their team colour pointing down at it, with the creature's name above. Partners each see only their own
    /// target. It reads <see cref="PlayerTargeting"/> and where the creature stands now, so nothing is sent for it.
    /// </summary>
    sealed class TargetMarkerView
    {
        const float ArrowSize = 34f;
        // How far over the top of the creature's body the arrow's tip floats, in metres.
        const float OverTop = 0.25f;
        const float PopSeconds = 0.18f;

        readonly RectTransform _root;
        readonly Image _arrow;
        readonly TextMeshProUGUI _name;
        ulong _shownId;
        float _poppedAt;

        public TargetMarkerView(RectTransform parent, HudSkin skin)
        {
            _root = HudWidgets.Place(HudWidgets.Rect("Target Marker", parent), Vector2.zero, new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(ArrowSize, ArrowSize));
            _arrow = HudWidgets.Image("Arrow", _root, MapSprites.EdgeArrow, Color.white);
            HudWidgets.Stretch(_arrow.rectTransform);
            _arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);

            _name = HudWidgets.SmallLabel("Name", _root, skin, 22f, Color.white, TextAlignmentOptions.Bottom);
            _name.outlineWidth = 0.22f;
            _name.outlineColor = skin.ink;
            HudWidgets.Place(_name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 4f),
                new Vector2(360f, 30f));
            HudWidgets.SetActive(_root, false);
        }

        /// <param name="area">The rect this view's parent covers, in GUI pixels.</param>
        /// <param name="scale">GUI pixels per 1080p pixel.</param>
        public void Tick(PlayerTargeting targeting, CombatTargets targets, PlayerView view, Rect area, float scale, Color team,
            float now)
        {
            if (targeting == null || !targeting.TryGetTarget(targets, out ICombatCreature creature))
            {
                _shownId = 0;
                HudWidgets.SetActive(_root, false);
                return;
            }
            float3 tip = creature.Feet + new float3(0f, creature.HalfExtents.y * 2f + OverTop, 0f);
            if (!PlayerScreenArea.TryProject(view, tip, out Vector2 gui))
            {
                HudWidgets.SetActive(_root, false);
                return;
            }
            HudWidgets.SetActive(_root, true);

            if (_shownId != creature.CreatureId)
            {
                // A new target: say its name and pop the arrow, so cycling is easy to follow.
                _shownId = creature.CreatureId;
                _poppedAt = now;
                _name.text = creature is Mob mob && mob.Definition != null ? mob.Definition.DisplayName : "";
                _arrow.color = team;
                _name.color = Color.Lerp(team, Color.white, 0.55f);
            }
            float pop = 1f + 0.5f * (1f - Mathf.Clamp01((now - _poppedAt) / PopSeconds));
            float bob = Mathf.Sin(now * 5f) * 5f;
            _root.anchoredPosition = new Vector2((gui.x - area.x) / scale, (area.yMax - gui.y) / scale + 6f + bob);
            _root.localScale = new Vector3(pop, pop, 1f);
        }
    }
}
