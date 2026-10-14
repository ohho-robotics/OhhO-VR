using System.Text;

namespace OmniBot.VR.Latency
{
    /// <summary>
    /// Reads a capture timestamp embedded in a JPEG buffer.
    /// The marker is the ASCII text <c>OHHO_CAP:</c> plus a unix-millisecond integer.
    /// A JPEG COM segment (FF FE) is a convenient place to put it; the reader
    /// searches the bytes and does not require a valid JPEG.
    /// </summary>
    public static class JpegCaptureStamp
    {
        public const string Marker = "OHHO_CAP:";

        public static long? TryReadUnixMs(byte[] data)
        {
            if (data == null || data.Length < Marker.Length + 1) return null;
            // ASCII marker, so a byte scan is enough. Avoid decoding the whole frame.
            byte[] needle = Encoding.ASCII.GetBytes(Marker);
            int last = data.Length - needle.Length;
            for (int i = 0; i <= last; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (data[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (!match) continue;
                return ReadDigits(data, i + needle.Length);
            }
            return null;
        }

        private static long? ReadDigits(byte[] data, int start)
        {
            if (start >= data.Length || data[start] < '0' || data[start] > '9') return null;
            long value = 0;
            int digits = 0;
            for (int i = start; i < data.Length && digits < 16; i++)
            {
                byte c = data[i];
                if (c < '0' || c > '9') break;
                value = value * 10L + (c - '0');
                digits++;
            }
            if (digits == 0) return null;
            return value;
        }
    }
}
