using System;
using UnityEngine;

namespace Networking
{
    [Serializable]
    public sealed class NetworkSimulationSettings
    {
        [SerializeField]
        private bool enabled;

        [SerializeField]
        [Min(0f)]
        private float latencyMilliseconds;

        [SerializeField]
        [Min(0f)]
        private float jitterMilliseconds;

        [SerializeField]
        [Range(0f, 100f)]
        private float packetLossPercent;

        [SerializeField]
        [Range(0f, 100f)]
        private float reorderingPercent;

        [SerializeField]
        [Min(0f)]
        private float reorderingDelayMilliseconds = 50f;

        public bool Enabled => enabled;
        public float LatencyMilliseconds => latencyMilliseconds;
        public float JitterMilliseconds => jitterMilliseconds;
        public float PacketLossPercent => packetLossPercent;
        public float ReorderingPercent => reorderingPercent;
        public float ReorderingDelayMilliseconds => reorderingDelayMilliseconds;
    }
}
