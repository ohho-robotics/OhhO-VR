using System;
using System.Collections.Generic;
using OmniBot.VR.Control.Session;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// One camera the sim screen can show. Topics match OmniBot
    /// <c>ohh-200-sim-cameras</c>: chase, front, wrist, and BEV.
    /// </summary>
    public readonly struct SimCameraView
    {
        public readonly string Id;
        public readonly string Label;
        public readonly string Topic;

        public SimCameraView(string id, string label, string topic)
        {
            Id = id;
            Label = label;
            Topic = topic;
        }
    }

    /// <summary>
    /// Gazebo cameras the Quest sim screen switches between, plus the JPEG
    /// size asked of <c>web_video_server</c>. Main frames are the Quest capture
    /// size (960 wide). The wrist picture-in-picture is smaller.
    /// </summary>
    public static class SimScreenCameras
    {
        public const string ChaseTopic = "/sim/chase_cam/image_raw";
        public const string FrontTopic = "/camera/front/image_raw";
        public const string WristTopic = "/camera/wrist/image_raw";
        public const string BevTopic = "/camera/base/bev/image_raw";

        public const int MainWidth = 960;
        public const int MainQuality = 80;
        public const int PipWidth = 320;
        public const int PipQuality = 60;

        public static SimCameraView ViewFor(string topic)
        {
            return new SimCameraView(IdFor(topic), LabelFor(topic), topic);
        }

        public static string LabelFor(string topic)
        {
            if (topic == ChaseTopic) return "Chase";
            if (topic == FrontTopic) return "Front";
            if (topic == WristTopic) return "Wrist";
            if (topic == BevTopic) return "BEV";
            if (string.IsNullOrEmpty(topic)) return "Camera";
            string trimmed = topic;
            const string suffix = "/image_raw";
            if (trimmed.EndsWith(suffix, StringComparison.Ordinal))
                trimmed = trimmed.Substring(0, trimmed.Length - suffix.Length);
            int slash = trimmed.LastIndexOf('/');
            string name = slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
            if (name.Length == 0) return "Camera";
            if (name.Length == 1) return name.ToUpperInvariant();
            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        public static string IdFor(string topic)
        {
            if (topic == ChaseTopic) return "chase";
            if (topic == FrontTopic) return "front";
            if (topic == WristTopic) return "wrist";
            if (topic == BevTopic) return "bev";
            return LabelFor(topic).ToLowerInvariant();
        }

        /// <summary>
        /// Simulate puts the chase camera first, then the profile's cameras.
        /// An empty sim profile still gets front, wrist, and BEV. A real session
        /// keeps the profile order and does not invent the chase topic.
        /// </summary>
        public static List<SimCameraView> ViewsFor(SessionKind? kind, IList<string> profileTopics)
        {
            bool sim = kind.HasValue && kind.Value == SessionKind.Sim;
            var result = new List<SimCameraView>();
            if (sim) result.Add(ViewFor(ChaseTopic));
            int profileCount = 0;
            if (profileTopics != null)
            {
                for (int i = 0; i < profileTopics.Count; i++)
                {
                    string topic = profileTopics[i];
                    if (string.IsNullOrEmpty(topic)) continue;
                    if (Contains(result, topic)) continue;
                    result.Add(ViewFor(topic));
                    profileCount++;
                }
            }
            if (sim && profileCount == 0)
            {
                AddIfMissing(result, FrontTopic);
                AddIfMissing(result, WristTopic);
                AddIfMissing(result, BevTopic);
            }
            return result;
        }

        private static void AddIfMissing(List<SimCameraView> views, string topic)
        {
            if (!Contains(views, topic)) views.Add(ViewFor(topic));
        }

        private static bool Contains(List<SimCameraView> views, string topic)
        {
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].Topic == topic) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// The cameras, default selection, wrist picture-in-picture, and MJPEG URLs
    /// for one session. Built without starting a stream so edit-mode tests can
    /// cover it.
    /// </summary>
    public sealed class SimScreenPlan
    {
        public bool IsSim;
        public string Host;
        public List<SimCameraView> Views = new List<SimCameraView>();
        public List<string> VideoUrls = new List<string>();

        public int DefaultIndex
        {
            get
            {
                if (IsSim)
                {
                    for (int i = 0; i < Views.Count; i++)
                    {
                        if (Views[i].Topic == SimScreenCameras.ChaseTopic) return i;
                    }
                }
                return 0;
            }
        }

        public static SimScreenPlan Build(SessionKind? kind, IList<string> profileTopics, string host, IList<string> videoUrls)
        {
            var plan = new SimScreenPlan();
            plan.IsSim = kind.HasValue && kind.Value == SessionKind.Sim;
            plan.Host = host ?? "";
            plan.Views = SimScreenCameras.ViewsFor(kind, profileTopics);
            if (videoUrls != null)
            {
                for (int i = 0; i < videoUrls.Count; i++)
                {
                    if (!string.IsNullOrEmpty(videoUrls[i])) plan.VideoUrls.Add(videoUrls[i]);
                }
            }
            if (plan.Host.Length == 0 && plan.VideoUrls.Count > 0)
                plan.Host = HostOf(plan.VideoUrls[0]);
            return plan;
        }

        /// <summary>Wrist inset while some other view is the main image. Sim only.</summary>
        public int PipIndexFor(int mainIndex)
        {
            if (!IsSim) return -1;
            for (int i = 0; i < Views.Count; i++)
            {
                if (Views[i].Topic != SimScreenCameras.WristTopic) continue;
                if (i == mainIndex) return -1;
                return i;
            }
            return -1;
        }

        public string UrlFor(int index, int quality, int width)
        {
            if (index < 0 || index >= Views.Count) return null;
            string topic = Views[index].Topic;
            string match = MjpegUrl.FindByTopic(VideoUrls, topic);
            if (!string.IsNullOrEmpty(match)) return MjpegUrl.WithParams(match, quality, width);
            if (string.IsNullOrEmpty(Host)) return null;
            return MjpegUrl.Build(Host, MjpegUrl.Port, topic, quality, width);
        }

        public void RetargetHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return;
            Host = host;
            for (int i = 0; i < VideoUrls.Count; i++)
                VideoUrls[i] = MjpegUrl.WithHost(VideoUrls[i], host);
        }

        private static string HostOf(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            int scheme = url.IndexOf("://", StringComparison.Ordinal);
            if (scheme < 0) return "";
            int start = scheme + 3;
            int slash = url.IndexOf('/', start);
            if (slash < 0) slash = url.Length;
            int colon = url.IndexOf(':', start);
            int hostEnd = (colon > start && colon < slash) ? colon : slash;
            if (hostEnd <= start) return "";
            return url.Substring(start, hostEnd - start);
        }
    }
}
