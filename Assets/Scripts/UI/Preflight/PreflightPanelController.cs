using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using OmniBot.VR.Control.Preflight;
using OmniBot.VR.Control.Session;
using OmniBot.VR.Core;
using OmniBot.VR.UI.Session;

namespace OmniBot.VR.UI.Preflight
{
    /// <summary>
    /// Checklist shown after rosbridge connects. Runs <see cref="ConnectionPreflight"/>
    /// and leaves the report on screen. Copy puts the diagnostics text on the
    /// clipboard. Teleop gating stays in the app; this view does not publish motion.
    /// </summary>
    public class PreflightPanelController : MonoBehaviour
    {
        public event Action<PreflightReport> Finished;
        public event Action Cancelled;

        private RobotEndpoint _endpoint;
        private int _generation;
        private bool _listening;
        private bool _running;
        private PreflightReport _report;
        private bool _chrome;
        private TMP_FontAsset _font;
        private RectTransform _content;
        private TMP_Text _title;
        private TMP_Text _status;

        private static readonly Color Surface = new Color(0.086f, 0.118f, 0.20f, 1f);
        private static readonly Color TextMain = new Color(0.95f, 0.97f, 1f, 1f);
        private static readonly Color TextMut = new Color(0.70f, 0.76f, 0.84f, 1f);
        private static readonly Color PassGreen = new Color(0.18f, 0.85f, 0.45f, 1f);
        private static readonly Color FailRed = new Color(0.95f, 0.32f, 0.28f, 1f);
        private static readonly Color WarnAmber = new Color(0.95f, 0.75f, 0.25f, 1f);

        public void Begin(RobotEndpoint endpoint)
        {
            _endpoint = endpoint;
            _generation++;
            _report = null;
            _running = false;
            StopAllCoroutines();
            gameObject.SetActive(true);
            ApplyFullScreen();
            EnsureChrome();
            SetStatus(endpoint != null && endpoint.Kind == SessionKind.Real
                ? "Checking the real robot before teleop."
                : "Checking the simulation before teleop.");
            ShowPending("Waiting for the rosbridge handshake…");
            Listen();
            if (ConnectionManager.Instance != null && ConnectionManager.Instance.IsConnected)
                StartRun();
        }

        public void Hide()
        {
            _generation++;
            _running = false;
            StopAllCoroutines();
            gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            Unlisten();
            ReleaseSubscriptions();
        }

        private void Listen()
        {
            var conn = ConnectionManager.Instance;
            if (conn == null || _listening) return;
            conn.OnConnectionStateChanged += OnConnection;
            _listening = true;
        }

        private void Unlisten()
        {
            if (!_listening) return;
            var conn = ConnectionManager.Instance;
            if (conn != null) conn.OnConnectionStateChanged -= OnConnection;
            _listening = false;
        }

        private void OnConnection(bool connected)
        {
            if (!isActiveAndEnabled) return;
            if (!connected)
            {
                SetStatus("Rosbridge disconnected. Waiting for the handshake…");
                return;
            }
            StartRun();
        }

        private void StartRun()
        {
            StopAllCoroutines();
            _running = false;
            StartCoroutine(CollectAndRun(_generation));
        }

        private IEnumerator CollectAndRun(int generation)
        {
            if (_running) yield break;
            _running = true;
            SetStatus("Running the connection preflight…");
            ReleaseSubscriptions();

            var link = new CollectedLink();
            var client = ROSBridgeClient.Instance;
            link.IsConnected = client != null && client.IsConnected;
            if (!link.IsConnected)
            {
                Finish(generation, link);
                yield break;
            }

            string descriptor = null;
            string safety = null;
            client.Subscribe(PreflightTopics.Descriptor, PreflightTopics.StringType, json =>
            {
                if (descriptor == null) descriptor = json;
            });
            client.Subscribe(PreflightTopics.SafetyState, PreflightTopics.StringType, json =>
            {
                if (safety == null) safety = json;
            });

            bool topicsDone = false;
            bool topicsOk = false;
            string topicsBody = null;
            client.CallService(PreflightTopics.TopicsService, "{}", (ok, body) =>
            {
                topicsDone = true;
                topicsOk = ok;
                topicsBody = body;
            });

            bool rttDone = false;
            bool rttOk = false;
            string rttBody = null;
            float rttStart = Time.realtimeSinceStartup;
            double rttMs = -1;
            client.CallService(PreflightTopics.TimeService, "{}", (ok, body) =>
            {
                rttDone = true;
                rttOk = ok;
                rttBody = body;
                rttMs = (Time.realtimeSinceStartup - rttStart) * 1000.0;
            });

            float waited = 0f;
            while (generation == _generation && waited < 2.5f &&
                   (descriptor == null || safety == null || !topicsDone || !rttDone))
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (generation != _generation) yield break;
            ReleaseSubscriptions();

            link.IsConnected = client.IsConnected;
            link.DescriptorMessage = descriptor;
            link.SafetyMessage = safety;
            link.TopicsOk = topicsOk;
            link.TopicsValuesJson = topicsBody;
            link.TopicsError = topicsOk ? null : (string.IsNullOrEmpty(topicsBody) ? "rosapi/topics did not answer." : topicsBody);
            link.RttOk = rttOk && rttMs >= 0;
            link.RttMs = rttMs;
            link.RttDetail = rttOk ? null : (string.IsNullOrEmpty(rttBody) ? "rosapi/get_time did not answer." : rttBody);

            string url = FirstVideoUrl();
            if (string.IsNullOrEmpty(url))
            {
                link.VideoState = VideoProbeState.NotProbed;
                link.VideoDetail = "no URL";
            }
            else
            {
                bool reachable = false;
                string detail = url;
                yield return ProbeVideo(url, (ok, text) =>
                {
                    reachable = ok;
                    detail = text;
                });
                if (generation != _generation) yield break;
                link.VideoState = reachable ? VideoProbeState.Reachable : VideoProbeState.Unreachable;
                link.VideoDetail = detail;
            }

            Finish(generation, link);
        }

        private void Finish(int generation, CollectedLink link)
        {
            _running = false;
            if (generation != _generation) return;

            var request = new PreflightRequest
            {
                Kind = _endpoint != null ? _endpoint.Kind : SessionKind.Real,
                RosbridgeUrl = _endpoint != null ? _endpoint.RosbridgeUrl : null,
            };

            PreflightReport report;
            try { report = ConnectionPreflight.Run(link, request); }
            catch (Exception ex)
            {
                report = ConnectionPreflight.Failed(request, ex.Message);
            }

            _report = report;
            if (!report.BlocksTeleop && !report.HasFailure && !report.HasWarning)
            {
                Finished?.Invoke(report);
                Hide();
                return;
            }

            if (report.BlocksTeleop)
                ApplyFullScreen();
            else
                ApplyCard();
            Render(report);
            Finished?.Invoke(report);
        }

        private string FirstVideoUrl()
        {
            var urls = _endpoint != null ? _endpoint.VideoUrls : null;
            if (urls == null) return null;
            for (int i = 0; i < urls.Count; i++)
            {
                if (!string.IsNullOrEmpty(urls[i])) return urls[i];
            }
            return null;
        }

        private static IEnumerator ProbeVideo(string url, Action<bool, string> done)
        {
            if (string.IsNullOrEmpty(url) ||
                (url.IndexOf("http://", StringComparison.OrdinalIgnoreCase) != 0 &&
                 url.IndexOf("https://", StringComparison.OrdinalIgnoreCase) != 0))
            {
                done(false, string.IsNullOrEmpty(url) ? "no URL" : url);
                yield break;
            }

            using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = 3;
                req.SendWebRequest();
                float elapsed = 0f;
                while (!req.isDone && elapsed < 2.5f)
                {
                    var data = req.downloadHandler != null ? req.downloadHandler.data : null;
                    if (data != null && data.Length > 0)
                    {
                        try { req.Abort(); }
                        catch (Exception) { }
                        done(true, url);
                        yield break;
                    }
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                bool httpOk = req.responseCode >= 200 && req.responseCode < 400;
                bool ok = httpOk && (req.result == UnityWebRequest.Result.Success || req.downloadedBytes > 0);
                done(ok, url);
            }
        }

        private static void ReleaseSubscriptions()
        {
            var client = ROSBridgeClient.Instance;
            if (client == null) return;
            client.Unsubscribe(PreflightTopics.Descriptor);
            client.Unsubscribe(PreflightTopics.SafetyState);
        }

        private void EnsureChrome()
        {
            if (_chrome) return;
            _chrome = true;

            var bg = gameObject.GetComponent<Image>();
            if (bg == null) bg = gameObject.AddComponent<Image>();
            bg.sprite = SessionUi.WhiteSprite;
            bg.color = new Color(0.039f, 0.055f, 0.102f, 0.96f);

            _title = MakeText("Title", transform, 32, TextMain, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(36f, -16f), new Vector2(-36f, -72f));
            _title.text = "Connection preflight";

            _status = MakeText("Status", transform, 22, TextMut, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            Place(_status.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(36f, -76f), new Vector2(-36f, -160f));

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(transform, false);
            _content = (RectTransform)contentGo.transform;
            _content.anchorMin = new Vector2(0f, 0f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.offsetMin = new Vector2(36f, 36f);
            _content.offsetMax = new Vector2(-36f, -172f);
            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private void ShowPending(string message)
        {
            EnsureChrome();
            ClearContent();
            AddBody(message, TextMain);
        }

        private void Render(PreflightReport report)
        {
            EnsureChrome();
            ClearContent();
            _title.text = "Connection preflight";
            SetStatus(report.Summary);
            if (_status != null)
                _status.color = report.BlocksTeleop ? FailRed : (report.HasFailure ? WarnAmber : PassGreen);

            for (int i = 0; i < report.Checks.Count; i++)
            {
                var check = report.Checks[i];
                string word = check.Status == PreflightStatus.Pass ? "PASS" :
                    check.Status == PreflightStatus.Warn ? "WARN" : "FAIL";
                string hex = check.Status == PreflightStatus.Pass ? "#2ED96B" :
                    check.Status == PreflightStatus.Warn ? "#F2BF40" : "#F25248";
                AddBody("<color=" + hex + ">" + word + "</color>  " + check.Title + " — " + check.Detail, TextMain);
            }

            AddButton("Copy diagnostics", Surface, TextMain, CopyDiagnostics);
            if (report.BlocksTeleop)
                AddButton("Back", SessionBannerStyle.RealOrange, Color.white, () => Cancelled?.Invoke());
            else if (report.HasFailure || report.HasWarning)
                AddButton("Dismiss", Surface, TextMain, Hide);
        }

        private void CopyDiagnostics()
        {
            string text = _report != null ? _report.Diagnostics : "";
            GUIUtility.systemCopyBuffer = text ?? "";
            if (_status != null) _status.text = "Diagnostics copied.";
        }

        private void SetStatus(string message)
        {
            if (_status != null) _status.text = message ?? "";
        }

        private void AddBody(string message, Color color)
        {
            var text = MakeText("Row", _content, 22, color, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            text.richText = true;
            text.text = message;
            var le = text.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 56f;
            le.preferredHeight = 72f;
        }

        private void AddButton(string label, Color background, Color foreground, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(_content, false);
            var image = go.AddComponent<Image>();
            image.sprite = SessionUi.WhiteSprite;
            image.color = background;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var text = MakeText("Text", go.transform, 24, foreground, FontStyles.Bold, TextAlignmentOptions.Center);
            text.text = label;
            Stretch(text.rectTransform);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 64f;
            le.preferredHeight = 64f;
            button.onClick.AddListener(onClick);
        }

        private void ClearContent()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);
        }

        private void ApplyFullScreen()
        {
            var rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var image = GetComponent<Image>();
            if (image != null) image.color = new Color(0.039f, 0.055f, 0.102f, 0.96f);
        }

        private void ApplyCard()
        {
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(24f, -640f);
            rt.offsetMax = new Vector2(-24f, -16f);
            var image = GetComponent<Image>();
            if (image != null) image.color = new Color(0.039f, 0.055f, 0.102f, 0.94f);
        }

        private TMP_Text MakeText(string name, Transform parent, float size, Color color, FontStyles style, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = align;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            if (Font != null) text.font = Font;
            return text;
        }

        private TMP_FontAsset Font
        {
            get
            {
                if (_font != null) return _font;
                var sample = transform.parent != null
                    ? transform.parent.GetComponentInChildren<TMP_Text>(true)
                    : null;
                _font = sample != null ? sample.font : TMP_Settings.defaultFontAsset;
                return _font;
            }
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        private sealed class CollectedLink : IPreflightLink
        {
            public bool IsConnected { get; set; }
            public string DescriptorMessage;
            public string SafetyMessage;
            public bool TopicsOk;
            public string TopicsValuesJson;
            public string TopicsError;
            public VideoProbeState VideoState = VideoProbeState.NotProbed;
            public string VideoDetail;
            public bool RttOk;
            public double RttMs;
            public string RttDetail;

            public string Message(string topic)
            {
                if (topic == PreflightTopics.Descriptor) return DescriptorMessage;
                if (topic == PreflightTopics.SafetyState) return SafetyMessage;
                return null;
            }

            public bool TryService(string service, out string valuesJson, out string error)
            {
                valuesJson = null;
                error = null;
                if (service != PreflightTopics.TopicsService)
                {
                    error = "Unexpected service " + service;
                    return false;
                }
                if (!TopicsOk)
                {
                    error = TopicsError;
                    return false;
                }
                valuesJson = TopicsValuesJson ?? "{\"topics\":[]}";
                return true;
            }

            public void ReadVideo(out VideoProbeState state, out string detail)
            {
                state = VideoState;
                detail = VideoDetail;
            }

            public bool TryRtt(out double rttMs, out string detail)
            {
                rttMs = RttMs;
                detail = RttDetail;
                return RttOk;
            }
        }
    }
}
