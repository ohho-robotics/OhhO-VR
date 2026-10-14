using Newtonsoft.Json;
using OmniBot.VR.Core;

namespace OmniBot.VR.Twin
{
    /// <summary>tf2_msgs/TFMessage, the msg object ROSBridge already unwrapped.</summary>
    public class TfMessage
    {
        [JsonProperty("transforms")]
        public TfStamped[] transforms;
    }

    public class TfStamped
    {
        [JsonProperty("header")]
        public HeaderMsg header;

        [JsonProperty("child_frame_id")]
        public string child_frame_id;

        [JsonProperty("transform")]
        public TfTransform transform;
    }

    public class TfTransform
    {
        [JsonProperty("translation")]
        public Vector3Msg translation;

        [JsonProperty("rotation")]
        public QuaternionMsg rotation;
    }
}
