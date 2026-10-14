using System;
using System.Text;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// Pulls one JPEG out of a <c>multipart/x-mixed-replace</c> buffer.
    /// Content-Length wins when the body is complete. Otherwise the scan is
    /// SOI (FF D8) through EOI (FF D9), the same fallback the original feed used.
    /// </summary>
    public static class MjpegFrameParser
    {
        public const int BoundarySearchSize = 512;

        public static bool TryExtractJpeg(byte[] buf, int len, int offset, out int frameStart, out int frameLen)
        {
            frameStart = 0;
            frameLen = 0;
            if (buf == null || offset < 0 || len - offset < 4 || len > buf.Length) return false;

            int window = Math.Min(BoundarySearchSize, len - offset);
            string header = Encoding.ASCII.GetString(buf, offset, window);
            int clIdx = header.IndexOf("Content-Length:", StringComparison.OrdinalIgnoreCase);
            if (clIdx >= 0)
            {
                int eolIdx = header.IndexOf("\r\n\r\n", clIdx);
                if (eolIdx >= 0)
                {
                    int headerEnd = offset + eolIdx + 4;
                    int clEnd = header.IndexOf('\r', clIdx + 15);
                    if (clEnd < 0) clEnd = header.IndexOf('\n', clIdx + 15);
                    if (clEnd > 0)
                    {
                        string clStr = header.Substring(clIdx + 15, clEnd - (clIdx + 15)).Trim();
                        if (int.TryParse(clStr, out int contentLen) && contentLen > 0 && headerEnd + contentLen <= len)
                        {
                            frameStart = headerEnd;
                            frameLen = contentLen;
                            return true;
                        }
                    }
                }
            }

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
