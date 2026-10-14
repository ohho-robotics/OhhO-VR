using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OmniBot.VR.Core
{
    /// <summary>
    /// rosbridge v2 <c>call_service</c> / <c>service_response</c> JSON.
    /// Kept free of the WebSocket so edit-mode tests can lock the payload.
    /// </summary>
    public static class RosBridgeServiceCall
    {
        public static string FormatRequest(string id, string service, string type)
        {
            var payload = new JObject
            {
                ["op"] = "call_service",
                ["id"] = id,
                ["service"] = service,
                ["type"] = type,
                ["args"] = new JObject(),
            };
            return payload.ToString(Formatting.None);
        }

        /// <summary>
        /// Parse a service_response. <c>std_srvs/Trigger</c> reports the outcome
        /// in <c>values.success</c>; <paramref name="ok"/> prefers that, then
        /// the transport <c>result</c> flag.
        /// </summary>
        public static bool TryParseResponse(string json, out string id, out bool ok, out string valuesJson)
        {
            id = null;
            ok = false;
            valuesJson = "";
            if (string.IsNullOrEmpty(json)) return false;

            try
            {
                var root = JObject.Parse(json);
                id = root["id"]?.ToString();
                var values = root["values"] as JObject;
                valuesJson = values != null ? values.ToString(Formatting.None) : "";
                if (values != null && values["success"] != null && values["success"].Type == JTokenType.Boolean)
                    ok = values.Value<bool>("success");
                else if (root["result"] != null && root["result"].Type == JTokenType.Boolean)
                    ok = root.Value<bool>("result");
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
