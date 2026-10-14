using UnityEngine;
using UnityEngine.EventSystems;

namespace OmniBot.VR.Mapping
{
    /// <summary>
    /// Forwards a UI click on the map image to <see cref="SlamMapPanel"/>.
    /// The image is the raycast target; the arrow and scan overlay are not.
    /// </summary>
    public sealed class MapPointerClick : MonoBehaviour, IPointerClickHandler
    {
        public SlamMapPanel Panel;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Panel != null) Panel.OnMapClicked(eventData);
        }
    }
}
