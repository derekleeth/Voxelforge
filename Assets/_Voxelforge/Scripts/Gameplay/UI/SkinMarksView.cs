using TMPro;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Skinning hints in one player's own view (Roadmap 5.7, CLAUDE.md Law #5): "Skin" (or "Salvage") over every
    /// defeated mob's body this player's hero fought and may still skin, or "Skinning 40" in red when their skill is too
    /// low. It reads each body's synced skinners and the hero's synced skills, so nothing is sent for it; the labels are
    /// pooled, so nothing is made while playing.
    /// </summary>
    sealed class SkinMarksView
    {
        const int Capacity = 6;
        // Over the top of the body, in metres; bodies further than MaxDistance from the hero get no hint.
        const float OverTop = 0.3f;
        const float MaxDistance = 24f;
        const float FontSize = 26f;

        readonly TextMeshProUGUI[] _labels = new TextMeshProUGUI[Capacity];
        readonly Color _ready;
        readonly Color _tooHard;

        public SkinMarksView(RectTransform parent, HudSkin skin)
        {
            _ready = skin.pickup;
            _tooHard = skin.refusal;
            for (int i = 0; i < Capacity; i++)
            {
                TextMeshProUGUI label = HudWidgets.Label($"Skin Hint {i}", parent, skin.titleFont, FontSize, _ready,
                    TextAlignmentOptions.Center);
                HudWidgets.Place(label.rectTransform, Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(240f, 34f));
                label.gameObject.SetActive(false);
                _labels[i] = label;
            }
        }

        /// <param name="area">The rect this view's parent covers, in GUI pixels.</param>
        /// <param name="scale">GUI pixels per 1080p pixel.</param>
        public void Tick(PlayerController player, PlayerProfessions professions, CombatTargets targets, PlayerView view, Rect area,
            float scale)
        {
            int used = 0;
            PlayerId me = player != null ? player.PlayerId : PlayerId.None;
            if (targets != null && me.IsValid && professions != null)
            {
                float3 hero = player.transform.position;
                foreach (ICombatCreature creature in targets.Creatures)
                {
                    if (used >= Capacity)
                        break;
                    if (!(creature is Mob mob) || mob.Definition == null || !mob.CanBeSkinnedBy(me) ||
                        !professions.TryGetNode(GatherNodeKind.Mob, (ushort)mob.Definition.Id, out GatherNode node))
                        continue;
                    float3 top = mob.Feet + new float3(0f, mob.HalfExtents.y * 2f + OverTop, 0f);
                    if (math.distance(top, hero) > MaxDistance || !PlayerScreenArea.TryProject(view, top, out Vector2 gui))
                        continue;
                    TextMeshProUGUI label = _labels[used++];
                    bool able = professions.CanGather(node);
                    string words = able ? (mob.Definition.Salvaged ? "Salvage" : "Skin") : $"{node.Profession} {node.RequiredSkill}";
                    if (label.text != words)
                        label.text = words;
                    label.color = able ? _ready : _tooHard;
                    label.rectTransform.anchoredPosition = new Vector2((gui.x - area.x) / scale, (area.yMax - gui.y) / scale);
                }
            }
            for (int i = 0; i < Capacity; i++)
                HudWidgets.SetActive(_labels[i].rectTransform, i < used);
        }
    }
}
