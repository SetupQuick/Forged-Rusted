using System;
using UnityEngine;

namespace Networking
{
    [DisallowMultipleComponent]
    public sealed class NetTransform : NetBehaviour
    {
        [SerializeField]
        private float interpolationSharpness = 15f;

        [SerializeField]
        private float snapDistance = 5f;

        [SerializeField]
        private float replicationPositionThreshold = 0.001f;

        [SerializeField]
        private float replicationRotationThreshold = 0.1f;

        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private bool _hasTarget;

        private Vector3 _lastObservedPosition;
        private Quaternion _lastObservedRotation;

        private bool _hasObservedReplicationState;

        public override NetBehaviourType ComponentType =>
            NetBehaviourType.Transform;

        public override void OnNetSpawn()
        {
            _targetPosition =
                transform.position;

            _targetRotation =
                transform.rotation;

            _hasTarget = false;

            _lastObservedPosition =
                transform.position;

            _lastObservedRotation =
                transform.rotation;

            _hasObservedReplicationState = false;
        }

        public override void OnNetDespawn()
        {
            _hasTarget = false;

            _hasObservedReplicationState = false;
        }

        public override void RefreshReplicationState()
        {
            Vector3 currentPosition =
                transform.position;

            Quaternion currentRotation =
                transform.rotation;

            if (!_hasObservedReplicationState)
            {
                _hasObservedReplicationState = true;

                _lastObservedPosition =
                    currentPosition;

                _lastObservedRotation =
                    currentRotation;

                MarkDirty();

                return;
            }

            float positionThresholdSquared =
                replicationPositionThreshold *
                replicationPositionThreshold;

            bool positionChanged =
                (currentPosition -
                _lastObservedPosition)
                .sqrMagnitude >=
                positionThresholdSquared;

            bool rotationChanged =
                Quaternion.Angle(
                    currentRotation,
                    _lastObservedRotation) >=
                replicationRotationThreshold;

            if (!positionChanged &&
                !rotationChanged)
            {
                return;
            }

            _lastObservedPosition =
                currentPosition;

            _lastObservedRotation =
                currentRotation;

            MarkDirty();
        }

        public override void WriteState(
            PacketWriter writer)
        {
            var message =
                new NetTransformState(
                    transform.position,
                    transform.rotation);

            message.Write(writer);
        }

        public override void ReadState(
            PacketReader reader,
            uint serverTick)
        {
            if (!NetTransformState.TryRead(
                    reader,
                    out NetTransformState message))
            {
                throw new InvalidOperationException(
                    "Received an invalid transform snapshot.");
            }

            _targetPosition =
                message.Position;

            _targetRotation =
                message.Rotation;

            if (!_hasTarget ||
                Vector3.Distance(
                    transform.position,
                    _targetPosition) >= snapDistance)
            {
                transform.SetPositionAndRotation(
                    _targetPosition,
                    _targetRotation);
            }

            _hasTarget = true;
        }

        public void Interpolate(float deltaTime)
        {
            if (!_hasTarget ||
                deltaTime <= 0f)
            {
                return;
            }

            float amount =
                1f -
                Mathf.Exp(
                    -interpolationSharpness *
                    deltaTime);

            transform.position =
                Vector3.Lerp(
                    transform.position,
                    _targetPosition,
                    amount);

            transform.rotation =
                Quaternion.Slerp(
                    transform.rotation,
                    _targetRotation,
                    amount);
        }
    }
}