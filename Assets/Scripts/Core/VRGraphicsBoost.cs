using UnityEngine;

namespace OmniBot.VR.Core
{
    /// <summary>
    /// One-shot visual quality bootstrap for the Quest build. The app renders
    /// through a single CenterEye camera in Built-in RP; without an explicit
    /// resolution scale OpenXR runs at the headset's default (1.0x) eye texture,
    /// which is what made the app "look like an old 3D game".
    ///
    /// What this does, once, at startup (and re-applies on quality level change):
    ///   - XRSettings.eyeTextureResolutionScale (scene uses 1.5x)
    ///   - MSAA 4x on the eye texture
    ///   - Fixed foveation at <see cref="QuestGraphicsPlan.Foveation"/> (dynamic off)
    ///   - CPU and GPU hints at SustainedHigh (not Boost — see the 30-min soak)
    ///   - 90 Hz display refresh request (72 Hz is the floor, not the request)
    ///   - Full-res textures (mipmap limit 0) + anisotropic filtering
    ///
    /// Development builds also gain a <see cref="QuestPerfSampler"/>. Release
    /// builds do not. Attach once to the app bootstrap GameObject.
    /// </summary>
    public class VRGraphicsBoost : MonoBehaviour
    {
        [Header("Resolution")]
        [Tooltip("Multiplier on the OpenXR eye texture size. 1.0 = stock, 2.0 = ultra-sharp on Quest 3.")]
        [SerializeField, Range(0.75f, 2.5f)] private float eyeTextureResolutionScale = 2.0f;

        [Tooltip("Extra supersampling inside the viewport. Keep at 1.0 — eyeTextureResolutionScale is the main lever.")]
        [SerializeField, Range(0.75f, 2f)] private float renderViewportScale = 1.0f;

        [Header("Anti-aliasing")]
        [Tooltip("MSAA samples on the eye texture (2/4/8). 4 is the Quest sweet spot.")]
        [SerializeField] private int msaaSamples = 4;

        [Header("Foveation")]
        [Tooltip("Legacy switch. Used only when Apply Fixed Foveation is off.")]
        [SerializeField] private bool ffrEnabled;

        [Tooltip("When on, Fixed Foveation is the level below and dynamic foveation stays off.")]
        [SerializeField] private bool applyFixedFoveation = true;

        [SerializeField] private OVRManager.FoveatedRenderingLevel fixedFoveation = QuestGraphicsPlan.Foveation;

        [Header("CPU / GPU")]
        [Tooltip("SustainedHigh is the soak default. Boost holds higher clocks and is more likely to thermal-throttle.")]
        [SerializeField] private bool applyPerformanceLevels = true;

        [SerializeField] private OVRManager.ProcessorPerformanceLevel cpuLevel = QuestGraphicsPlan.CpuLevel;

        [SerializeField] private OVRManager.ProcessorPerformanceLevel gpuLevel = QuestGraphicsPlan.GpuLevel;

        [Header("Refresh rate")]
        [Tooltip("Requested display refresh in Hz (72/90/120). 0 = leave at system default.")]
        [SerializeField] private float displayRefreshHz = 90f;

        [Header("Textures")]
        [SerializeField] private bool fullResolutionTextures = true;

        private void Awake()
        {
            Apply();
            EnsureDevelopmentSampler();
        }

        private void OnEnable()
        {
            Apply();
        }

        /// <summary>Apply all quality settings. Safe to call repeatedly.</summary>
        public void Apply()
        {
            // ── Eye texture resolution (the big one) ─────────────────────────
            UnityEngine.XR.XRSettings.eyeTextureResolutionScale = eyeTextureResolutionScale;
            UnityEngine.XR.XRSettings.renderViewportScale = renderViewportScale;

            // ── MSAA ─────────────────────────────────────────────────────────
            QualitySettings.antiAliasing = msaaSamples;

            // ── Fixed foveation and CPU/GPU hints ───────────────────────────
            OVRManager.FoveatedRenderingLevel foveation = QuestGraphicsPlan.ResolveFoveation(
                applyFixedFoveation, fixedFoveation, ffrEnabled);
            OVRManager.foveatedRenderingLevel = foveation;
            OVRManager.useDynamicFoveatedRendering = false;
            if (applyPerformanceLevels)
            {
                OVRManager.suggestedCpuPerfLevel = cpuLevel;
                OVRManager.suggestedGpuPerfLevel = gpuLevel;
            }

            // ── Refresh rate ─────────────────────────────────────────────────
            if (displayRefreshHz > 0f && OVRManager.display != null)
            {
                float[] supported = OVRManager.display.displayFrequenciesAvailable;
                if (supported != null && supported.Length > 0)
                {
                    float best = supported[0];
                    foreach (float f in supported)
                        if (Mathf.Abs(f - displayRefreshHz) < Mathf.Abs(best - displayRefreshHz))
                            best = f;
                    OVRManager.display.displayFrequency = best;
                    Debug.Log($"[VRGraphicsBoost] Display refresh set to {best} Hz.");
                }
            }

            // ── Texture fidelity ─────────────────────────────────────────────
            if (fullResolutionTextures)
            {
                QualitySettings.globalTextureMipmapLimit = 0;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
            }

            Debug.Log($"[VRGraphicsBoost] Applied — eyeTextureResolutionScale={eyeTextureResolutionScale}, " +
                      $"MSAA={msaaSamples}x, foveation={foveation}, dynamic=off, " +
                      $"cpu={(applyPerformanceLevels ? cpuLevel.ToString() : "unchanged")}, " +
                      $"gpu={(applyPerformanceLevels ? gpuLevel.ToString() : "unchanged")}.");
        }

        private void EnsureDevelopmentSampler()
        {
            if (!QuestPerfBudget.ShouldSample(Debug.isDebugBuild, true)) return;
            if (GetComponent<QuestPerfSampler>() != null) return;
            gameObject.AddComponent<QuestPerfSampler>();
        }
    }
}
