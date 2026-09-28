using System;
using System.Collections.Generic;
using UnityEngine;

namespace Networking
{
    [DisallowMultipleComponent]
    public sealed class NetObject : MonoBehaviour
    {
        [SerializeField]
        private ushort prefabId;

        private readonly Dictionary
            <NetBehaviourType, NetBehaviour>
            _behavioursByType =
                new Dictionary
                    <NetBehaviourType, NetBehaviour>();

        private NetBehaviour[] _behaviours =
            Array.Empty<NetBehaviour>();

        public ushort PrefabId => prefabId;

        public uint NetworkId
        {
            get;
            private set;
        }

        public uint OwnerPeerId
        {
            get;
            private set;
        }

        public bool IsSpawned =>
            NetworkId != 0;

        public IReadOnlyList<NetBehaviour>
            Behaviours => _behaviours;

        public bool IsLocallyOwned
        {
            get
            {
                NetworkRuntime runtime =
                    NetworkRuntime.Current;

                return
                    IsSpawned &&
                    runtime != null &&
                    runtime.RunsClient &&
                    runtime.LocalPeerId != 0 &&
                    OwnerPeerId ==
                        runtime.LocalPeerId;
            }
        }

        private void Awake()
        {
            CacheBehaviours();
        }

        internal void Initialize(
            uint networkId,
            uint ownerPeerId)
        {
            if (IsSpawned)
            {
                throw new InvalidOperationException(
                    "NetObject is already spawned.");
            }

            if (networkId == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(networkId));
            }

            NetworkId = networkId;
            OwnerPeerId = ownerPeerId;

            for (int index = 0;
                 index < _behaviours.Length;
                 index++)
            {
                _behaviours[index].OnNetSpawn();
            }
        }

        internal void ChangeOwner(
            uint ownerPeerId)
        {
            if (!IsSpawned)
            {
                throw new InvalidOperationException(
                    "NetObject is not spawned.");
            }

            OwnerPeerId = ownerPeerId;
        }

        internal void ClearNetworkState()
        {
            if (!IsSpawned)
                return;

            for (int index = 0;
                 index < _behaviours.Length;
                 index++)
            {
                _behaviours[index].OnNetDespawn();
            }

            NetworkId = 0;
            OwnerPeerId = 0;
        }

        public bool TryGetBehaviour(
            NetBehaviourType componentType,
            out NetBehaviour behaviour)
        {
            return _behavioursByType.TryGetValue(
                componentType,
                out behaviour);
        }

        public bool TryApplyState(
            NetBehaviourType componentType,
            byte[] state,
            uint serverTick)
        {
            if (state == null)
            {
                throw new ArgumentNullException(
                    nameof(state));
            }

            if (!_behavioursByType.TryGetValue(
                    componentType,
                    out NetBehaviour behaviour))
            {
                return false;
            }

            var reader =
                new PacketReader(state);

            behaviour.ReadState(
                reader,
                serverTick);

            if (reader.Remaining != 0)
            {
                throw new InvalidOperationException(
                    $"{componentType} did not consume " +
                    "its complete state.");
            }

            return true;
        }

        private void CacheBehaviours()
        {
            // This Unity component lookup happens once.
            _behaviours =
                GetComponents<NetBehaviour>();

            _behavioursByType.Clear();

            for (int index = 0;
                 index < _behaviours.Length;
                 index++)
            {
                NetBehaviour behaviour =
                    _behaviours[index];

                NetBehaviourType type =
                    behaviour.ComponentType;

                if (_behavioursByType.ContainsKey(type))
                {
                    throw new InvalidOperationException(
                        $"{name} contains more than one " +
                        $"{type} behaviour.");
                }

                behaviour.Bind(this);

                _behavioursByType.Add(
                    type,
                    behaviour);
            }
        }
    }
}