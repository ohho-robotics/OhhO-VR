using UnityEngine;
using UnityEngine.UI;
using OmniBot.VR.Core;

namespace OmniBot.VR.UI.Teleop
{
    public class ControlsPanelController : MonoBehaviour
    {
        [SerializeField] private Button startRosbagButton;
        [SerializeField] private Button stopRosbagButton;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (startRosbagButton != null) startRosbagButton.onClick.AddListener(OnStartRosbag);
            if (stopRosbagButton != null) stopRosbagButton.onClick.AddListener(OnStopRosbag);
        }

        private void OnDisable()
        {
            if (!Application.isPlaying) return;
            if (startRosbagButton != null) startRosbagButton.onClick.RemoveListener(OnStartRosbag);
            if (stopRosbagButton != null) stopRosbagButton.onClick.RemoveListener(OnStopRosbag);
        }

        private void OnStartRosbag()
        {
            if (ROSBridgeClient.Instance != null && ROSBridgeClient.Instance.IsConnected)
            {
                ROSBridgeClient.Instance.Publish("/mission/command", new { data = "rosbag:start" });
            }
        }

        private void OnStopRosbag()
        {
            if (ROSBridgeClient.Instance != null && ROSBridgeClient.Instance.IsConnected)
            {
                ROSBridgeClient.Instance.Publish("/mission/command", new { data = "rosbag:stop" });
            }
        }
    }
}
