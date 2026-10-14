using System;
using System.Text;

namespace OmniBot.VR.Telemetry
{
    public enum LimitZone
    {
        Unknown = 0,
        Ok = 1,
        Amber = 2,
        Red = 3
    }

    /// <summary>
    /// Number and bar formatting that appends into a caller-owned
    /// <see cref="StringBuilder"/>. No intermediate strings.
    /// </summary>
    public static class HudText
    {
        static readonly char[] Digits = new char[24];
        public const int BarWidth = 10;
        const double RadToDeg = 180.0 / Math.PI;

        public static double ToDeg(double radians)
        {
            return radians * RadToDeg;
        }

        public static double PlanarSpeed(double vx, double vy)
        {
            return Math.Sqrt(vx * vx + vy * vy);
        }

        public static LimitZone Zone(double pos, double min, double max)
        {
            double span = max - min;
            if (span <= 1e-9 || double.IsNaN(pos) || double.IsInfinity(pos)) return LimitZone.Unknown;
            double t = (pos - min) / span;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            double edge = t < 0.5 ? t : 1.0 - t;
            if (edge <= FieldAges.RedEdge) return LimitZone.Red;
            if (edge <= FieldAges.AmberEdge) return LimitZone.Amber;
            return LimitZone.Ok;
        }

        public static int GhostIndex(double value, double min, double max)
        {
            double span = max - min;
            if (span <= 1e-9) return 0;
            double t = (value - min) / span;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            int index = (int)(t * (BarWidth - 1) + 0.5);
            if (index < 0) return 0;
            if (index >= BarWidth) return BarWidth - 1;
            return index;
        }

        public static bool TryRpy(double x, double y, double z, double w, out double roll, out double pitch, out double yaw)
        {
            roll = pitch = yaw = 0;
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z) || double.IsNaN(w)) return false;
            double norm = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (norm < 1e-9) return false;
            x /= norm;
            y /= norm;
            z /= norm;
            w /= norm;
            double sinr = 2.0 * (w * x + y * z);
            double cosr = 1.0 - 2.0 * (x * x + y * y);
            roll = Math.Atan2(sinr, cosr);
            double sinp = 2.0 * (w * y - z * x);
            if (sinp >= 1.0) pitch = Math.PI / 2.0;
            else if (sinp <= -1.0) pitch = -Math.PI / 2.0;
            else pitch = Math.Asin(sinp);
            double siny = 2.0 * (w * z + x * y);
            double cosy = 1.0 - 2.0 * (y * y + z * z);
            yaw = Math.Atan2(siny, cosy);
            return true;
        }

        public static void AppendFixed(StringBuilder sb, double value, int decimals)
        {
            if (sb == null) return;
            if (double.IsNaN(value)) { sb.Append("nan"); return; }
            if (double.IsPositiveInfinity(value)) { sb.Append("inf"); return; }
            if (double.IsNegativeInfinity(value)) { sb.Append("-inf"); return; }
            if (decimals < 0) decimals = 0;
            if (decimals > 4) decimals = 4;
            double scaleD = decimals == 0 ? 1d : decimals == 1 ? 10d : decimals == 2 ? 100d : decimals == 3 ? 1000d : 10000d;
            bool neg = value < 0;
            double abs = neg ? -value : value;
            long scaled = (long)(abs * scaleD + 0.5);
            if (scaled < 0) scaled = 0;
            if (scaled == 0) neg = false;
            if (neg) sb.Append('-');
            if (decimals == 0)
            {
                AppendLong(sb, scaled);
                return;
            }
            long scale = (long)scaleD;
            AppendLong(sb, scaled / scale);
            sb.Append('.');
            long frac = scaled % scale;
            long pad = scale / 10;
            while (pad > 1 && frac < pad)
            {
                sb.Append('0');
                pad /= 10;
            }
            AppendLong(sb, frac);
        }

        public static void AppendLong(StringBuilder sb, long value)
        {
            if (sb == null) return;
            if (value < 0) value = 0;
            if (value == 0) { sb.Append('0'); return; }
            int n = 0;
            while (value > 0 && n < Digits.Length)
            {
                Digits[n++] = (char)('0' + (value % 10));
                value /= 10;
            }
            for (int i = n - 1; i >= 0; i--)
                sb.Append(Digits[i]);
        }

        public static void AppendText(StringBuilder sb, string value)
        {
            if (sb == null) return;
            if (string.IsNullOrEmpty(value)) sb.Append("n/a");
            else sb.Append(value);
        }

        public static void AppendBar(StringBuilder sb, double fraction, bool hasGhost, int ghost)
        {
            if (sb == null) return;
            if (fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;
            if (double.IsNaN(fraction)) fraction = 0;
            int fill = (int)(fraction * BarWidth + 0.5);
            if (fill > BarWidth) fill = BarWidth;
            if (ghost < 0) ghost = 0;
            if (ghost >= BarWidth) ghost = BarWidth - 1;
            sb.Append('[');
            for (int i = 0; i < BarWidth; i++)
            {
                if (hasGhost && i == ghost) sb.Append('|');
                else if (i < fill) sb.Append('#');
                else sb.Append('-');
            }
            sb.Append(']');
        }

        public static void OpenColor(StringBuilder sb, bool stale, LimitZone zone)
        {
            if (sb == null) return;
            if (stale) sb.Append("<color=#8A93A0>");
            else if (zone == LimitZone.Red) sb.Append("<color=#E23B3B>");
            else if (zone == LimitZone.Amber) sb.Append("<color=#E0A020>");
        }

        public static void CloseColor(StringBuilder sb, bool opened)
        {
            if (opened && sb != null) sb.Append("</color>");
        }

        public static bool ColorOpened(bool stale, LimitZone zone)
        {
            return stale || zone == LimitZone.Red || zone == LimitZone.Amber;
        }
    }
}
