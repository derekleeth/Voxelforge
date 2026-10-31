using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>What a map marker shows (Roadmap 4.8). Dungeons and ruins (5.3) will join the list.</summary>
    public enum MapMarkerKind : byte
    {
        Hero,
        Home,
    }

    /// <summary>One thing to draw on a player's minimap and World Map (Roadmap 4.8).</summary>
    public struct MapMarker
    {
        public MapMarkerKind Kind;
        /// <summary>World x, z.</summary>
        public float2 WorldXZ;
        /// <summary>Degrees clockwise from north. Only heroes have one.</summary>
        public float Heading;
        public Color Tint;
        /// <summary>The player it belongs to: the hero, or the totem's owner.</summary>
        public PlayerId Owner;
    }

    /// <summary>
    /// Something that puts markers on the maps (Roadmap 4.8): the party now, and later dungeon stamps (5.3). Every map
    /// asks every source once a frame, so a source should be cheap.
    /// </summary>
    public interface IMapMarkerSource
    {
        void CollectMarkers(List<MapMarker> markers);
    }

    /// <summary>
    /// Every hero in the session and their Home Totem, on every PC (Roadmap 4.8), so partners can always find each other
    /// and each other's homes. Heroes come from <see cref="PlayerController.AnySpawned"/>, never a global player list
    /// (CLAUDE.md Law #1). Positions and totems are what the host already syncs.
    /// </summary>
    public sealed class PartyMarkers : IMapMarkerSource, IDisposable
    {
        readonly List<(PlayerController Player, PlayerHearth Hearth)> _heroes = new List<(PlayerController, PlayerHearth)>();

        public PartyMarkers()
        {
            PlayerController.AnySpawned += Track;
            PlayerController.AnyDespawned += Forget;
        }

        /// <summary>Adds a hero, e.g. one that spawned before this was made.</summary>
        public void Track(PlayerController player)
        {
            if (player != null && _heroes.FindIndex(h => h.Player == player) < 0)
                _heroes.Add((player, player.GetComponent<PlayerHearth>()));
        }

        void Forget(PlayerController player) => _heroes.RemoveAll(h => h.Player == player);

        public void CollectMarkers(List<MapMarker> markers)
        {
            for (int i = _heroes.Count - 1; i >= 0; i--)
            {
                (PlayerController player, PlayerHearth hearth) = _heroes[i];
                if (player == null)
                {
                    _heroes.RemoveAt(i);
                    continue;
                }
                if (hearth != null && hearth.HasHome)
                {
                    int3 home = hearth.HomeBlock;
                    markers.Add(new MapMarker
                    {
                        Kind = MapMarkerKind.Home,
                        WorldXZ = new float2(home.x + 0.5f, home.z + 0.5f),
                        Tint = player.TeamColor,
                        Owner = player.PlayerId,
                    });
                }
                if (!player.HasLanded)
                    continue;
                Vector3 hero = player.VisualRoot.position;
                markers.Add(new MapMarker
                {
                    Kind = MapMarkerKind.Hero,
                    WorldXZ = new float2(hero.x, hero.z),
                    Heading = player.transform.eulerAngles.y,
                    Tint = player.TeamColor,
                    Owner = player.PlayerId,
                });
            }
        }

        public void Dispose()
        {
            PlayerController.AnySpawned -= Track;
            PlayerController.AnyDespawned -= Forget;
            _heroes.Clear();
        }
    }
}
