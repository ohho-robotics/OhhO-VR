using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace OmniBot.VR.Comfort
{
    /// <summary>
    /// Applies <see cref="ComfortSettings.TextScale"/> to TMP labels on this object.
    /// Not placed in <c>Assets/scene1.unity</c>. HUD and perception panels should
    /// add it or subscribe to <see cref="ComfortSettingsStore.Changed"/> themselves.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ComfortTextScale : MonoBehaviour, IComfortSettingsConsumer
    {
        private readonly List<LabelSize> _authored = new List<LabelSize>();
        private bool _captured;

        private struct LabelSize
        {
            public TMP_Text Text;
            public float FontSize;
        }

        private void Awake()
        {
            Capture();
        }

        private void OnEnable()
        {
            ComfortSettingsStore.Changed += ApplyComfort;
            if (ComfortSettingsStore.HasSaved)
            {
                ApplyComfort(ComfortSettingsStore.Current);
            }
        }

        private void OnDisable()
        {
            ComfortSettingsStore.Changed -= ApplyComfort;
        }

        public void ApplyComfort(ComfortSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            Capture();
            var scale = settings.TextScale;
            for (var i = 0; i < _authored.Count; i++)
            {
                var label = _authored[i];
                if (label.Text == null)
                {
                    continue;
                }

                label.Text.fontSize = label.FontSize * scale;
            }
        }

        private void Capture()
        {
            if (_captured)
            {
                return;
            }

            _captured = true;
            var labels = GetComponentsInChildren<TMP_Text>(true);
            for (var i = 0; i < labels.Length; i++)
            {
                _authored.Add(new LabelSize { Text = labels[i], FontSize = labels[i].fontSize });
            }
        }
    }
}
