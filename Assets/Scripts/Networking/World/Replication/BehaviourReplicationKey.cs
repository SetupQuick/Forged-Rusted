using System;

namespace Networking
{
    public readonly struct BehaviourReplicationKey :
        IEquatable<BehaviourReplicationKey>
    {
        public uint NetworkId { get; }

        public NetBehaviourType ComponentType
        {
            get;
        }

        public BehaviourReplicationKey(
            uint networkId,
            NetBehaviourType componentType)
        {
            NetworkId = networkId;
            ComponentType =
                componentType;
        }

        public bool Equals(
            BehaviourReplicationKey other)
        {
            return NetworkId ==
                       other.NetworkId &&
                   ComponentType ==
                       other.ComponentType;
        }

        public override bool Equals(
            object obj)
        {
            return obj is
                       BehaviourReplicationKey other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return
                    ((int)NetworkId * 397) ^
                    (int)ComponentType;
            }
        }
    }
}