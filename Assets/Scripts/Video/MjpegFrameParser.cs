using System;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// Pulls one JPEG out of a <c>multipart/x-mixed-replace</c> buffer.
    /// Content-Length wins when the body is complete. Otherwise the scan is
    /// SOI (FF D8) through EOI (FF D9), the same fallback the original feed used.
    /// The scan does not allocate: headers are matched as bytes.
    /// </summary>
    public static class MjpegFrameParser
    {
        public const int BoundarySearchSize = 512;

        private static readonly byte[] ContentLengthToken = { 99, 111, 110, 116, 101, 110, 116, 45, 108, 101, 110, 103, 116, 104, 58 };

        public static bool TryExtractJpeg(byte[] buf, int len, int offset, out int frameStart, out int frameLen)
        {
            frameStart = 0;
            frameLen = 0;
            if (buf == null || offset < 0 || len - offset < 4 || len > buf.Length) return false;

            int windowEnd = offset + Math.Min(BoundarySearchSize, len - offset);
            if (TryContentLength(buf, offset, windowEnd, len, out frameStart, out frameLen))
                return true;

            for (int i = offset; i < len - 1; i++)
            {
                if (buf[i] == 0xFF && buf[i + 1] == 0xD8)
                {
                    for (int j = i + 2; j < len - 1; j++)
                    {
                        if (buf[j] == 0xFF && buf[j + 1] == 0xD9)
                        {
                            frameStart = i;
                            frameLen = j + 2 - i;
                            return true;
                        }
                    }
                    break;
                }
            }
            return false;
        }

        private static bool TryContentLength(byte[] buf, int offset, int windowEnd, int len, out int frameStart, out int frameLen)
        {
            frameStart = 0;
            frameLen = 0;
            int idx = FindIgnoreCase(buf, offset, windowEnd, ContentLengthToken);
            if (idx < 0) return false;

            int tokenLen = ContentLengthToken.Length;
            int p = idx + tokenLen;
            while (p < windowEnd && (buf[p] == (byte)' ' || buf[p] == (byte)'\t')) p++;
            if (p >= windowEnd || buf[p] < (byte)'0' || buf[p] > (byte)'9') return false;

            int value = 0;
            while (p < windowEnd && buf[p] >= (byte)'0' && buf[p] <= (byte)'9')
            {
                int digit = buf[p] - (byte)'0';
                if (value > (int.MaxValue - digit) / 10) return false;
                value = value * 10 + digit;
                p++;
            }
            if (value <= 0) return false;

            while (p < windowEnd && (buf[p] == (byte)' ' || buf[p] == (byte)'\t')) p++;
            if (p >= windowEnd || (buf[p] != (byte)'\r' && buf[p] != (byte)'\n')) return false;

            int blank = FindCrlfCrlf(buf, idx, windowEnd);
            if (blank < 0) return false;
            int headerEnd = blank + 4;
            if (headerEnd + value > len) return false;
            frameStart = headerEnd;
            frameLen = value;
            return true;
        }

        private static int FindIgnoreCase(byte[] buf, int start, int end, byte[] lowerLiteral)
        {
            int last = end - lowerLiteral.Length;
            for (int i = start; i <= last; i++)
            {
                bool match = true;
                for (int k = 0; k < lowerLiteral.Length; k++)
                {
                    byte b = buf[i + k];
                    if (b >= (byte)'A' && b <= (byte)'Z') b = (byte)(b + 32);
                    if (b != lowerLiteral[k])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private static int FindCrlfCrlf(byte[] buf, int start, int end)
        {
            int last = end - 4;
            for (int i = start; i <= last; i++)
            {
                if (buf[i] == (byte)'\r' && buf[i + 1] == (byte)'\n' && buf[i + 2] == (byte)'\r' && buf[i + 3] == (byte)'\n')
                    return i;
            }
            return -1;
        }
    }

    /// <summary>Grows a byte buffer and then reuses it. Callers overwrite the bytes they need.</summary>
    public static class ByteScratch
    {
        public static byte[] Ensure(ref byte[] buffer, int minLength)
        {
            if (minLength <= 0) throw new ArgumentOutOfRangeException(nameof(minLength));
            if (buffer != null && buffer.Length >= minLength) return buffer;
            int size = 64 * 1024;
            if (buffer != null && buffer.Length > size) size = buffer.Length;
            while (size < minLength)
            {
                if (size > 16 * 1024 * 1024)
                {
                    size = minLength;
                    break;
                }
                size *= 2;
            }
            buffer = new byte[size];
            return buffer;
        }
    }

    /// <summary>
    /// Rolling buffer for chunked MJPEG. The callback must copy a frame it wants
    /// to keep; the buffer reuses that storage after the callback returns.
    /// </summary>
    public sealed class MjpegChunkBuffer
    {
        public const int MaxBytes = 4 * 1024 * 1024;

        private byte[] _buf = new byte[64 * 1024];
        private int _fill;

        public int Fill { get { return _fill; } }

        public void Clear() { _fill = 0; }

        public int Append(byte[] data, int length, Action<byte[], int, int> onFrame)
        {
            if (data == null || length <= 0) return 0;
            if (length > data.Length) length = data.Length;
            if (length > MaxBytes) return 0;
            if (_fill > MaxBytes - length)
            {
                int keep = Math.Min(_fill, 64 * 1024);
                if (keep > 0 && keep < _fill)
                    Buffer.BlockCopy(_buf, _fill - keep, _buf, 0, keep);
                _fill = keep;
                if (_fill > MaxBytes - length) _fill = 0;
            }
            Ensure(_fill + length);
            Buffer.BlockCopy(data, 0, _buf, _fill, length);
            _fill += length;

            int found = 0;
            int consumed = 0;
            while (found < 8 && MjpegFrameParser.TryExtractJpeg(_buf, _fill, consumed, out int start, out int frameLen))
            {
                if (frameLen <= 0) break;
                if (onFrame != null) onFrame(_buf, start, frameLen);
                consumed = start + frameLen;
                found++;
            }
            if (consumed > 0)
            {
                int remain = _fill - consumed;
                if (remain > 0) Buffer.BlockCopy(_buf, consumed, _buf, 0, remain);
                _fill = remain < 0 ? 0 : remain;
            }
            return found;
        }

        private void Ensure(int needed)
        {
            if (needed <= _buf.Length) return;
            int n = _buf.Length;
            while (n < needed) n *= 2;
            if (n > MaxBytes) n = MaxBytes;
            if (n < needed) n = needed;
            var grown = new byte[n];
            if (_fill > 0) Buffer.BlockCopy(_buf, 0, grown, 0, _fill);
            _buf = grown;
        }
    }
}
