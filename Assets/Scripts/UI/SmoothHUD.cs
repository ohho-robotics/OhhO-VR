using UnityEngine;

namespace OmniBot.VR.UI
{
    public class SmoothHUD : MonoBehaviour
    {
        public Transform targetCamera;
        public float followSpeed = 4f;
        public float rotationSpeed = 4f;
        public float triggerAngle = 25f; 

        private bool _isFollowingRotation = false;

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

        private void LateUpdate()
        {
            FindCamera();
            if (targetCamera == null) return;

            Vector3 targetPos = targetCamera.position;
            targetPos.y -= 0.1f;
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * followSpeed);

            Vector3 fwd = targetCamera.forward;
            fwd.y = 0;
            if (fwd.sqrMagnitude > 0.001f)
            {
                fwd.Normalize();
                Quaternion headYaw = Quaternion.LookRotation(fwd);
                
                float angle = Quaternion.Angle(transform.rotation, headYaw);
                
                if (angle > triggerAngle) _isFollowingRotation = true;
                if (angle < 2f) _isFollowingRotation = false;

                if (_isFollowingRotation)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, headYaw, Time.deltaTime * rotationSpeed);
                }
            }
        }
    }
}
