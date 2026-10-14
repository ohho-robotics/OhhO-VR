using UnityEngine;

namespace OmniBot.VR.UI
{
    public class SmoothHUD : MonoBehaviour
    {
        public Transform targetCamera;
        public float followSpeed = 4f;
        public float rotationSpeed = 4f;
        public float triggerAngle = 25f;

        /// <summary>True while operate mode holds the rig still.</summary>
        public bool IsPlacementLocked { get; private set; }

        private bool _isFollowingRotation = false;

        public void SetPlacementLocked(bool locked) => IsPlacementLocked = locked;

        public void SetViewCamera(Transform camera) => targetCamera = camera;

        /// <summary>
        /// Snap the rig onto the head yaw. Children parked at +Z stay in front.
        /// Does not unlock placement.
        /// </summary>
        public void Recentre()
        {
            FindCamera();
            if (targetCamera == null) return;
            Vector3 fwd = Flatten(targetCamera.forward);
            Vector3 pos = targetCamera.position;
            pos.y -= 0.1f;
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(fwd);
            _isFollowingRotation = false;
        }

        private void Start()
        {
            FindCamera();
            if (targetCamera != null)
            {
                Vector3 fwd = targetCamera.forward;
                fwd.y = 0;
                
                Vector3 targetPos = targetCamera.position;
                targetPos.y -= 0.1f; 
                transform.position = targetPos;
                
                if (fwd.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.LookRotation(fwd.normalized);
            }
        }

        private void FindCamera()
        {
            if (targetCamera == null)
            {
                if (Camera.main != null) targetCamera = Camera.main.transform;
                else
                {
                    var centerEye = GameObject.Find("CenterEyeAnchor");
                    if (centerEye != null) targetCamera = centerEye.transform;
                    else
                    {
                        var anyCam = FindObjectOfType<Camera>();
                        if (anyCam != null) targetCamera = anyCam.transform;
                    }
                }
            }
        }

        private void LateUpdate() => TickFollow(Time.deltaTime);

        /// <summary>One follow step. Locked placement does not move or yaw.</summary>
        public void TickFollow(float deltaTime)
        {
            if (IsPlacementLocked) return;
            FindCamera();
            if (targetCamera == null) return;

            Vector3 targetPos = targetCamera.position;
            targetPos.y -= 0.1f;
            transform.position = Vector3.Lerp(transform.position, targetPos, deltaTime * followSpeed);

            Vector3 fwd = Flatten(targetCamera.forward);
            Quaternion headYaw = Quaternion.LookRotation(fwd);
            float angle = Quaternion.Angle(transform.rotation, headYaw);
            if (angle > triggerAngle) _isFollowingRotation = true;
            if (angle < 2f) _isFollowingRotation = false;
            if (_isFollowingRotation)
                transform.rotation = Quaternion.Slerp(transform.rotation, headYaw, deltaTime * rotationSpeed);
        }

        private static Vector3 Flatten(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) return Vector3.forward;
            return forward.normalized;
        }
    }
}
