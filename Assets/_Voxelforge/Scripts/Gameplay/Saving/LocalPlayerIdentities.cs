using System;
using System.IO;
using UnityEngine;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The <see cref="PlayerUuid"/> of each local slot on <b>this PC</b> (Roadmap 3.4, D-017), kept in a small JSON file
    /// so Player 1 on this couch is the same player in every session and every world. The join request carries it to
    /// the host, which finds that player's profile in the save. A remote PC (Phase 7) keeps its own file.
    /// <para>Plain C# (the scene uses it through <see cref="PlayerJoinService"/>) so tests can point it at a temp file.</para>
    /// </summary>
    public sealed class LocalPlayerIdentities
    {
        [Serializable]
        sealed class FileData
        {
            public string[] slots = Array.Empty<string>();
        }

        public const string FileName = "local-players.json";

        readonly string _path;
        PlayerUuid[] _slots = Array.Empty<PlayerUuid>();

        public LocalPlayerIdentities(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            Load();
        }

        /// <summary>The file beside the saves in Unity's per-user data folder.</summary>
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>The identity of <paramref name="slot"/>, made (and written to the file) the first time it's asked for.</summary>
        public PlayerUuid Get(int slot)
        {
            if (slot < 0)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (slot < _slots.Length && _slots[slot].IsValid)
                return _slots[slot];

            if (slot >= _slots.Length)
                Array.Resize(ref _slots, slot + 1);
            _slots[slot] = PlayerUuid.NewUuid();
            Save();
            return _slots[slot];
        }

        void Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return;
                FileData data = JsonUtility.FromJson<FileData>(File.ReadAllText(_path));
                if (data?.slots == null)
                    return;
                _slots = new PlayerUuid[data.slots.Length];
                for (int i = 0; i < data.slots.Length; i++)
                    PlayerUuid.TryParse(data.slots[i], out _slots[i]); // A bad entry becomes None and gets a new identity.
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Voxelforge] Couldn't read local player identities from {_path}: {e.Message}");
            }
        }

        void Save()
        {
            var data = new FileData { slots = new string[_slots.Length] };
            for (int i = 0; i < _slots.Length; i++)
                data.slots[i] = _slots[i].IsValid ? _slots[i].ToString() : "";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, JsonUtility.ToJson(data, prettyPrint: true));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Voxelforge] Couldn't save local player identities to {_path}: {e.Message}");
            }
        }
    }
}
