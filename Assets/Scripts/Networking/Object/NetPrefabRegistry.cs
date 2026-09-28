using System;
using System.Collections.Generic;
using UnityEngine;

namespace Networking
{
    [CreateAssetMenu(
        fileName = "NetPrefabRegistry",
        menuName = "Networking/Net Prefab Registry")]
    public sealed class NetPrefabRegistry : ScriptableObject
    {
        [SerializeField]
        private NetObject[] prefabs =
            Array.Empty<NetObject>();

        private readonly Dictionary<ushort, NetObject>
            _prefabsById =
                new Dictionary<ushort, NetObject>();

        private bool _cacheBuilt;

        public bool TryGet(
            ushort prefabId,
            out NetObject prefab)
        {
            EnsureCache();

            return _prefabsById.TryGetValue(
                prefabId,
                out prefab);
        }

        public bool Contains(NetObject prefab)
        {
            if (prefab == null ||
                prefab.PrefabId == 0)
            {
                return false;
            }

            EnsureCache();

            return _prefabsById.TryGetValue(
                       prefab.PrefabId,
                       out NetObject registered) &&
                   registered == prefab;
        }

        private void OnEnable()
        {
            _cacheBuilt = false;
        }

        private void OnValidate()
        {
            _cacheBuilt = false;
        }

        private void EnsureCache()
        {
            if (_cacheBuilt)
                return;

            _prefabsById.Clear();

            for (int index = 0;
                 index < prefabs.Length;
                 index++)
            {
                NetObject prefab = prefabs[index];

                if (prefab == null)
                    continue;

                if (prefab.PrefabId == 0)
                {
                    throw new InvalidOperationException(
                        $"Network prefab {prefab.name} " +
                        "has prefab ID 0.");
                }

                if (_prefabsById.ContainsKey(
                        prefab.PrefabId))
                {
                    throw new InvalidOperationException(
                        $"Duplicate network prefab ID " +
                        $"{prefab.PrefabId}.");
                }

                _prefabsById.Add(
                    prefab.PrefabId,
                    prefab);
            }

            _cacheBuilt = true;
        }
    }
}