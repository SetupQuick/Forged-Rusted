using System;
using UnityEngine;

namespace Networking
{
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        [Header("Presentation")]
        [SerializeField]
        private Transform presentationRoot;

        [SerializeField]
        private Transform cameraPivot;

        [Header("Camera")]
        [SerializeField]
        private Camera playerCamera;

        [SerializeField]
        private AudioListener audioListener;

        [Header("Crouching")]
        [SerializeField]
        private CharacterMotor characterMotor;

        [SerializeField]
        [Min(0f)]
        private float crouchCameraDrop = 0.8f;

        [Header("Presentation smoothing")]
        [SerializeField]
        [Min(0f)]
        private float positionSmoothTime = 0.035f;

        [SerializeField]
        [Min(0f)]
        private float snapDistance = 4f;

        private Vector3 _presentationLocalPosition;
        private Quaternion _presentationLocalRotation;

        private Vector3 _renderedPosition;
        private Vector3 _positionVelocity;

        private Vector3 _cameraLocalPosition;
        private Quaternion _cameraLocalRotation;

        private Vector3 _cameraPivotStandingLocalPosition;

        private float _pitch;

        private bool _active;
        private bool _cameraDetached;

        public float Pitch => _pitch;

        private void Awake()
        {
            if (playerCamera != null &&
                audioListener == null)
            {
                audioListener =
                    playerCamera.GetComponent<AudioListener>();
            }

            if (characterMotor == null)
            {
                characterMotor =
                    GetComponent<CharacterMotor>();
            }

            if (presentationRoot != null)
            {
                _presentationLocalPosition =
                    presentationRoot.localPosition;

                _presentationLocalRotation =
                    presentationRoot.localRotation;
            }

            if (cameraPivot != null)
            {
                _cameraPivotStandingLocalPosition =
                    cameraPivot.localPosition;
            }

            if (cameraPivot != null &&
                playerCamera != null)
            {
                CacheCameraOffset();

                _pitch =
                    NormalizeAngle(
                        cameraPivot.localEulerAngles.x);
            }

            SetOutputActive(false);
        }

        public void Activate()
        {
            if (_active)
                return;

            ValidateReferences();

            _presentationLocalPosition =
                presentationRoot.localPosition;

            _presentationLocalRotation =
                presentationRoot.localRotation;

            _cameraPivotStandingLocalPosition =
                cameraPivot.localPosition;

            CacheCameraOffset();

            playerCamera.transform.SetParent(
                null,
                true);

            _cameraDetached = true;
            _active = true;

            _positionVelocity =
                Vector3.zero;

            _renderedPosition =
                GetPresentationTargetPosition();

            ApplyPresentationImmediately();
            ApplyCrouchCamera();
            ApplyCameraImmediately();

            SetOutputActive(true);
        }

        public void Deactivate()
        {
            _active = false;

            _positionVelocity =
                Vector3.zero;

            SetOutputActive(false);

            if (_cameraDetached &&
                playerCamera != null &&
                cameraPivot != null)
            {
                playerCamera.transform.SetParent(
                    cameraPivot,
                    false);

                playerCamera.transform.localPosition =
                    _cameraLocalPosition;

                playerCamera.transform.localRotation =
                    _cameraLocalRotation;

                _cameraDetached = false;
            }

            if (cameraPivot != null)
            {
                cameraPivot.localPosition =
                    _cameraPivotStandingLocalPosition;
            }

            if (presentationRoot != null)
            {
                presentationRoot.localPosition =
                    _presentationLocalPosition;

                presentationRoot.localRotation =
                    _presentationLocalRotation;
            }
        }

        public void SetPitch(float pitch)
        {
            _pitch =
                pitch;

            if (cameraPivot == null)
                return;

            cameraPivot.localRotation =
                Quaternion.Euler(
                    _pitch,
                    0f,
                    0f);
        }

        private void LateUpdate()
        {
            if (!_active)
                return;

            UpdatePresentation();
            ApplyCrouchCamera();
            UpdateCamera();
        }

        private void UpdatePresentation()
        {
            Vector3 targetPosition =
                GetPresentationTargetPosition();

            Quaternion targetRotation =
                GetPresentationTargetRotation();

            float distance =
                Vector3.Distance(
                    _renderedPosition,
                    targetPosition);

            if (positionSmoothTime <= 0f ||
                (snapDistance > 0f &&
                 distance >= snapDistance))
            {
                _renderedPosition =
                    targetPosition;

                _positionVelocity =
                    Vector3.zero;
            }
            else
            {
                _renderedPosition =
                    Vector3.SmoothDamp(
                        _renderedPosition,
                        targetPosition,
                        ref _positionVelocity,
                        positionSmoothTime,
                        Mathf.Infinity,
                        Time.unscaledDeltaTime);
            }

            presentationRoot.SetPositionAndRotation(
                _renderedPosition,
                targetRotation);
        }

        private void ApplyCrouchCamera()
        {
            if (cameraPivot == null ||
                characterMotor == null)
            {
                return;
            }

            Vector3 position =
                _cameraPivotStandingLocalPosition;

            position.y -=
                crouchCameraDrop *
                characterMotor.CrouchAmount;

            cameraPivot.localPosition =
                position;
        }

        private void UpdateCamera()
        {
            Vector3 desiredPosition =
                cameraPivot.TransformPoint(
                    _cameraLocalPosition);

            Quaternion desiredRotation =
                cameraPivot.rotation *
                _cameraLocalRotation;

            playerCamera.transform.SetPositionAndRotation(
                desiredPosition,
                desiredRotation);
        }

        private Vector3 GetPresentationTargetPosition()
        {
            return transform.TransformPoint(
                _presentationLocalPosition);
        }

        private Quaternion GetPresentationTargetRotation()
        {
            return transform.rotation *
                   _presentationLocalRotation;
        }

        private void ApplyPresentationImmediately()
        {
            _renderedPosition =
                GetPresentationTargetPosition();

            presentationRoot.SetPositionAndRotation(
                _renderedPosition,
                GetPresentationTargetRotation());

            _positionVelocity =
                Vector3.zero;
        }

        private void ApplyCameraImmediately()
        {
            playerCamera.transform.SetPositionAndRotation(
                cameraPivot.TransformPoint(
                    _cameraLocalPosition),
                cameraPivot.rotation *
                    _cameraLocalRotation);
        }

        private void CacheCameraOffset()
        {
            _cameraLocalPosition =
                cameraPivot.InverseTransformPoint(
                    playerCamera.transform.position);

            _cameraLocalRotation =
                Quaternion.Inverse(
                    cameraPivot.rotation) *
                playerCamera.transform.rotation;
        }

        private void ValidateReferences()
        {
            if (presentationRoot == null)
            {
                throw new InvalidOperationException(
                    "Assign PresentationRoot to PlayerCameraRig.");
            }

            if (presentationRoot == transform)
            {
                throw new InvalidOperationException(
                    "PresentationRoot must be a child of the " +
                    "player simulation object.");
            }

            if (cameraPivot == null)
            {
                throw new InvalidOperationException(
                    "Assign CameraPivot to PlayerCameraRig.");
            }

            if (!cameraPivot.IsChildOf(
                    presentationRoot))
            {
                throw new InvalidOperationException(
                    "CameraPivot must be a child of " +
                    "PresentationRoot.");
            }

            if (playerCamera == null)
            {
                throw new InvalidOperationException(
                    "Assign PlayerCamera to PlayerCameraRig.");
            }

            if (characterMotor == null)
            {
                throw new InvalidOperationException(
                    "Assign CharacterMotor to PlayerCameraRig.");
            }
        }

        private void SetOutputActive(bool active)
        {
            if (playerCamera != null)
                playerCamera.enabled = active;

            if (audioListener != null)
                audioListener.enabled = active;
        }

        private void OnDestroy()
        {
            _active = false;

            SetOutputActive(false);

            // During destruction, do not try to parent the detached camera
            // back under CameraPivot. The player's hierarchy may already
            // be in the middle of being destroyed by Unity.
            //
            // Because the local camera was detached from the player root,
            // destroy it explicitly so it does not survive as an orphan.
            if (_cameraDetached &&
                playerCamera != null)
            {
                Destroy(playerCamera.gameObject);
            }

            _cameraDetached = false;
        }

        private static float NormalizeAngle(
            float angle)
        {
            if (angle > 180f)
                angle -= 360f;

            return angle;
        }
    }
}