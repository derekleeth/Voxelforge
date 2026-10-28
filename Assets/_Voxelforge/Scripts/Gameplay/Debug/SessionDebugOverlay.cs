using System.Text;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>A corner readout of the host and every player, for play-testing. Press F3 to hide it.</summary>
    public sealed class SessionDebugOverlay : MonoBehaviour
    {
        [SerializeField] GameSession session;

        [Tooltip("Optional: shows the split-screen mode.")]
        [SerializeField] SplitScreenDirector cameras;

        [Tooltip("Optional: shows how much terrain is loaded.")]
        [SerializeField] VoxelWorldManager world;

        [Tooltip("Optional: shows terrain streaming around the players.")]
        [SerializeField] ChunkLoader chunkLoader;

        [SerializeField] bool visible = true;

        readonly StringBuilder _text = new StringBuilder();
        GUIStyle _style;
        float _smoothedDeltaTime = 1f / 60f;

        void Update()
        {
            _smoothedDeltaTime = Mathf.Lerp(_smoothedDeltaTime, Time.unscaledDeltaTime, 0.05f);
            if (UnityEngine.InputSystem.Keyboard.current?.f3Key.wasPressedThisFrame == true)
                visible = !visible;
        }

        void OnGUI()
        {
            if (!visible || session == null || session.NetworkManager == null)
                return;

            _style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true };

            var network = session.NetworkManager;
            _text.Clear();
            _text.Append($"<b>{(network.IsHost ? "Host" : network.IsServer ? "Server" : network.IsClient ? "Client" : "Offline")}</b>");
            if (network.IsListening)
                _text.Append($"  client {network.LocalClientId}");
            _text.Append($"   {1f / _smoothedDeltaTime:F0} FPS");
            if (cameras != null)
            {
                SplitLayout layout = cameras.Layout;
                _text.Append($"   camera: {layout.Mode}");
                if (layout.Mode == SplitMode.Merged || layout.Mode == SplitMode.Voronoi)
                    _text.Append($" ({layout.Separation:F1} m apart)");
            }

            AppendTerrain();

            foreach (PlayerController player in session.Players.Players)
            {
                if (player == null)
                    continue;

                Vector3 p = player.transform.position;
                string state = player.IsWaitingForGround ? "waiting for ground" : player.Grounded ? "grounded" : "airborne";
                _text.Append($"\n{player.PlayerId}  slot {player.LocalSlot}  ({p.x:F1}, {p.y:F1}, {p.z:F1})  {state}");
            }

            AppendLocalSlots();

            GUILayout.BeginArea(new Rect(10, 10, 560, 300));
            GUILayout.Label(_text.ToString(), _style);
            GUILayout.EndArea();
        }

        void AppendTerrain()
        {
            if (world == null || world.World == null)
                return;

            _text.Append($"\nchunks: {world.World.ChunkCount} loaded, {world.Scheduler.RendererCount} drawn");
            if (world.Generator.QueuedCount + world.Generator.PendingCount > 0)
                _text.Append($", {world.Generator.QueuedCount + world.Generator.PendingCount} generating");
            if (chunkLoader != null && chunkLoader.Streamer != null)
            {
                _text.Append($"   columns: {chunkLoader.Streamer.ColumnCount}");
                if (chunkLoader.Streamer.UnloadingCount > 0)
                    _text.Append($" ({chunkLoader.Streamer.UnloadingCount} unloading)");
                _text.Append($"   viewers: {chunkLoader.Viewers.Count}");
            }
            if (world.Decorations != null)
                _text.Append($"\ndecorations: {world.Decorations.InstanceCount} in {world.Decorations.ChunkCount} chunks");
        }

        /// <summary>One line per joined slot on this PC, plus a join prompt for the next free one.</summary>
        void AppendLocalSlots()
        {
            LocalPlayerRoster roster = session.LocalPlayers != null ? session.LocalPlayers.Roster : null;
            if (roster == null)
                return;

            _text.Append("\n<b>This PC</b>");
            for (int slot = 0; slot < roster.MaxSlots; slot++)
            {
                LocalPlayerInput input = roster.GetInput(slot);
                switch (roster.GetState(slot))
                {
                    case LocalSlotState.Free:
                        _text.Append($"\n  slot {slot}: press Start / Enter to join");
                        return; // Only prompt for the next free slot.
                    case LocalSlotState.Joining:
                        _text.Append($"\n  slot {slot}: {input.DeviceName} joining...");
                        break;
                    case LocalSlotState.Active:
                        _text.Append($"\n  slot {slot}: {input.DeviceName} ({input.ControlScheme})");
                        break;
                    case LocalSlotState.Disconnected:
                        _text.Append($"\n  slot {slot}: <color=#ff8040>{input.DeviceName} disconnected</color> (reconnect, or press Start on another controller)");
                        break;
                }
            }
        }
    }
}
