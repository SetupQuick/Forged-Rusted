using System;
using System.Collections.Generic;
using UnityEngine;

namespace Networking
{
    public sealed class ClientWorldReplication :
        IDisposable
    {
        private const int SpawnPayloadSize =
            sizeof(ushort) +
            sizeof(uint) +
            sizeof(uint) +
            (sizeof(float) * 3) +
            (sizeof(float) * 4);

        private const int MaximumSnapshotEntries = 32;

        private readonly ClientMessageRouter _router;
        private readonly NetworkWorld _world;

        private readonly HashSet<NetTransform>
            _interpolated =
                new HashSet<NetTransform>();

        private bool _disposed;

        public ClientWorldReplication(
            ClientMessageRouter router,
            NetworkWorld world)
        {
            _router = router ??
                throw new ArgumentNullException(
                    nameof(router));

            _world = world ??
                throw new ArgumentNullException(
                    nameof(world));

            _router.Register(
                NetworkMessageType.ObjectSpawn,
                HandleSpawnSafe);

            _router.Register(
                NetworkMessageType.ObjectDespawn,
                HandleDespawnSafe);

            _router.Register(
                NetworkMessageType.WorldSnapshot,
                HandleWorldSnapshotSafe);
        }

        public void Interpolate(float deltaTime)
        {
            if (_disposed ||
                deltaTime <= 0f)
            {
                return;
            }

            NetworkRuntime runtime =
                NetworkRuntime.Current;

            if (runtime != null &&
                runtime.RunsServer)
            {
                // Listen server shares authoritative objects.
                return;
            }

            _interpolated.RemoveWhere(
                netTransform =>
                    netTransform == null ||
                    netTransform.NetObject == null ||
                    !netTransform.NetObject.IsSpawned);

            foreach (
                NetTransform netTransform
                in _interpolated)
            {
                netTransform.Interpolate(
                    deltaTime);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _router.Unregister(
                NetworkMessageType.ObjectSpawn,
                HandleSpawnSafe);

            _router.Unregister(
                NetworkMessageType.ObjectDespawn,
                HandleDespawnSafe);

            _router.Unregister(
                NetworkMessageType.WorldSnapshot,
                HandleWorldSnapshotSafe);

            _interpolated.Clear();
        }

        private void HandleSpawnSafe(byte[] data)
        {
            try
            {
                HandleSpawn(data);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning(
                    $"Discarded malformed ObjectSpawn: " +
                    exception.Message);
            }
        }

        private void HandleDespawnSafe(byte[] data)
        {
            try
            {
                HandleDespawn(data);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning(
                    $"Discarded malformed ObjectDespawn: " +
                    exception.Message);
            }
        }

        private void HandleWorldSnapshotSafe(
            byte[] data)
        {
            try
            {
                HandleWorldSnapshot(data);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning(
                    $"Discarded malformed WorldSnapshot: " +
                    exception.Message);
            }
        }

        private void HandleSpawn(byte[] data)
        {
            if (data == null ||
                data.Length != SpawnPayloadSize)
            {
                return;
            }

            var reader =
                new PacketReader(data);

            ushort prefabId =
                reader.ReadUInt16();

            uint networkId =
                reader.ReadUInt32();

            uint ownerPeerId =
                reader.ReadUInt32();

            var position =
                new Vector3(
                    reader.ReadFloat(),
                    reader.ReadFloat(),
                    reader.ReadFloat());

            var rotation =
                new Quaternion(
                    reader.ReadFloat(),
                    reader.ReadFloat(),
                    reader.ReadFloat(),
                    reader.ReadFloat());

            if (prefabId == 0 ||
                networkId == 0 ||
                !IsFinite(position) ||
                !IsFinite(rotation))
            {
                return;
            }

            float magnitudeSquared =
                rotation.x * rotation.x +
                rotation.y * rotation.y +
                rotation.z * rotation.z +
                rotation.w * rotation.w;

            if (magnitudeSquared < 0.0001f ||
                float.IsInfinity(magnitudeSquared))
            {
                return;
            }

            _world.SpawnOrResolveReplica(
                prefabId,
                networkId,
                ownerPeerId,
                position,
                rotation.normalized);
        }

        private void HandleDespawn(byte[] data)
        {
            if (data == null ||
                data.Length != sizeof(uint))
            {
                return;
            }

            var reader =
                new PacketReader(data);

            uint networkId =
                reader.ReadUInt32();

            if (networkId == 0)
                return;

            _world.DespawnReplica(
                networkId);
        }

        private void HandleWorldSnapshot(
            byte[] data)
        {
            NetworkRuntime runtime =
                NetworkRuntime.Current;

            if (runtime != null &&
                runtime.RunsServer)
            {
                // Listen servers already own authoritative state.
                return;
            }

            if (data == null ||
                data.Length <
                    sizeof(uint) +
                    sizeof(ushort))
            {
                return;
            }

            var reader =
                new PacketReader(data);

            uint serverTick =
                reader.ReadUInt32();

            ushort entryCount =
                reader.ReadUInt16();

            if (entryCount >
                MaximumSnapshotEntries)
            {
                return;
            }

            for (int index = 0;
                 index < entryCount;
                 index++)
            {
                const int entryHeaderSize =
                    sizeof(uint) +
                    sizeof(byte) +
                    sizeof(ushort);

                if (reader.Remaining <
                    entryHeaderSize)
                {
                    return;
                }

                uint networkId =
                    reader.ReadUInt32();

                var componentType =
                    (NetBehaviourType)
                    reader.ReadByte();

                ushort stateLength =
                    reader.ReadUInt16();

                if (networkId == 0 ||
                    stateLength == 0 ||
                    reader.Remaining <
                        stateLength)
                {
                    return;
                }

                byte[] state =
                    reader.ReadBytes(
                        stateLength);

                if (!_world.TryGet(
                        networkId,
                        out NetObject netObject) ||
                    netObject == null)
                {
                    continue;
                }

                if (componentType ==
                        NetBehaviourType.Transform &&
                    netObject.IsLocallyOwned)
                {
                    /*
                    * Locally owned movement is driven by
                    * ClientPlayerMovement prediction and
                    * PlayerMovementState reconciliation.
                    */
                    continue;
                }

                if (!netObject.TryApplyState(
                        componentType,
                        state,
                        serverTick))
                {
                    continue;
                }

                if (componentType ==
                        NetBehaviourType.Transform &&
                    netObject.TryGetBehaviour(
                        NetBehaviourType.Transform,
                        out NetBehaviour behaviour) &&
                    behaviour is
                        NetTransform netTransform)
                {
                    _interpolated.Add(
                        netTransform);
                }
            }

            if (reader.Remaining != 0)
            {
                throw new InvalidOperationException(
                    "WorldSnapshot contains trailing bytes.");
            }

            if (runtime?.Diagnostics != null)
            {
                runtime.Diagnostics.ServerTick =
                    serverTick;
            }
        }

        private static bool IsFinite(
            Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(
            Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}