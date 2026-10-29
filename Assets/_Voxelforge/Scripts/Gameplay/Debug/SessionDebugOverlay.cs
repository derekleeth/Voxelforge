using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A corner readout of the host and every player, for play-testing. F3 hides it, F4 records frame stats, F5 saves
    /// the world, F11 toggles full screen in a build, F12 saves a screenshot and Shift+Esc quits a build. Starting a build with <c>-smoketest &lt;seconds&gt;</c> logs stats and a
    /// screenshot after that long, then quits.
    /// </summary>
    public sealed class SessionDebugOverlay : MonoBehaviour
    {
        [SerializeField] GameSession session;

        [Tooltip("Optional: shows the split-screen mode.")]
        [SerializeField] SplitScreenDirector cameras;

        [Tooltip("Optional: shows how much terrain is loaded.")]
        [SerializeField] VoxelWorldManager world;

        [Tooltip("Optional: shows terrain streaming around the players.")]
        [SerializeField] ChunkLoader chunkLoader;

        [Tooltip("Optional: shows how many block drops are lying around.")]
        [SerializeField] BlockDropSpawner drops;

        [Tooltip("Optional: F5 saves the world, and the overlay shows the save.")]
        [SerializeField] WorldSaveManager saves;

        [Tooltip("Optional: shows how many sounds are playing (Roadmap 3.9).")]
        [SerializeField] SoundRouter sounds;

        [SerializeField] bool visible = true;

        [Tooltip("How many seconds of frames the average and 1% low FPS cover.")]
        [SerializeField] float statsWindowSeconds = 10f;

        readonly StringBuilder _text = new StringBuilder();
        readonly FrameTimeStats _frameStats = new FrameTimeStats();
        GUIStyle _style;
        GUIStyle _toastStyle;
        string _toast;
        int _toastFromFrame;
        float _toastUntil;
        float _smoothedDeltaTime = 1f / 60f;
        FrameStatsSnapshot _shownStats;
        float _nextStatsRefresh;
        float _smokeTestSeconds = -1f;
        int _quitInFrames = -1;

        void Awake()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-smoketest");
            if (index >= 0)
                _smokeTestSeconds = index + 1 < args.Length && float.TryParse(args[index + 1], out float seconds) ? seconds : 20f;
        }

        void OnEnable()
        {
            if (saves != null)
                saves.SaveFinished += HandleSaveFinished;
            if (cameras != null)
                cameras.StyleChanged += HandleStyleChanged;
        }

        void OnDisable()
        {
            if (saves != null)
                saves.SaveFinished -= HandleSaveFinished;
            if (cameras != null)
                cameras.StyleChanged -= HandleStyleChanged;
        }

        void HandleSaveFinished(string message) => ShowToast(message, delayFrames: 0);

        void HandleStyleChanged(SplitStyle style) => ShowToast(style == SplitStyle.Classic
            ? "Split-screen: Classic (each player has their own half)"
            : "Split-screen: Dynamic (one view when together, split when apart)", delayFrames: 0);

        void Update()
        {
            _smoothedDeltaTime = Mathf.Lerp(_smoothedDeltaTime, Time.unscaledDeltaTime, 0.05f);
            _frameStats.Add(Time.unscaledDeltaTime);
            if (Time.unscaledTime >= _nextStatsRefresh)
            {
                // Sorting thousands of frames every frame would cost more than it measures.
                _shownStats = _frameStats.Snapshot(statsWindowSeconds);
                _nextStatsRefresh = Time.unscaledTime + 0.5f;
            }

            if (_quitInFrames >= 0 && _quitInFrames-- == 0)
                Application.Quit(); // A few frames after the smoke test, so its screenshot gets written first.
            else if (_quitInFrames < 0 && _smokeTestSeconds >= 0f && Time.realtimeSinceStartup >= _smokeTestSeconds)
            {
                LogFrameStats("smoke test");
                SaveScreenshot("smoketest");
                _quitInFrames = 5;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.f3Key.wasPressedThisFrame)
                visible = !visible;
            if (keyboard.f4Key.wasPressedThisFrame)
                LogFrameStats("F4");
            if (keyboard.f5Key.wasPressedThisFrame && saves != null)
                saves.SaveNow();
            if (keyboard.f11Key.wasPressedThisFrame && !Application.isEditor)
                ToggleFullScreen();
            if (keyboard.f12Key.wasPressedThisFrame)
                SaveScreenshot("screenshot");
            if (keyboard.escapeKey.wasPressedThisFrame && keyboard.shiftKey.isPressed && !Application.isEditor)
                Application.Quit();
        }

        /// <summary>
        /// Switches a build between a borderless full-screen window at the monitor's own resolution and a window. Unity
        /// remembers the choice for the next start. The 6.3 settings screen takes this over.
        /// </summary>
        void ToggleFullScreen()
        {
            if (Screen.fullScreenMode == FullScreenMode.Windowed)
            {
                DisplayInfo display = Screen.mainWindowDisplayInfo;
                Screen.SetResolution(display.width, display.height, FullScreenMode.FullScreenWindow);
                ShowToast($"Full screen ({display.width}x{display.height}). F11 for a window.", delayFrames: 0);
            }
            else
            {
                DisplayInfo display = Screen.mainWindowDisplayInfo;
                int width = Mathf.Min(1600, display.width * 3 / 4);
                int height = Mathf.Min(900, display.height * 3 / 4);
                Screen.SetResolution(width, height, FullScreenMode.Windowed);
                ShowToast($"Windowed ({width}x{height}). F11 for full screen.", delayFrames: 0);
            }
        }

        /// <summary>Writes the stats to the log and to frame-stats.txt, and says so on screen.</summary>
        void LogFrameStats(string reason)
        {
            FrameStatsSnapshot stats = _frameStats.Snapshot(statsWindowSeconds);
            string camera = cameras != null ? $"{cameras.Layout.Mode} ({cameras.Style} style)" : "unknown";
            int players = session != null && session.Players != null ? session.Players.Players.Count : 0;
            string save = saves != null && world != null
                ? $"; save {(saves.LastSaveBytes > 0 ? $"{saves.LastSaveBytes:N0} bytes" : "not saved yet")}, " +
                  $"{world.DeltaLog.BlockCount} edited blocks, loaded in {saves.LoadMilliseconds:F1} ms"
                : "";
            string line = $"{reason}: {stats}; camera {camera}, {players} player(s), {Screen.width}x{Screen.height}, " +
                          $"{(Application.isEditor ? "editor" : "build")}{save}";
            Debug.Log($"[FrameStats] {line}");

            string path = Path.Combine(OutputFolder(), "frame-stats.txt");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
            ShowToast($"FPS recorded ({camera}, {players} player(s)): {stats.AverageFps:F0} avg, {stats.OnePercentLowFps:F0} 1% low\n" +
                      $"saved to {path}", delayFrames: 0);
        }

        void SaveScreenshot(string prefix)
        {
            string folder = Path.Combine(OutputFolder(), "Screenshots");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[FrameStats] Screenshot saved to {path}");
            // The capture happens at the end of this frame: wait a frame so the message isn't in the picture.
            ShowToast($"Screenshot saved to {path}", delayFrames: 1);
        }

        /// <summary>Builds write next to the .exe (Builds/Windows); the editor writes to Builds/Editor.</summary>
        static string OutputFolder()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string folder = Application.isEditor ? Path.Combine(root, "Builds", "Editor") : root;
            Directory.CreateDirectory(folder);
            return folder;
        }

        void ShowToast(string message, int delayFrames)
        {
            _toast = message;
            _toastFromFrame = Time.frameCount + delayFrames;
            _toastUntil = Time.unscaledTime + 4f;
        }

        /// <summary>A confirmation along the bottom of the screen, clear of the overlay and shown even while it is hidden.</summary>
        void DrawToast()
        {
            if (_toast == null || Time.frameCount < _toastFromFrame || Time.unscaledTime > _toastUntil)
                return;

            _toastStyle ??= new GUIStyle(GUI.skin.box)
                { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold, wordWrap = true };
            float width = Mathf.Min(900f, Screen.width - 40f);
            GUI.Box(new Rect((Screen.width - width) * 0.5f, Screen.height - 100f, width, 64f), _toast, _toastStyle);
        }

        void OnGUI()
        {
            DrawToast();
            if (!visible || session == null || session.NetworkManager == null)
                return;

            _style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true };

            var network = session.NetworkManager;
            _text.Clear();
            _text.Append($"<b>{(network.IsHost ? "Host" : network.IsServer ? "Server" : network.IsClient ? "Client" : "Offline")}</b>");
            if (network.IsListening)
                _text.Append($"  client {network.LocalClientId}");
            _text.Append($"   {1f / _smoothedDeltaTime:F0} FPS");
            if (_shownStats.FrameCount > 0)
                _text.Append($" (last {_shownStats.Seconds:F0} s: {_shownStats.AverageFps:F0} avg, {_shownStats.OnePercentLowFps:F0} 1% low)");
            if (cameras != null)
            {
                SplitLayout layout = cameras.Layout;
                _text.Append($"   camera: {layout.Mode}");
                if (layout.Mode == SplitMode.Merged || layout.Mode == SplitMode.Voronoi || layout.Mode == SplitMode.Classic)
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
            if (world.DeltaLog != null)
                _text.Append($"\nedits: {world.DeltaLog.BlockCount} blocks in {world.DeltaLog.ChunkCount} chunks");
            if (world.Damage != null)
                _text.Append($"   being mined: {world.Damage.Count}");
            if (drops != null)
                _text.Append($"   drops: {drops.DropCount}");
            if (sounds != null)
                _text.Append($"\nsounds: {sounds.ActiveVoices} + {sounds.ActiveLoops} loops / {sounds.VoiceCount} voices");
            if (saves != null)
                _text.Append($"\n{saves.Describe()}");
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
