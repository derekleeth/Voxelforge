using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Loot bags in the world (Roadmap 4.5, D-028). <b>The host</b> spawns them (<see cref="ServerSpawn"/>: a dropped stack,
    /// a partner's overflow, a broken chest), pulls them toward the nearest hero who can take them and puts their stacks
    /// in that hero's bag, as <see cref="BlockDropSpawner"/> does for blocks. On <b>every PC</b> it also draws the little
    /// sack flying from one hero to another when someone uses Send to Partner (<see cref="PlayerBlockBag.Tossed"/>):
    /// looks only, as the host has already moved the items.
    /// </summary>
    public sealed class LootBagSpawner : MonoBehaviour
    {
        [SerializeField] NetworkManager networkManager;

        [SerializeField] VoxelWorldManager world;

        [Tooltip("Who can pick loot up.")]
        [SerializeField] PlayerRegistry players;

        [SerializeField] LootBag lootBagPrefab;

        [Tooltip("For the pop when loot goes into a bag and the toss landing (Roadmap 3.9).")]
        [SerializeField] SoundRouter router;

        [SerializeField] SoundBook sounds;

        [Tooltip("Loot closer than this to a hero's middle flies toward them, in metres.")]
        [SerializeField, Range(0.5f, 5f)] float pullRadius = 2f;

        [Tooltip("Loot closer than this to a hero's middle is picked up, in metres.")]
        [SerializeField, Range(0.1f, 2f)] float pickupRadius = 0.7f;

        [SerializeField, Min(1f)] float pullSpeed = 8f;

        [Tooltip("Whoever dropped a sack can take it back only after walking this much further away than the pull " +
                 "radius, so it doesn't fly straight back into their bag, in metres.")]
        [SerializeField, Range(0f, 5f)] float dropperWalkAway = 1f;

        [Tooltip("Loot nobody picks up disappears after this many seconds.")]
        [SerializeField, Min(30f)] float lifetimeSeconds = 600f;

        [Header("Send to Partner toss (looks only)")]
        [SerializeField, Range(0.2f, 2f)] float tossSeconds = 0.6f;

        [Tooltip("How high the toss arcs above a straight line, in metres (more for longer throws).")]
        [SerializeField, Range(0f, 5f)] float tossHeight = 1.4f;

        [SerializeField, Range(0.1f, 1.5f)] float tossScale = 0.7f;

        // How high above the feet a hero's middle is, where loot flies to.
        const float HeroMiddle = 0.8f;
        const int TossPool = 6;

        readonly List<LootBag> _bags = new List<LootBag>();
        readonly List<ItemStack> _given = new List<ItemStack>();
        readonly List<ItemStack> _chunk = new List<ItemStack>();

        struct Toss
        {
            public Transform Model;
            public Transform From;
            public Transform To;
            public float Start;
        }

        readonly List<Toss> _tosses = new List<Toss>();
        readonly Stack<Transform> _freeModels = new Stack<Transform>();

        bool IsServer => networkManager != null && networkManager.IsServer;

        /// <summary>Host only: loot bags lying in the world right now.</summary>
        public int Count => _bags.Count;

        void OnEnable()
        {
            LootBag.Spawned += OnSpawned;
            PlayerBlockBag.Tossed += OnTossed;
        }

        void OnDisable()
        {
            LootBag.Spawned -= OnSpawned;
            PlayerBlockBag.Tossed -= OnTossed;
        }

        void OnSpawned(LootBag bag)
        {
            if (IsServer && !_bags.Contains(bag))
                _bags.Add(bag);
        }

        /// <summary>
        /// Host only: drops <paramref name="stacks"/> at <paramref name="position"/> in as many sacks as they need,
        /// starting at <paramref name="velocity"/>. <paramref name="notFor"/> (whoever dropped them) can't pick them up
        /// until they've walked away from them once.
        /// </summary>
        public void ServerSpawn(float3 position, IReadOnlyList<ItemStack> stacks, float3 velocity, PlayerId notFor = default)
        {
            if (!IsServer || lootBagPrefab == null || world == null || world.World == null)
                return;
            _chunk.Clear();
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].IsEmpty)
                    continue;
                _chunk.Add(stacks[i]);
                if (_chunk.Count == LootBag.MaxStacks)
                    SpawnOne(position, velocity, notFor);
            }
            if (_chunk.Count > 0)
                SpawnOne(position, velocity, notFor);
        }

        void SpawnOne(float3 position, float3 velocity, PlayerId notFor)
        {
            // Several sacks at once (a big chest) spread out a little.
            float angle = UnityEngine.Random.value * math.PI * 2f;
            float3 spread = _bags.Count > 0 ? new float3(math.sin(angle), 0f, math.cos(angle)) * 0.8f : float3.zero;
            LootBag bag = Instantiate(lootBagPrefab, position, Quaternion.identity);
            bag.ServerInitialize(world.World, _chunk, velocity + spread, notFor);
            bag.NetworkObject.Spawn(destroyWithScene: true);
            _chunk.Clear();
        }

        void FixedUpdate()
        {
            if (!IsServer)
                return;
            for (int i = _bags.Count - 1; i >= 0; i--)
            {
                LootBag bag = _bags[i];
                if (bag == null || !bag.IsSpawned)
                {
                    _bags.RemoveAt(i);
                    continue;
                }
                if (Time.time - bag.SpawnTime > lifetimeSeconds || bag.IsEmpty)
                {
                    _bags.RemoveAt(i);
                    bag.NetworkObject.Despawn(destroy: true);
                    continue;
                }
                TryPickUp(bag);
                if (bag.IsEmpty)
                {
                    _bags.RemoveAt(i);
                    bag.NetworkObject.Despawn(destroy: true);
                }
            }
        }

        /// <summary>Pulls the sack toward the nearest hero who can take something from it, and empties it into their bag.</summary>
        void TryPickUp(LootBag loot)
        {
            if (players == null)
                return;
            float3 position = loot.transform.position;
            PlayerController nearest = null;
            PlayerBlockBag nearestBag = null;
            float nearestDistance = pullRadius;
            foreach (PlayerController player in players.Players)
            {
                if (player == null || !player.HasLanded || !player.TryGetComponent(out PlayerBlockBag bag))
                    continue;
                float distance = math.distance(position, Middle(player));
                if (player.PlayerId == loot.ServerDropper && distance > pullRadius + dropperWalkAway)
                    loot.ServerForgetDropper();
                if (!loot.ServerCanTake(player.PlayerId))
                    continue;
                if (distance >= nearestDistance)
                    continue;
                if (!HasRoomForAny(bag, loot))
                {
                    if (distance <= pickupRadius)
                        bag.ServerTellBagFull();
                    continue;
                }
                nearestDistance = distance;
                nearest = player;
                nearestBag = bag;
            }
            if (nearest == null)
                return;
            if (nearestDistance > pickupRadius)
            {
                loot.ServerPull(Middle(nearest), pullSpeed);
                return;
            }

            loot.ServerGiveTo(nearestBag, _given);
            foreach (ItemStack stack in _given)
                nearestBag.ServerTellPickedUp(stack.ItemId, stack.Count);
            if (_given.Count > 0)
                PlayPop(Middle(nearest), 1f);
        }

        static bool HasRoomForAny(PlayerBlockBag bag, LootBag loot)
        {
            for (int i = 0; i < loot.StackCount; i++)
            {
                if (bag.ServerRoomFor(loot.GetStack(i).ItemId) > 0)
                    return true;
            }
            return false;
        }

        static float3 Middle(PlayerController player) => (float3)player.transform.position + new float3(0f, HeroMiddle, 0f);

        // ---- Every PC: the Send to Partner toss ----

        void OnTossed(PlayerBlockBag from, PlayerBlockBag to, ushort itemId)
        {
            if (lootBagPrefab == null || from == null || to == null)
                return;
            Transform model = _freeModels.Count > 0 ? _freeModels.Pop() : MakeTossModel();
            if (model == null)
                return;
            if (_tosses.Count >= TossPool)
            {
                Release(_tosses[0].Model);
                _tosses.RemoveAt(0);
            }
            model.gameObject.SetActive(true);
            _tosses.Add(new Toss { Model = model, From = from.transform, To = to.transform, Start = Time.time });
        }

        Transform MakeTossModel()
        {
            var source = lootBagPrefab.GetComponentInChildren<MeshFilter>();
            var sourceRenderer = lootBagPrefab.GetComponentInChildren<MeshRenderer>();
            if (source == null || sourceRenderer == null)
                return null;
            var go = new GameObject("Tossed Loot Bag");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = sourceRenderer.sharedMaterial;
            go.transform.localScale = Vector3.one * tossScale;
            return go.transform;
        }

        void Release(Transform model)
        {
            model.gameObject.SetActive(false);
            _freeModels.Push(model);
        }

        void LateUpdate()
        {
            for (int i = _tosses.Count - 1; i >= 0; i--)
            {
                Toss toss = _tosses[i];
                float t = (Time.time - toss.Start) / tossSeconds;
                if (t >= 1f || toss.From == null || toss.To == null)
                {
                    if (toss.To != null)
                        PlayPop((float3)toss.To.position + new float3(0f, HeroMiddle, 0f), 0.8f);
                    Release(toss.Model);
                    _tosses.RemoveAt(i);
                    continue;
                }
                // From the thrower's hands to the catcher's middle, following both as they move, in a lob.
                Vector3 a = toss.From.position + Vector3.up * 1.1f;
                Vector3 b = toss.To.position + Vector3.up * HeroMiddle;
                float lift = tossHeight * Mathf.Clamp(Vector3.Distance(a, b) / 8f, 0.4f, 3f);
                toss.Model.position = Vector3.Lerp(a, b, t) + Vector3.up * (4f * lift * t * (1f - t));
                toss.Model.rotation = Quaternion.Euler(t * 540f, t * 200f, 0f);
            }
        }

        void PlayPop(float3 position, float pitch)
        {
            if (router != null && sounds != null && sounds.PickupPop != null)
                router.Play(sounds.PickupPop, position, 0.5f, pitch * UnityEngine.Random.Range(0.95f, 1.05f), 16f);
        }
    }
}
