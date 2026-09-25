using UnityEngine;
using UnityEngine.EventSystems;
using OmniBot.VR.MR;

namespace OmniBot.VR.UI
{
    public class SpatialWindowManipulator : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public enum Mode { Move, Resize }
        public Mode mode = Mode.Move;
        
        public RectTransform targetWindow;
        private bool isDragging;
        
        private Transform rayOrigin;
        private Vector3 positionOffset;
        private Quaternion rotationOffset;
        
        private Vector2 initialSize;
        private Vector3 initialHitLocal;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (targetWindow == null) return;

            var module = EventSystem.current.GetComponent<OVRInputModule>();
            if (module != null && module.rayTransform != null)
            {
                rayOrigin = module.rayTransform;
                isDragging = true;

                var placer = targetWindow.GetComponent<WorldSpaceUiPlacer>();
                if (placer != null) placer.enabled = false;

                if (mode == Mode.Move)
                {
                    positionOffset = Quaternion.Inverse(rayOrigin.rotation) * (targetWindow.position - rayOrigin.position);
                    rotationOffset = Quaternion.Inverse(rayOrigin.rotation) * targetWindow.rotation;
                }
                else if (mode == Mode.Resize)
                {
                    initialSize = targetWindow.sizeDelta;
                    initialHitLocal = targetWindow.InverseTransformPoint(eventData.pointerCurrentRaycast.worldPosition);
                }
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            isDragging = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
        }

        private void Update()
        {
            if (isDragging && rayOrigin != null && targetWindow != null)
            {
                if (mode == Mode.Move)
                {
                    targetWindow.position = rayOrigin.position + rayOrigin.rotation * positionOffset;
                    targetWindow.rotation = rayOrigin.rotation * rotationOffset;
                }
                else if (mode == Mode.Resize)
                {
                    Plane canvasPlane = new Plane(targetWindow.forward, targetWindow.position);
                    Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
                    if (canvasPlane.Raycast(ray, out float enter))
                    {
                        Vector3 hitPoint = ray.GetPoint(enter);
                        Vector3 localHit = targetWindow.InverseTransformPoint(hitPoint);
                        
                        Vector3 localDelta = localHit - initialHitLocal;
                        float newWidth = Mathf.Max(400f, initialSize.x + localDelta.x);
                        float newHeight = Mathf.Max(300f, initialSize.y - localDelta.y);
                        
                        targetWindow.sizeDelta = new Vector2(newWidth, newHeight);
                    }
                }
            }
        }
    }
}
