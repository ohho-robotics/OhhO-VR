using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace OmniBot.VR.Core
{
    /// <summary>
    /// rosbridge <c>compression: "png"</c> codec. Matches
    /// <c>rosbridge_library/internal/pngcompression.py</c>: the compact JSON
    /// text is packed into a square RGB image, padded with <c>'\n'</c>, and the
    /// PNG is standard-base64'd. Decoding reverses that and strips the padding.
    /// </summary>
    public static class RosbridgePng
    {
        static readonly object Gate = new object();
        static readonly uint[] CrcTable = BuildCrcTable();
        static byte[] _idat = new byte[256];
        static byte[] _raw = new byte[256];
        static byte[] _rgb = new byte[256];

        /// <summary>
        /// Decode a rosbridge png payload (standard base64) back to the JSON
        /// text that was compressed. Thread-safe.
        /// </summary>
        public static bool TryDecodeToJson(string base64, out string json)
        {
            json = null;
            if (string.IsNullOrEmpty(base64)) return false;
            byte[] png;
            try { png = Convert.FromBase64String(base64); }
            catch (FormatException) { return false; }
            lock (Gate)
            {
                if (!TryDecodePng(png, png.Length, out int rgbLen)) return false;
                json = Encoding.UTF8.GetString(_rgb, 0, rgbLen).Replace("\n", "");
                return json.Length > 0;
            }
        }

        /// <summary>
        /// Encode JSON the way rosbridge does, so tests can round-trip without
        /// a bridge. Width matches <c>floor(sqrt(len/3))</c>, with a floor of 1
        /// so a tiny payload does not divide by zero the way the Python encoder can.
        /// </summary>
        public static string EncodeToBase64(string json)
        {
            if (json == null) json = "";
            byte[] text = Encoding.UTF8.GetBytes(json);
            int length = text.Length;
            int width = (int)Math.Floor(Math.Sqrt(length / 3.0));
            if (width < 1) width = 1;
            int height = (int)Math.Ceiling((length / 3.0) / width);
            int need = width * height * 3;
            var padded = new byte[need];
            Buffer.BlockCopy(text, 0, padded, 0, length);
            for (int i = length; i < need; i++) padded[i] = (byte)'\n';

            int stride = width * 3;
            var scan = new byte[height * (stride + 1)];
            for (int y = 0; y < height; y++)
            {
                scan[y * (stride + 1)] = 0;
                Buffer.BlockCopy(padded, y * stride, scan, y * (stride + 1) + 1, stride);
            }

            byte[] zlib = ZlibWrap(scan);
            byte[] png = WritePng(width, height, zlib);
            return Convert.ToBase64String(png);
        }

        static bool TryDecodePng(byte[] png, int length, out int rgbLen)
        {
            rgbLen = 0;
            if (png == null || length < 8) return false;
            if (png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47) return false;

            int width = 0, height = 0, idatLen = 0;
            bool sawHeader = false;
            int i = 8;
            while (i + 8 <= length)
            {
                int chunkLen = ReadBE(png, i);
                if (chunkLen < 0 || i + 12 + chunkLen > length) return false;
                int type = i + 4;
                int data = i + 8;
                bool ihdr = png[type] == (byte)'I' && png[type + 1] == (byte)'H' &&
                            png[type + 2] == (byte)'D' && png[type + 3] == (byte)'R';
                bool idat = png[type] == (byte)'I' && png[type + 1] == (byte)'D' &&
                            png[type + 2] == (byte)'A' && png[type + 3] == (byte)'T';
                bool iend = png[type] == (byte)'I' && png[type + 1] == (byte)'E' &&
                            png[type + 2] == (byte)'N' && png[type + 3] == (byte)'D';
                if (ihdr)
                {
                    if (chunkLen < 13) return false;
                    width = ReadBE(png, data);
                    height = ReadBE(png, data + 4);
                    if (png[data + 8] != 8 || png[data + 9] != 2) return false; // 8-bit RGB
                    if (png[data + 12] != 0) return false; // no interlace
                    if (width <= 0 || height <= 0 || width > 8192 || height > 8192) return false;
                    sawHeader = true;
                }
                else if (idat)
                {
                    Ensure(ref _idat, idatLen + chunkLen);
                    Buffer.BlockCopy(png, data, _idat, idatLen, chunkLen);
                    idatLen += chunkLen;
                }
                else if (iend) break;
                i += 12 + chunkLen;
            }
            if (!sawHeader || idatLen < 6) return false;

            int pos = 2;
            if ((_idat[0] & 0x0F) != 8) return false;
            if ((_idat[1] & 0x20) != 0) pos = 6; // FDICT
            int deflateLen = idatLen - pos - 4;
            if (deflateLen <= 0) return false;

            int stride = width * 3;
            int rawNeed = height * (stride + 1);
            Ensure(ref _raw, rawNeed);
            try
            {
                using (var ms = new MemoryStream(_idat, pos, deflateLen, false))
                using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                {
                    int read = 0;
                    while (read < rawNeed)
                    {
                        int n = ds.Read(_raw, read, rawNeed - read);
                        if (n <= 0) return false;
                        read += n;
                    }
                }
            }
            catch (InvalidDataException) { return false; }

            Ensure(ref _rgb, height * stride);
            if (!Unfilter(_raw, width, height, _rgb)) return false;
            rgbLen = height * stride;
            return true;
        }

        static bool Unfilter(byte[] raw, int width, int height, byte[] rgb)
        {
            int bpp = 3;
            int stride = width * bpp;
            for (int y = 0; y < height; y++)
            {
                int inRow = y * (stride + 1);
                int filter = raw[inRow];
                int outRow = y * stride;
                for (int x = 0; x < stride; x++)
                {
                    int left = x >= bpp ? rgb[outRow + x - bpp] : 0;
                    int up = y > 0 ? rgb[outRow - stride + x] : 0;
                    int upLeft = (y > 0 && x >= bpp) ? rgb[outRow - stride + x - bpp] : 0;
                    int pred;
                    switch (filter)
                    {
                        case 0: pred = 0; break;
                        case 1: pred = left; break;
                        case 2: pred = up; break;
                        case 3: pred = (left + up) / 2; break;
                        case 4: pred = Paeth(left, up, upLeft); break;
                        default: return false;
                    }
                    rgb[outRow + x] = (byte)((raw[inRow + 1 + x] + pred) & 0xFF);
                }
            }
            return true;
        }

        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            if (pb <= pc) return b;
            return c;
        }

        static byte[] ZlibWrap(byte[] raw)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78);
                ms.WriteByte(0x9C);
                using (var ds = new DeflateStream(ms, CompressionMode.Compress, true))
                    ds.Write(raw, 0, raw.Length);
                uint adler = Adler32(raw, 0, raw.Length);
                ms.WriteByte((byte)(adler >> 24));
                ms.WriteByte((byte)(adler >> 16));
                ms.WriteByte((byte)(adler >> 8));
                ms.WriteByte((byte)adler);
                return ms.ToArray();
            }
        }

        static uint Adler32(byte[] data, int offset, int count)
        {
            uint a = 1, b = 0;
            for (int i = 0; i < count; i++)
            {
                a = (a + data[offset + i]) % 65521u;
                b = (b + a) % 65521u;
            }
            return (b << 16) | a;
        }

        static byte[] WritePng(int width, int height, byte[] zlib)
        {
            var ihdr = new byte[13];
            WriteBE(ihdr, 0, width);
            WriteBE(ihdr, 4, height);
            ihdr[8] = 8;
            ihdr[9] = 2;
            using (var ms = new MemoryStream())
            {
                ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                WriteChunk(ms, "IHDR", ihdr);
                WriteChunk(ms, "IDAT", zlib);
                WriteChunk(ms, "IEND", Array.Empty<byte>());
                return ms.ToArray();
            }
        }

        static void WriteChunk(Stream stream, string type, byte[] data)
        {
            var typeBytes = Encoding.ASCII.GetBytes(type);
            WriteBE(stream, data.Length);
            stream.Write(typeBytes, 0, 4);
            if (data.Length > 0) stream.Write(data, 0, data.Length);
            uint crc = 0xFFFFFFFFu;
            crc = Crc32(typeBytes, 0, 4, crc);
            crc = Crc32(data, 0, data.Length, crc);
            WriteBE(stream, (int)(crc ^ 0xFFFFFFFFu));
        }

        static uint Crc32(byte[] data, int offset, int count, uint crc)
        {
            for (int i = 0; i < count; i++)
                crc = CrcTable[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        static int ReadBE(byte[] b, int o)
        {
            return (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        }

        static void WriteBE(byte[] b, int o, int value)
        {
            b[o] = (byte)(value >> 24);
            b[o + 1] = (byte)(value >> 16);
            b[o + 2] = (byte)(value >> 8);
            b[o + 3] = (byte)value;
        }

        static void WriteBE(Stream stream, int value)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        static void Ensure(ref byte[] buffer, int size)
        {
            if (buffer == null || buffer.Length < size)
                buffer = new byte[Math.Max(size, 256)];
        }
    }
}
