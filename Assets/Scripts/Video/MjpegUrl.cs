using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// <c>web_video_server</c> stream URLs. Topic slashes stay unescaped, matching
    /// <see cref="OmniBot.VR.Control.Session.EndpointAddresses.VideoUrlsFor"/>.
    /// <c>quality</c> and <c>width</c> are the server's JPEG knobs; width alone
    /// keeps the camera's aspect ratio.
    /// </summary>
    public static class MjpegUrl
    {
        public const int Port = 8080;
        public const int MinQuality = 1;
        public const int MaxQuality = 100;
        public const int MinWidth = 16;
        public const int MaxWidth = 1920;

        public static int ClampQuality(int quality)
        {
            if (quality < MinQuality) return MinQuality;
            if (quality > MaxQuality) return MaxQuality;
            return quality;
        }

        public static int ClampWidth(int width)
        {
            if (width < MinWidth) return MinWidth;
            if (width > MaxWidth) return MaxWidth;
            return width;
        }

        public static string Build(string host, int port, string topic, int quality, int width)
        {
            quality = ClampQuality(quality);
            width = ClampWidth(width);
            string encoded = Uri.EscapeUriString(topic ?? "");
            return "http://" + host + ":" + port.ToString(CultureInfo.InvariantCulture)
                + "/stream?topic=" + encoded
                + "&quality=" + quality.ToString(CultureInfo.InvariantCulture)
                + "&width=" + width.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Set or replace <c>quality</c> and <c>width</c>. Other query keys stay.</summary>
        public static string WithParams(string url, int quality, int width)
        {
            if (string.IsNullOrEmpty(url)) return url;
            url = SetQuery(url, "quality", ClampQuality(quality).ToString(CultureInfo.InvariantCulture));
            url = SetQuery(url, "width", ClampWidth(width).ToString(CultureInfo.InvariantCulture));
            return url;
        }

        /// <summary>Replace the host and keep the port, path, and query.</summary>
        public static string WithHost(string url, string host)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(host)) return url;
            int scheme = url.IndexOf("://", StringComparison.Ordinal);
            if (scheme < 0) return url;
            int start = scheme + 3;
            int slash = url.IndexOf('/', start);
            if (slash < 0) return url;
            int colon = url.IndexOf(':', start);
            int hostEnd = (colon > start && colon < slash) ? colon : slash;
            return url.Substring(0, start) + host + url.Substring(hostEnd);
        }

        public static string TopicOf(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            int q = url.IndexOf("topic=", StringComparison.OrdinalIgnoreCase);
            if (q < 0) return null;
            int start = q + "topic=".Length;
            int end = url.IndexOf('&', start);
            string raw = end < 0 ? url.Substring(start) : url.Substring(start, end - start);
            return Uri.UnescapeDataString(raw);
        }

        public static string FindByTopic(IList<string> urls, string topic)
        {
            if (urls == null || string.IsNullOrEmpty(topic)) return null;
            for (int i = 0; i < urls.Count; i++)
            {
                string url = urls[i];
                if (string.IsNullOrEmpty(url)) continue;
                if (string.Equals(TopicOf(url), topic, StringComparison.Ordinal)) return url;
            }
            return null;
        }

        private static string SetQuery(string url, string key, string value)
        {
            int q = url.IndexOf('?');
            if (q < 0) return url + "?" + key + "=" + value;
            string path = url.Substring(0, q);
            string query = url.Substring(q + 1);
            var parts = query.Length == 0 ? new string[0] : query.Split('&');
            bool found = false;
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                string k = eq >= 0 ? parts[i].Substring(0, eq) : parts[i];
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                {
                    parts[i] = key + "=" + value;
                    found = true;
                }
            }
            var joined = string.Join("&", parts);
            if (joined.Length == 0) return path + "?" + key + "=" + value;
            if (!found) return path + "?" + joined + "&" + key + "=" + value;
            return path + "?" + joined;
        }
    }
}
