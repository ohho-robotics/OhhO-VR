using System;
using System.Collections;
using System.IO;
using OmniBot.VR.Control;
using OmniBot.VR.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace OmniBot.VR.Twin
{
    /// <summary>
    /// Scene adapter for <see cref="TwinAnimator"/>. Display only: it subscribes
    /// through <see cref="IRobotLink"/> and never publishes a command.
    ///
    /// Drop <c>Assets/Twin/Prefabs/OmniBotTwin.prefab</c> on the desk anchor, or:
    /// <c>gameObject.AddComponent&lt;TwinView&gt;().Configure(link, TwinProfiles.OmniBot());</c>
    /// </summary>
    public sealed class TwinView : MonoBehaviour
    {
        [SerializeField] private TextAsset urdfDocument;
        [SerializeField] private TwinScaleMode scaleMode = TwinScaleMode.Tabletop;
        [SerializeField] private float staleTimeoutSeconds = 1f;
        [Tooltip("Optional IRobotLink behaviour. Defaults to RosBridgeLink.")]
        [SerializeField] private MonoBehaviour linkProvider;

        readonly TwinLinkSubscriber _subscriber = new TwinLinkSubscriber();
        TwinProfile _profile;
        IRobotLink _link;
        TwinAnimator _animator;
        TwinBuiltHierarchy _measured;
        TwinBuiltHierarchy _ghost;
        bool _started;
        bool _listening;
        bool _loggedListen;
        bool _tintStale;
        int _loadGeneration;

        public TwinAnimator Animator => _animator;
        public TwinScaleMode ScaleMode => scaleMode;

        public void Configure(IRobotLink link, TwinProfile profile)
        {
            _link = link;
            _profile = profile ?? TwinProfiles.OmniBot();
            scaleMode = _profile.Scale;
            if (_started)
                Restart();
        }

        public void SetScale(TwinScaleMode mode)
        {
            scaleMode = mode;
            if (_profile != null)
                _profile.Scale = mode;
            if (_animator != null)
                _animator.SetScale(mode);
            ApplyRootScale();
        }

        void Start()
        {
            _started = true;
            if (_profile == null)
            {
                _profile = TwinProfiles.OmniBot();
                _profile.Scale = scaleMode;
                if (staleTimeoutSeconds > 0f)
                    _profile.StaleTimeoutSeconds = staleTimeoutSeconds;
            }
            Restart();
        }

        void Update()
        {
            if (_animator == null)
                return;
            TryListen();
            _animator.Tick(Time.timeAsDouble);
            ApplyPoses(_measured, false);
            if (_ghost != null && _ghost.Root != null)
            {
                bool show = _animator.GhostVisible;
                if (_ghost.Root.gameObject.activeSelf != show)
                    _ghost.Root.gameObject.SetActive(show);
                if (show)
                    ApplyPoses(_ghost, true);
            }
            ApplyRootScale();
            UpdateStaleTint();
        }

        void OnDestroy()
        {
            _loadGeneration++;
            _subscriber.Detach();
        }

        void Restart()
        {
            _listening = false;
            _subscriber.Detach();
            StopAllCoroutines();
            StartCoroutine(LoadAndBuild());
        }

        IEnumerator LoadAndBuild()
        {
            int generation = ++_loadGeneration;
            string text = null;
            string path = _profile != null ? _profile.UrdfPath : null;
            if (!string.IsNullOrEmpty(path) &&
                (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                using (UnityWebRequest request = UnityWebRequest.Get(path))
                {
                    yield return request.SendWebRequest();
                    if (generation != _loadGeneration)
                        yield break;
                    if (request.result == UnityWebRequest.Result.Success)
                        text = request.downloadHandler.text;
                    else
                        Debug.LogError("[Twin] URDF download failed: " + request.error);
                }
            }
            else if (!string.IsNullOrEmpty(path))
            {
                text = TryRead(path);
            }

            if (generation != _loadGeneration)
                yield break;
            if (string.IsNullOrEmpty(text) && urdfDocument != null)
                text = urdfDocument.text;
            if (string.IsNullOrEmpty(text))
            {
                Debug.LogError("[Twin] No URDF text. Assign the document or set TwinProfile.UrdfPath.");
                yield break;
            }

            UrdfDocument document;
            try
            {
                document = UrdfParser.Parse(text);
            }
            catch (Exception ex)
            {
                Debug.LogError("[Twin] URDF parse failed: " + ex.Message);
                yield break;
            }

            ClearBuilt();
            _animator = new TwinAnimator(document, _profile);
            _animator.SetScale(scaleMode);
            string urdfDirectory = Path.Combine(Application.dataPath, "Twin", "URDF");
            _measured = TwinHierarchyBuilder.Build(document, transform, false, urdfDirectory);
            _ghost = TwinHierarchyBuilder.Build(document, transform, true, urdfDirectory);
            if (_ghost.Root != null)
                _ghost.Root.gameObject.SetActive(false);
            _tintStale = false;
            ApplyRootScale();
            TryListen();
        }

        void TryListen()
        {
            if (_listening || _animator == null || _profile == null)
                return;
            IRobotLink link = ResolveLink();
            if (link == null)
                return;
            if (link == RosBridgeLink.Instance && ROSBridgeClient.Instance == null)
                return;
            try
            {
                _subscriber.Attach(link, _profile, _animator, () => Time.timeAsDouble);
                _listening = true;
            }
            catch (Exception ex)
            {
                if (!_loggedListen)
                {
                    _loggedListen = true;
                    Debug.LogWarning("[Twin] Not subscribed yet: " + ex.Message);
                }
            }
        }

        IRobotLink ResolveLink()
        {
            if (_link != null)
                return _link;
            if (linkProvider is IRobotLink provider)
                return provider;
            return RosBridgeLink.Instance;
        }

        void ApplyPoses(TwinBuiltHierarchy built, bool commanded)
        {
            if (built == null)
                return;
            for (int i = 0; i < built.Nodes.Count; i++)
            {
                TwinLinkNode node = built.Nodes[i];
                if (node == null)
                    continue;
                TwinPose pose;
                if (_animator.TryGetLinkLocalPose(node.LinkName, commanded, out pose))
                    TwinPoseApplicator.Apply(node.transform, pose);
            }
        }

        void ApplyRootScale()
        {
            float factor = _animator != null ? _animator.ScaleFactor : TwinScale.Factor(scaleMode);
            Vector3 scale = new Vector3(factor, factor, factor);
            if (_measured != null && _measured.Root != null)
                _measured.Root.localScale = scale;
            if (_ghost != null && _ghost.Root != null)
                _ghost.Root.localScale = scale;
        }

        void UpdateStaleTint()
        {
            bool stale = (_animator.HasBaseSample && _animator.IsBaseStale)
                || (_animator.HasJointSample && _animator.AreJointsStale);
            if (stale == _tintStale || _measured == null || _measured.Root == null)
                return;
            _tintStale = stale;
            Renderer[] renderers = _measured.Root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!stale)
                {
                    renderers[i].SetPropertyBlock(null);
                    continue;
                }
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", new Color(0.45f, 0.45f, 0.45f, 1f));
                renderers[i].SetPropertyBlock(block);
            }
        }

        void ClearBuilt()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name == "Measured" || child.name == "Ghost")
                    Destroy(child.gameObject);
            }
            _measured = null;
            _ghost = null;
        }

        static string TryRead(string path)
        {
            try
            {
                if (File.Exists(path))
                    return File.ReadAllText(path);
                string fromProject = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
                if (File.Exists(fromProject))
                    return File.ReadAllText(fromProject);
                string relative = path;
                if (relative.StartsWith("Assets/", StringComparison.Ordinal) || relative.StartsWith("Assets\\", StringComparison.Ordinal))
                    relative = relative.Substring("Assets/".Length);
                string fromAssets = Path.Combine(Application.dataPath, relative);
                if (File.Exists(fromAssets))
                    return File.ReadAllText(fromAssets);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Twin] URDF file read failed: " + ex.Message);
            }
            return null;
        }
    }
}
