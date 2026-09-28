using System;
using UnityEngine;

namespace Networking
{
    public sealed class NetworkDiagnostics
    {
        public const int CorrectionHistoryCapacity = 120;

        private readonly float[] _correctionHistory =
            new float[CorrectionHistoryCapacity];

        private readonly int _pendingInputWarningThreshold;
        private readonly float _correctionLogThreshold;

        private int _correctionHistoryStart;
        private int _correctionHistoryCount;
        private float _lastPendingWarningTime = -10f;

        public uint ServerTick { get; internal set; }
        public double RoundTripTimeMilliseconds { get; internal set; }
        public int PendingInputCount { get; private set; }
        public uint LatestSentInputSequence { get; internal set; }
        public uint LatestAcknowledgedInputSequence { get; private set; }
        public float LatestCorrectionDistance { get; private set; }
        public float MaximumCorrectionDistance { get; private set; }

        public ulong PacketsSent { get; internal set; }
        public ulong PacketsReceived { get; internal set; }
        public ulong BytesSent { get; internal set; }
        public ulong BytesReceived { get; internal set; }
        public ulong SimulatedPacketsDropped { get; internal set; }
        public ulong SimulatedPacketsReordered { get; internal set; }

        public int CorrectionHistoryCount => _correctionHistoryCount;

        public NetworkDiagnostics(
            int pendingInputWarningThreshold,
            float correctionLogThreshold)
        {
            _pendingInputWarningThreshold =
                Math.Max(1, pendingInputWarningThreshold);

            _correctionLogThreshold =
                Math.Max(0f, correctionLogThreshold);
        }

        public void ReportPendingInputs(
            int count,
            uint networkId)
        {
            PendingInputCount = Math.Max(0, count);

            if (PendingInputCount <=
                    _pendingInputWarningThreshold ||
                Time.unscaledTime -
                    _lastPendingWarningTime < 1f)
            {
                return;
            }

            _lastPendingWarningTime = Time.unscaledTime;

            Debug.LogWarning(
                $"Network prediction queue for object " +
                $"{networkId} contains {PendingInputCount} " +
                $"inputs (healthy threshold: " +
                $"{_pendingInputWarningThreshold}).");
        }

        public void ReportReconciliation(
            uint acknowledgedInput,
            float correctionDistance)
        {
            LatestAcknowledgedInputSequence =
                acknowledgedInput;

            LatestCorrectionDistance =
                Math.Max(0f, correctionDistance);

            MaximumCorrectionDistance =
                Math.Max(
                    MaximumCorrectionDistance,
                    LatestCorrectionDistance);

            int writeIndex =
                (_correctionHistoryStart +
                 _correctionHistoryCount) %
                CorrectionHistoryCapacity;

            _correctionHistory[writeIndex] =
                LatestCorrectionDistance;

            if (_correctionHistoryCount <
                CorrectionHistoryCapacity)
            {
                _correctionHistoryCount++;
            }
            else
            {
                _correctionHistoryStart =
                    (_correctionHistoryStart + 1) %
                    CorrectionHistoryCapacity;
            }

            if (_correctionLogThreshold > 0f &&
                LatestCorrectionDistance >=
                    _correctionLogThreshold)
            {
                Debug.Log(
                    $"Reconciliation before restore: " +
                    $"ack={acknowledgedInput}, " +
                    $"correction=" +
                    $"{LatestCorrectionDistance:F4} m.");
            }
        }

        public float GetCorrectionHistory(int index)
        {
            if (index < 0 ||
                index >= _correctionHistoryCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index));
            }

            return _correctionHistory[
                (_correctionHistoryStart + index) %
                CorrectionHistoryCapacity];
        }
    }
}
