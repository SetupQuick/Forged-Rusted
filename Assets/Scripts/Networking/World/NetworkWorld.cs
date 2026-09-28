using System;
using System.Collections.Generic;
using UnityEngine;

namespace Networking
{
    [DisallowMultipleComponent]
    public sealed class NetworkWorld : MonoBehaviour
    {
        [SerializeField]
        private Transform spawnedObjectRoot;

        [SerializeField]
        private NetPrefabRegistry prefabRegistry;

        private readonly Dictionary<uint, NetObject>
            _objects =
                new Dictionary<uint, NetObject>();

        private NetworkRuntime _runtime;
        private uint _nextNetworkId = 1;
        private bool _initialized;

        public int ObjectCount => _objects.Count;

        public IEnumerable<NetObject> Objects =>
            _objects.Values;

        public event Action<NetObject> ObjectSpawned;
        public event Action<uint> ObjectDespawned;

        internal void Initialize(
            NetworkRuntime runtime)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "NetworkWorld is already initialized.");
            }

            _runtime = runtime ??
                throw new ArgumentNullException(
                    nameof(runtime));

            if (prefabRegistry == null)
            {
                throw new InvalidOperationException(
                    "Assign a NetPrefabRegistry " +
                    "to NetworkWorld.");
            }

            _initialized = true;
        }

        public NetObject SpawnAuthoritative(
            NetObject prefab,
            Vector3 position,
            Quaternion rotation,
            uint ownerPeerId = 0)
        {
            EnsureInitialized();

            if (!_runtime.RunsServer)
            {
                throw new InvalidOperationException(
                    "Only a server runtime can spawn " +
                    "authoritative objects.");
            }

            if (prefab == null)
            {
                throw new ArgumentNullException(
                    nameof(prefab));
            }

            if (!prefabRegistry.Contains(prefab))
            {
                throw new InvalidOperationException(
                    $"Prefab {prefab.name} is not registered " +
                    "in the NetPrefabRegistry.");
            }

            NetObject instance =
                Instantiate(
                    prefab,
                    position,
                    rotation,
                    spawnedObjectRoot);

            try
            {
                Register(
                    instance,
                    AllocateNetworkId(),
                    ownerPeerId);

                return instance;
            }
            catch
            {
                Destroy(instance.gameObject);
                throw;
            }
        }

        public void RegisterReplica(
            NetObject instance,
            uint networkId,
            uint ownerPeerId)
        {
            EnsureInitialized();

            if (!_runtime.RunsClient)
            {
                throw new InvalidOperationException(
                    "This runtime does not contain a client.");
            }

            Register(
                instance,
                networkId,
                ownerPeerId);
        }

        public NetObject SpawnOrResolveReplica(
            ushort prefabId,
            uint networkId,
            uint ownerPeerId,
            Vector3 position,
            Quaternion rotation)
        {
            EnsureInitialized();

            if (!_runtime.RunsClient)
            {
                throw new InvalidOperationException(
                    "This runtime does not contain a client.");
            }

            if (_objects.TryGetValue(
                    networkId,
                    out NetObject existing))
            {
                if (existing == null ||
                    existing.PrefabId != prefabId ||
                    existing.OwnerPeerId != ownerPeerId)
                {
                    throw new InvalidOperationException(
                        $"Spawn data for network ID {networkId} " +
                        "does not match the existing object.");
                }

                // Listen-server path:
                // the authoritative object already exists.
                return existing;
            }

            if (!prefabRegistry.TryGet(
                    prefabId,
                    out NetObject prefab))
            {
                throw new InvalidOperationException(
                    $"No network prefab is registered " +
                    $"for ID {prefabId}.");
            }

            NetObject instance =
                Instantiate(
                    prefab,
                    position,
                    rotation,
                    spawnedObjectRoot);

            try
            {
                Register(
                    instance,
                    networkId,
                    ownerPeerId);

                return instance;
            }
            catch
            {
                Destroy(instance.gameObject);
                throw;
            }
        }

        public bool DespawnReplica(uint networkId)
        {
            EnsureInitialized();

            if (_runtime.RunsServer &&
                _objects.ContainsKey(networkId))
            {
                // On a listen server, this is the shared
                // authoritative object. Server-side despawning
                // owns its destruction.
                return false;
            }

            return Despawn(networkId);
        }

        public bool TryGet(
            uint networkId,
            out NetObject netObject)
        {
            return _objects.TryGetValue(
                networkId,
                out netObject);
        }

        public bool Despawn(uint networkId)
        {
            if (!_objects.TryGetValue(
                    networkId,
                    out NetObject netObject))
            {
                return false;
            }

            _objects.Remove(networkId);

            if (netObject != null)
                netObject.ClearNetworkState();

            ObjectDespawned?.Invoke(networkId);

            if (netObject != null)
                Destroy(netObject.gameObject);

            return true;
        }

        internal void Shutdown()
        {
            if (!_initialized)
                return;

            foreach (NetObject netObject
                     in _objects.Values)
            {
                if (netObject == null)
                    continue;

                netObject.ClearNetworkState();
                Destroy(netObject.gameObject);
            }

            _objects.Clear();

            _nextNetworkId = 1;
            _runtime = null;
            _initialized = false;
        }

        private void Register(
            NetObject netObject,
            uint networkId,
            uint ownerPeerId)
        {
            if (netObject == null)
            {
                throw new ArgumentNullException(
                    nameof(netObject));
            }

            if (networkId == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(networkId));
            }

            if (_objects.ContainsKey(networkId))
            {
                throw new InvalidOperationException(
                    $"Network ID {networkId} " +
                    "is already registered.");
            }

            // Register before invoking OnNetSpawn so the
            // object can resolve itself during that callback.
            _objects.Add(
                networkId,
                netObject);

            try
            {
                netObject.Initialize(
                    networkId,
                    ownerPeerId);
            }
            catch
            {
                _objects.Remove(networkId);
                throw;
            }

            ObjectSpawned?.Invoke(netObject);
        }

        private uint AllocateNetworkId()
        {
            while (true)
            {
                uint candidate =
                    _nextNetworkId++;

                if (candidate != 0 &&
                    !_objects.ContainsKey(candidate))
                {
                    return candidate;
                }
            }
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    "NetworkWorld is not initialized.");
            }
        }
    }
}