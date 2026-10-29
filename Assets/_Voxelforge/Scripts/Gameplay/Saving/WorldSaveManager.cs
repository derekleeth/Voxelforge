using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;
using Debug = UnityEngine.Debug;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Loads the world from its save at startup and saves it while playing (Roadmap 3.4, D-017). A save is only the
    /// <see cref="WorldSeed"/>, the <see cref="VoxelDeltaLog"/> of changed blocks and each player's
    /// <see cref="PlayerProfile"/>; everything else grows back from the seed.
    /// <para>
    /// Runs before <see cref="VoxelWorldManager"/> so the world starts from the saved seed and the first chunks already
    /// have their edits. <b>Host only</b> (CLAUDE.md Law #2). Encoding happens on the main thread, where the world
    /// can't change underneath it; compressing and writing the file happen on a worker.
    /// </para>
    /// <para>
    /// Saves every <see cref="autosaveMinutes"/>, on quit, and on F5 (<see cref="SessionDebugOverlay"/>). Start a build
    /// with <c>-newworld</c> to start over, or <c>-world &lt;name&gt;</c> to pick a save.
    /// <b>Not a singleton:</b> systems that need it get a reference (CLAUDE.md Law #1).
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-200)] // Before VoxelWorldManager (-100) builds the world.
    public sealed class WorldSaveManager : MonoBehaviour
    {
        public const string SaveFileName = "world.vfsave";

        [SerializeField] VoxelWorldManager world;

        [SerializeField] PlayerRegistry players;

        [SerializeField] NetworkManager networkManager;

        [Tooltip("The save's folder name. -world <name> on the command line overrides it.")]
        [SerializeField] string worldName = "World1";

        [Tooltip("Ignore the save and start a new world. The old save is replaced (and kept as .bak) at the first save. " +
                 "-newworld on the command line does the same.")]
        [SerializeField] bool startNewWorld;

        [Tooltip("Minutes between autosaves. 0 = only on quit and F5.")]
        [SerializeField, Range(0f, 30f)] float autosaveMinutes = 2f;

        [SerializeField] bool saveOnQuit = true;

        // How often live players' positions and bags are copied into their profiles, so a save on quit still has them
        // if the players are gone by then.
        const float ProfileRefreshSeconds = 1f;

        Dictionary<PlayerUuid, PlayerProfile> _profiles = new Dictionary<PlayerUuid, PlayerProfile>();
        // Storage Chest contents (Roadmap 4.5), carried through until the chest service owns them.
        Dictionary<int3, List<ItemStack>> _chests = new Dictionary<int3, List<ItemStack>>();
        string _path;
        string _blockedReason;
        Task<(long bytes, double ms)> _writeTask;
        double _encodeMs;
        bool _saveQueued;
        float _nextAutosave;
        float _nextProfileRefresh;
        DateTime _lastSaveUtc;

        /// <summary>The save's name, e.g. "World1".</summary>
        public string WorldName => worldName;

        /// <summary>The save file's full path.</summary>
        public string SavePath => _path;

        /// <summary>True if this session started from a save.</summary>
        public bool LoadedFromSave { get; private set; }

        /// <summary>How long reading the save took at startup.</summary>
        public double LoadMilliseconds { get; private set; }

        /// <summary>Size of the last save written, in bytes. 0 if none yet.</summary>
        public long LastSaveBytes { get; private set; }

        /// <summary>True while a save is being written.</summary>
        public bool IsSaving => _writeTask != null;

        /// <summary>Raised on the main thread when a save finishes or fails, with a message for players.</summary>
        public event Action<string> SaveFinished;

        /// <summary>Every save folder lives here. The editor uses its own so play-testing never touches a build's saves.</summary>
        public static string SavesRoot => Path.Combine(Application.persistentDataPath, Application.isEditor ? "Saves-Editor" : "Saves");

        public static string PathFor(string worldName) => Path.Combine(SavesRoot, worldName, SaveFileName);

        /// <summary>Host only: a copy of the saved profile for <paramref name="uuid"/>, if this world has met them.</summary>
        public bool TryGetProfile(PlayerUuid uuid, out PlayerProfile profile)
        {
            profile = _profiles.TryGetValue(uuid, out PlayerProfile saved) ? saved.Clone() : null;
            return profile != null;
        }

        /// <summary>A one-line summary for the debug overlay.</summary>
        public string Describe()
        {
            if (_blockedReason != null)
                return $"save: {worldName} · <color=#ff8040>not saving: {_blockedReason}</color>";
            string last = LastSaveBytes == 0 ? "not saved yet" :
                $"saved {(DateTime.UtcNow - _lastSaveUtc).TotalSeconds:F0} s ago ({LastSaveBytes / 1024f:F1} KB)";
            return $"save: {worldName} · {(LoadedFromSave ? "loaded" : "new world")} · {last}{(IsSaving ? " · saving..." : "")} · F5 save";
        }

        void Awake()
        {
            if (world == null)
            {
                Debug.LogError($"{nameof(WorldSaveManager)} needs the world manager.", this);
                enabled = false;
                return;
            }

            ReadCommandLine();
            _path = PathFor(worldName);
            if (startNewWorld)
            {
                Debug.Log($"[Voxelforge] Starting a new world '{worldName}'. Any old save there is replaced at the first save.", this);
                return;
            }
            Load();
        }

        void ReadCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-newworld") >= 0)
                startNewWorld = true;
            int index = Array.IndexOf(args, "-world");
            if (index >= 0 && index + 1 < args.Length)
                worldName = args[index + 1];

            foreach (char c in Path.GetInvalidFileNameChars())
                worldName = worldName.Replace(c, '_');
            if (string.IsNullOrWhiteSpace(worldName))
                worldName = "World1";
        }

        void Load()
        {
            var timer = Stopwatch.StartNew();
            WorldLoadResult result = TryRead(_path, out WorldSave save);
            if (result == WorldLoadResult.NotASave || result == WorldLoadResult.Corrupt)
            {
                Debug.LogError($"[Voxelforge] The save {_path} is damaged ({result}). Trying the backup.", this);
                if (TryRead(_path + ".bak", out save) == WorldLoadResult.Loaded)
                {
                    result = WorldLoadResult.Loaded;
                    Debug.LogWarning("[Voxelforge] Loaded the backup save. Changes since the save before last are lost.", this);
                }
                else
                {
                    string aside = $"{_path}.damaged-{DateTime.Now:yyyyMMdd-HHmmss}";
                    TryMove(_path, aside);
                    Debug.LogError($"[Voxelforge] The backup didn't load either. Moved the damaged save to {aside} and started a new world.", this);
                    return;
                }
            }

            switch (result)
            {
                case WorldLoadResult.NotFound:
                    if (_blockedReason == null)
                        Debug.Log($"[Voxelforge] No save for '{worldName}' yet: starting a new world. It will save to {_path}.", this);
                    return;
                case WorldLoadResult.NewerFormat:
                    _blockedReason = "the save is from a newer version";
                    Debug.LogError($"[Voxelforge] {_path} was made by a newer version of Voxelforge. Playing a new world " +
                                   "without saving, so it isn't overwritten.", this);
                    return;
                case WorldLoadResult.Loaded:
                    break;
                default:
                    _blockedReason = $"couldn't read the save ({result})";
                    return;
            }

            world.UseSavedWorld(save.Seed, save.Deltas);
            _profiles = save.Profiles;
            _chests = save.Chests;
            LoadedFromSave = true;
            LoadMilliseconds = timer.Elapsed.TotalMilliseconds;
            if (save.GeneratorVersion < TerrainSampler.GeneratorVersion)
                Debug.LogWarning($"[Voxelforge] This world was saved with terrain generator v{save.GeneratorVersion}; this is " +
                                 $"v{TerrainSampler.GeneratorVersion}, so unedited terrain may look different.", this);
            Debug.Log($"[Voxelforge] Loaded '{worldName}' in {LoadMilliseconds:F1} ms: seed {save.Seed}, " +
                      $"{save.Deltas.BlockCount} edited blocks in {save.Deltas.ChunkCount} chunks, {save.Profiles.Count} player profile(s), " +
                      $"saved {save.SavedUtc.ToLocalTime():yyyy-MM-dd HH:mm}.", this);
        }

        WorldLoadResult TryRead(string path, out WorldSave save)
        {
            try
            {
                return WorldSaveFile.ReadFile(path, out save);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // A file we can't open isn't damaged, so don't move it aside or overwrite it.
                save = null;
                _blockedReason = $"couldn't open the save ({e.Message})";
                Debug.LogError($"[Voxelforge] Couldn't open {path}: {e.Message}", this);
                return WorldLoadResult.NotFound;
            }
        }

        static void TryMove(string from, string to)
        {
            try
            {
                if (File.Exists(from))
                    File.Move(from, to);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Voxelforge] Couldn't move {from} aside: {e.Message}");
            }
        }

        void Start() => _nextAutosave = Time.unscaledTime + autosaveMinutes * 60f;

        void Update()
        {
            if (_writeTask != null && _writeTask.IsCompleted)
                FinishWrite();

            if (!IsHost)
                return;

            if (Time.unscaledTime >= _nextProfileRefresh)
            {
                RefreshLiveProfiles();
                _nextProfileRefresh = Time.unscaledTime + ProfileRefreshSeconds;
            }

            if (autosaveMinutes > 0f && Time.unscaledTime >= _nextAutosave)
            {
                SaveNow();
                _nextAutosave = Time.unscaledTime + autosaveMinutes * 60f;
            }
        }

        /// <summary>
        /// Host only: saves the world now. The file is written on a worker; <see cref="SaveFinished"/> says when it's
        /// done. Asking while a save is still writing queues one more. Returns false if this PC can't save.
        /// </summary>
        public bool SaveNow()
        {
            if (_blockedReason != null)
            {
                SaveFinished?.Invoke($"Not saving: {_blockedReason}");
                return false;
            }
            if (!IsHost || world == null || world.DeltaLog == null)
                return false;
            if (_writeTask != null)
            {
                _saveQueued = true;
                return true;
            }

            var timer = Stopwatch.StartNew();
            RefreshLiveProfiles();
            byte[] body = WorldSaveFile.EncodeBody(new WorldSave
            {
                Seed = world.Seed,
                GeneratorVersion = TerrainSampler.GeneratorVersion,
                SavedUtc = DateTime.UtcNow,
                Deltas = world.DeltaLog,
                Profiles = _profiles,
                Chests = _chests,
            });
            _encodeMs = timer.Elapsed.TotalMilliseconds;

            string path = _path;
            _writeTask = Task.Run(() =>
            {
                var writeTimer = Stopwatch.StartNew();
                long bytes = WorldSaveFile.WriteFile(path, body);
                return (bytes, writeTimer.Elapsed.TotalMilliseconds);
            });
            return true;
        }

        void FinishWrite()
        {
            Task<(long bytes, double ms)> task = _writeTask;
            _writeTask = null;
            string message;
            if (task.IsFaulted)
            {
                Exception error = task.Exception?.GetBaseException();
                message = $"Save failed: {error?.Message}";
                Debug.LogError($"[Voxelforge] Couldn't save '{worldName}' to {_path}: {error}", this);
            }
            else
            {
                LastSaveBytes = task.Result.bytes;
                _lastSaveUtc = DateTime.UtcNow;
                message = $"World saved ({LastSaveBytes / 1024f:F1} KB, {world.DeltaLog.BlockCount} edited blocks, {_profiles.Count} player(s))";
                Debug.Log($"[Voxelforge] Saved '{worldName}': {LastSaveBytes:N0} bytes, {world.DeltaLog.BlockCount} edited blocks, " +
                          $"{_profiles.Count} profile(s); encode {_encodeMs:F1} ms on the main thread, write {task.Result.ms:F1} ms on a worker. {_path}", this);
            }
            SaveFinished?.Invoke(message);

            if (_saveQueued)
            {
                _saveQueued = false;
                SaveNow();
            }
        }

        /// <summary>Copies where each live player stands and what's in their bag into their profile.</summary>
        void RefreshLiveProfiles()
        {
            if (players == null)
                return;
            foreach (PlayerController player in players.Players)
            {
                if (player == null || !player.Uuid.IsValid || !player.TryGetSaveState(out float3 feet, out float yaw))
                    continue;
                if (!_profiles.TryGetValue(player.Uuid, out PlayerProfile profile))
                    _profiles[player.Uuid] = profile = new PlayerProfile { Uuid = player.Uuid };
                profile.Name = $"Player {player.LocalSlot + 1}";
                profile.Feet = feet;
                profile.Yaw = yaw;
                if (player.TryGetComponent(out PlayerBlockBag bag))
                {
                    profile.Bag = bag.ServerSnapshot();
                    profile.Bags = bag.Bags;
                }
                if (player.TryGetComponent(out PlayerHearth hearth))
                {
                    profile.HasHome = hearth.HasHome;
                    profile.HomeBlock = hearth.ServerHomeBlock;
                }
            }
        }

        // A remote client (Phase 7) has no world to save. Before the host starts, or as it shuts down on quit, this PC
        // is still the one that owns the world.
        bool IsHost => networkManager == null || networkManager.IsServer || !networkManager.IsListening;

        void OnApplicationQuit()
        {
            if (saveOnQuit)
                SaveNow();
            WaitForWrites();
        }

        void OnDestroy() => WaitForWrites();

        /// <summary>Blocks until every save in progress (and a queued one) is on disk, so quitting never cuts one short.</summary>
        void WaitForWrites()
        {
            for (int i = 0; i < 2 && _writeTask != null; i++)
            {
                try
                {
                    _writeTask.Wait(TimeSpan.FromSeconds(10));
                }
                catch (AggregateException)
                {
                    // FinishWrite reports it.
                }
                if (_writeTask.IsCompleted)
                    FinishWrite();
            }
        }
    }
}
