using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OmniBot.VR.Core
{
    /// <summary>
    /// Pulls the topic and <c>msg</c> out of a rosbridge publish, including a
    /// <c>png</c>-compressed envelope. The socket thread can run this; it does
    /// not touch Unity objects.
    /// </summary>
    public static class RosbridgeIncoming
    {
        public static bool TryExtractPublish(string json, out string topic, out string msgJson)
        {
            topic = null;
            msgJson = null;
            if (string.IsNullOrEmpty(json)) return false;

            JObject root;
            try { root = JObject.Parse(json); }
            catch (JsonException) { return false; }

            string op = root["op"]?.ToString();
            if (op == "png")
            {
                if (!RosbridgePng.TryDecodeToJson(root["data"]?.ToString(), out string inner))
                {
                    Debug.LogWarning("[ROSBridgeClient] Could not decode a png-compressed message.");
                    return false;
                }
                try { root = JObject.Parse(inner); }
                catch (JsonException) { return false; }
                op = root["op"]?.ToString();
            }
            if (op != "publish") return false;

            topic = root["topic"]?.ToString();
            if (string.IsNullOrEmpty(topic) || root["msg"] == null || root["msg"].Type == JTokenType.Null)
                return false;
            msgJson = root["msg"].ToString(Formatting.None);
            return true;
        }
    }
}
