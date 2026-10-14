using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmniBot.VR.Video
{
    /// <summary>Drops frames so a stream is displayed at most <see cref="MaxFps"/> times a second.</summary>
    public static class FramePacer
    {
        public const int MaxFps = 30;

        public static bool ShouldTake(double lastTakenSeconds, double nowSeconds)
        {
            if (lastTakenSeconds < 0) return true;
            if (nowSeconds < lastTakenSeconds) return true;
            return (nowSeconds - lastTakenSeconds) + 1e-6 >= 1.0 / MaxFps;
        }
    }

    /// <summary>Displayed-frame counter over a one-second window.</summary>
    public sealed class StreamFpsCounter
    {
        private readonly Queue<double> _times = new Queue<double>();

        public void Mark(double nowSeconds)
        {
            _times.Enqueue(nowSeconds);
            Trim(nowSeconds);
        }

        public float Fps(double nowSeconds)
        {
            Trim(nowSeconds);
            return _times.Count;
        }

        public void Clear() { _times.Clear(); }

        private void Trim(double nowSeconds)
        {
            while (_times.Count > 0 && nowSeconds - _times.Peek() > 1.0)
                _times.Dequeue();
        }
    }

    /// <summary>Text for the fps and frame-age overlay.</summary>
    public static class StreamOverlay
    {
        private static string _cached;
        private static int _cachedFps = int.MinValue;
        private static int _cachedAge = int.MinValue;

        public static float AgeMilliseconds(double receivedAtSeconds, double nowSeconds)
        {
            if (receivedAtSeconds <= 0) return 0f;
            double age = nowSeconds - receivedAtSeconds;
            if (age < 0) age = 0;
            return (float)(age * 1000.0);
        }

        public static string Format(float fps, float ageMs, bool hasFrame)
        {
            if (!hasFrame) return "No signal";
            int fpsInt = (int)Math.Round(fps);
            int ageInt = (int)Math.Round(ageMs);
            if (fpsInt < 0) fpsInt = 0;
            if (ageInt < 0) ageInt = 0;
            if (_cached != null && fpsInt == _cachedFps && ageInt == _cachedAge)
                return _cached;
            _cachedFps = fpsInt;
            _cachedAge = ageInt;
            _cached = fpsInt.ToString(CultureInfo.InvariantCulture) + " fps  ·  "
                + ageInt.ToString(CultureInfo.InvariantCulture) + " ms";
            return _cached;
        }
    }

    /// <summary>
    /// Decides when a TMP label may be rewritten. Same text never republishes.
    /// A different string waits out <see cref="IntervalSeconds"/> so a 30–90 Hz
    /// caller does not rebuild the mesh every frame.
    /// </summary>
    public static class OverlayTextGate
    {
        public const double IntervalSeconds = 0.2;

        public static bool ShouldPublish(string current, string incoming, double nowSeconds, double notBeforeSeconds)
        {
            if (incoming == null) return false;
            if (incoming == current) return false;
            if (notBeforeSeconds >= 0.0 && nowSeconds < notBeforeSeconds) return false;
            return true;
        }
    }
}
