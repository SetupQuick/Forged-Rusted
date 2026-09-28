using UnityEngine;

namespace Networking
{
    [RequireComponent(typeof(NetObject))]
    public abstract class NetBehaviour
        : MonoBehaviour
    {
        private uint _stateVersion = 1;

        public abstract NetBehaviourType ComponentType
        {
            get;
        }

        public NetObject NetObject
        {
            get;
            private set;
        }

        public uint StateVersion =>
            _stateVersion;

        internal void Bind(
            NetObject netObject)
        {
            NetObject = netObject;
        }

        protected void MarkDirty()
        {
            unchecked
            {
                _stateVersion++;

                // Reserve zero for "no state known".
                if (_stateVersion == 0)
                {
                    _stateVersion = 1;
                }
            }
        }

        public virtual void RefreshReplicationState()
        {
        }

        public virtual void OnNetSpawn()
        {
        }

        public virtual void OnNetDespawn()
        {
        }

        public abstract void WriteState(
            PacketWriter writer);

        public abstract void ReadState(
            PacketReader reader,
            uint serverTick);
    }
}