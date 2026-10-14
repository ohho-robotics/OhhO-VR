using System;
using System.IO;

namespace OmniBot.VR.Video
{
    /// <summary>
    /// Baseline sequential JPEG (SOF0 / SOF1) to top-down RGB24.
    /// No Unity API, so a worker thread can run it. One instance is not thread-safe;
    /// each stream keeps its own. Quant tables, Huffman tables, component slots,
    /// and the RGB/plane buffers are reused across frames.
    /// <see cref="GrowthCount"/> increments only when one of those buffers has to
    /// grow. This is the path <c>web_video_server</c> produces
    /// (OpenCV / libjpeg baseline, usually 4:2:0). Progressive and arithmetic JPEGs fail.
    /// </summary>
    public sealed class BaselineJpegDecoder
    {
        public const int MaxDimension = 4096;

        private static readonly int[] ZigZag =
        {
            0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
            12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63
        };

        private static readonly double[,] Cos = BuildCos();

        private readonly HuffmanTable[,] _huff = new HuffmanTable[2, 4];
        private readonly bool[,] _huffOk = new bool[2, 4];
        private readonly int[][] _quant = new int[4][];
        private readonly bool[] _quantOk = new bool[4];
        private readonly int[] _huffCounts = new int[17];
        private readonly byte[] _huffSymbols = new byte[256];
        private readonly Comp[] _frame = new Comp[4];
        private readonly Comp[] _scan = new Comp[4];
        private readonly int[] _coef = new int[64];
        private readonly double[] _tmp = new double[64];
        private readonly byte[] _spatial = new byte[64];
        private readonly byte[][] _planes = new byte[4][];

        private byte[] _data;
        private int _len;
        private int _pos;
        private int _bitBuf;
        private int _bitCount;
        private int _width;
        private int _height;
        private int _nComp;
        private int _scanCount;
        private int _maxH;
        private int _maxV;
        private int _dri;
        private bool _rgb;
        private bool _decoded;

        /// <summary>How many times a reused buffer had to grow. Steady-state decodes stay flat.</summary>
        public int GrowthCount { get; private set; }

        public BaselineJpegDecoder()
        {
            for (int i = 0; i < 4; i++)
            {
                _frame[i] = new Comp();
                _quant[i] = new int[64];
                _huff[0, i] = new HuffmanTable();
                _huff[1, i] = new HuffmanTable();
            }
        }

        /// <summary>
        /// Decode <paramref name="jpeg"/>. <paramref name="rgb"/> is top-down RGB24
        /// and is reused when it is already large enough.
        /// </summary>
        public bool TryDecode(byte[] jpeg, int length, ref byte[] rgb, out int width, out int height, out string error)
        {
            width = 0;
            height = 0;
            error = null;
            if (jpeg == null || length < 4 || length > jpeg.Length)
            {
                error = "empty jpeg";
                return false;
            }

            try
            {
                Reset(jpeg, length);
                Parse();
                if (!_decoded || _width <= 0 || _height <= 0)
                    throw new InvalidDataException("no image");
                int bytes = _width * _height * 3;
                if (rgb == null || rgb.Length < bytes)
                {
                    rgb = new byte[bytes];
                    GrowthCount++;
                }
                WriteRgb(rgb);
                width = _width;
                height = _height;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                width = 0;
                height = 0;
                return false;
            }
        }

        /// <summary>Copy top-down RGB24 into Unity's bottom-up raw layout.</summary>
        public static void FlipVertical(byte[] src, byte[] dst, int width, int height)
        {
            if (src == null || dst == null) throw new ArgumentNullException();
            int stride = width * 3;
            if (width <= 0 || height <= 0 || src.Length < stride * height || dst.Length < stride * height)
                throw new ArgumentException("rgb buffer is shorter than width*height*3");
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(src, y * stride, dst, (height - 1 - y) * stride, stride);
        }

        private void Reset(byte[] jpeg, int length)
        {
            _data = jpeg;
            _len = length;
            _pos = 0;
            _bitBuf = 0;
            _bitCount = 0;
            _width = 0;
            _height = 0;
            _nComp = 0;
            _scanCount = 0;
            _maxH = 1;
            _maxV = 1;
            _dri = 0;
            _rgb = false;
            _decoded = false;
            for (int i = 0; i < 4; i++)
            {
                _quantOk[i] = false;
                _huffOk[0, i] = false;
                _huffOk[1, i] = false;
                _frame[i].Dc = 0;
                _scan[i] = null;
            }
        }

        private void Parse()
        {
            int guard = 0;
            while (_pos + 1 < _len && guard++ < 100000)
            {
                if (_data[_pos] != 0xFF)
                {
                    _pos++;
                    continue;
                }
                while (_pos < _len && _data[_pos] == 0xFF) _pos++;
                if (_pos >= _len) break;
                int marker = _data[_pos++];
                if (marker == 0x00 || marker == 0xD8) continue;
                if (marker == 0xD9) break;
                if (marker >= 0xD0 && marker <= 0xD7) continue;
                if (marker == 0x01) continue;
                if (marker == 0xDA)
                {
                    DecodeScan();
                    _decoded = true;
                    return;
                }
                int seg = ReadLength();
                int end = _pos + seg;
                if (marker == 0xC0 || marker == 0xC1) ParseSof();
                else if (marker == 0xC2)
                    throw new InvalidDataException("progressive jpeg is not supported");
                else if (marker == 0xDB) ParseDqt(end);
                else if (marker == 0xC4) ParseDht(end);
                else if (marker == 0xDD) ParseDri();
                else if (marker == 0xEE) ParseAdobe(end);
                _pos = end;
            }
            throw new InvalidDataException("jpeg ended before the scan");
        }

        private int ReadLength()
        {
            if (_pos + 2 > _len) throw new InvalidDataException("truncated marker");
            int l = (_data[_pos] << 8) | _data[_pos + 1];
            _pos += 2;
            if (l < 2 || _pos + (l - 2) > _len) throw new InvalidDataException("bad marker length");
            return l - 2;
        }

        private void ParseSof()
        {
            int p = _pos;
            int precision = _data[p++];
            if (precision != 8) throw new InvalidDataException("only 8-bit jpeg");
            _height = (_data[p] << 8) | _data[p + 1];
            p += 2;
            _width = (_data[p] << 8) | _data[p + 1];
            p += 2;
            _nComp = _data[p++];
            if (_width <= 0 || _height <= 0 || _width > MaxDimension || _height > MaxDimension)
                throw new InvalidDataException("jpeg size out of range");
            if (_nComp != 1 && _nComp != 3)
                throw new InvalidDataException("only grayscale and YCbCr jpeg");
            _maxH = 1;
            _maxV = 1;
            for (int i = 0; i < _nComp; i++)
            {
                Comp c = _frame[i];
                c.Id = _data[p++];
                int hv = _data[p++];
                c.H = hv >> 4;
                c.V = hv & 15;
                c.Quant = _data[p++];
                if (c.H < 1 || c.H > 4 || c.V < 1 || c.V > 4)
                    throw new InvalidDataException("bad sampling factor");
                if (c.H > _maxH) _maxH = c.H;
                if (c.V > _maxV) _maxV = c.V;
                c.Index = i;
                _frame[i] = c;
            }
        }

        private void ParseDqt(int end)
        {
            int p = _pos;
            while (p < end)
            {
                int info = _data[p++];
                int precision = info >> 4;
                int id = info & 15;
                if (id > 3 || (precision != 0 && precision != 1))
                    throw new InvalidDataException("bad quant table");
                int[] natural = _quant[id];
                for (int i = 0; i < 64; i++)
                {
                    int qv;
                    if (precision == 0)
                    {
                        if (p >= end) throw new InvalidDataException("truncated quant table");
                        qv = _data[p++];
                    }
                    else
                    {
                        if (p + 1 >= end) throw new InvalidDataException("truncated quant table");
                        qv = (_data[p] << 8) | _data[p + 1];
                        p += 2;
                    }
                    natural[ZigZag[i]] = qv;
                }
                _quantOk[id] = true;
            }
        }

        private void ParseDht(int end)
        {
            int p = _pos;
            while (p < end)
            {
                int info = _data[p++];
                int cls = info >> 4;
                int id = info & 15;
                if (cls > 1 || id > 3) throw new InvalidDataException("bad huffman table");
                Array.Clear(_huffCounts, 0, _huffCounts.Length);
                int total = 0;
                for (int len = 1; len <= 16; len++)
                {
                    if (p >= end) throw new InvalidDataException("truncated huffman table");
                    _huffCounts[len] = _data[p++];
                    total += _huffCounts[len];
                }
                if (total > 256 || p + total > end) throw new InvalidDataException("bad huffman symbols");
                if (total > 0) Buffer.BlockCopy(_data, p, _huffSymbols, 0, total);
                p += total;
                _huff[cls, id].Fill(_huffCounts, _huffSymbols, total);
                _huffOk[cls, id] = true;
            }
        }

        private void ParseDri()
        {
            if (_pos + 2 > _len) throw new InvalidDataException("truncated dri");
            _dri = (_data[_pos] << 8) | _data[_pos + 1];
        }

        private void ParseAdobe(int end)
        {
            int n = end - _pos;
            if (n >= 12 &&
                _data[_pos] == (byte)'A' && _data[_pos + 1] == (byte)'d' &&
                _data[_pos + 2] == (byte)'o' && _data[_pos + 3] == (byte)'b' &&
                _data[_pos + 4] == (byte)'e')
            {
                _rgb = _data[_pos + 11] == 0 && _nComp == 3;
            }
        }

        private void DecodeScan()
        {
            if (_nComp == 0) throw new InvalidDataException("scan before frame header");
            if (_pos + 2 > _len) throw new InvalidDataException("truncated scan");
            int segLen = (_data[_pos] << 8) | _data[_pos + 1];
            if (segLen < 8 || _pos + segLen > _len) throw new InvalidDataException("bad scan length");
            int p = _pos + 2;
            int end = _pos + segLen;
            int ns = _data[p++];
            if (ns < 1 || ns > _nComp) throw new InvalidDataException("bad scan component count");
            _scanCount = ns;
            for (int i = 0; i < ns; i++)
            {
                if (p + 1 >= end) throw new InvalidDataException("truncated scan header");
                int id = _data[p++];
                int tables = _data[p++];
                Comp comp = null;
                for (int c = 0; c < _nComp; c++)
                {
                    if (_frame[c].Id == id) { comp = _frame[c]; break; }
                }
                if (comp == null) throw new InvalidDataException("scan names an unknown component");
                comp.DcTable = tables >> 4;
                comp.AcTable = tables & 15;
                comp.Dc = 0;
                if (comp.DcTable > 3 || comp.AcTable > 3) throw new InvalidDataException("bad table id");
                if (!_huffOk[0, comp.DcTable] || !_huffOk[1, comp.AcTable])
                    throw new InvalidDataException("missing huffman table");
                if (comp.Quant > 3 || !_quantOk[comp.Quant])
                    throw new InvalidDataException("missing quant table");
                _scan[i] = comp;
            }
            if (p + 2 >= end) throw new InvalidDataException("truncated scan header");
            int ss = _data[p++];
            int se = _data[p++];
            int ahal = _data[p++];
            if (ss != 0 || se != 63 || ahal != 0)
                throw new InvalidDataException("not a baseline scan");

            // Entropy bytes start immediately after the SOS segment.
            _pos = end;
            _bitCount = 0;

            int mcuW = _maxH * 8;
            int mcuH = _maxV * 8;
            int mcuCols = (_width + mcuW - 1) / mcuW;
            int mcuRows = (_height + mcuH - 1) / mcuH;
            if (mcuCols <= 0 || mcuRows <= 0 || (long)mcuCols * mcuRows > 300000)
                throw new InvalidDataException("too many mcus");

            for (int i = 0; i < ns; i++)
            {
                var c = _scan[i];
                c.PlaneW = mcuCols * c.H * 8;
                c.PlaneH = mcuRows * c.V * 8;
                int need = c.PlaneW * c.PlaneH;
                if (_planes[c.Index] == null || _planes[c.Index].Length < need)
                {
                    _planes[c.Index] = new byte[need];
                    GrowthCount++;
                }
            }

            int produced = 0;
            int rst = 0;
            for (int my = 0; my < mcuRows; my++)
            {
                for (int mx = 0; mx < mcuCols; mx++)
                {
                    if (_dri > 0 && produced > 0 && (produced % _dri) == 0)
                    {
                        ConsumeRestart(ref rst);
                        for (int i = 0; i < ns; i++) _scan[i].Dc = 0;
                    }
                    for (int i = 0; i < ns; i++)
                    {
                        var c = _scan[i];
                        int block = 0;
                        for (int by = 0; by < c.V; by++)
                        {
                            for (int bx = 0; bx < c.H; bx++)
                            {
                                int ox = mx * c.H * 8 + bx * 8;
                                int oy = my * c.V * 8 + by * 8;
                                DecodeBlock(c, ox, oy);
                                block++;
                            }
                        }
                    }
                    produced++;
                }
            }
        }

        private void ConsumeRestart(ref int expected)
        {
            _bitCount = 0;
            if (_pos >= _len || _data[_pos] != 0xFF)
                throw new InvalidDataException("restart marker missing");
            _pos++;
            while (_pos < _len && _data[_pos] == 0xFF) _pos++;
            if (_pos >= _len) throw new InvalidDataException("restart marker missing");
            int m = _data[_pos++];
            if (m < 0xD0 || m > 0xD7) throw new InvalidDataException("bad restart marker");
            expected = (expected + 1) & 7;
        }

        private void DecodeBlock(Comp comp, int ox, int oy)
        {
            Array.Clear(_coef, 0, 64);
            int cat = ReadSymbol(_huff[0, comp.DcTable]);
            if (cat < 0 || cat > 11) throw new InvalidDataException("bad dc category");
            int diff = cat == 0 ? 0 : Extend(ReadBits(cat), cat);
            comp.Dc += diff;
            _coef[0] = comp.Dc * _quant[comp.Quant][0];

            bool onlyDc = true;
            int k = 1;
            while (k < 64)
            {
                int rs = ReadSymbol(_huff[1, comp.AcTable]);
                int s = rs & 15;
                int r = rs >> 4;
                if (s == 0)
                {
                    if (r == 15)
                    {
                        k += 16;
                        continue;
                    }
                    break;
                }
                if (s > 10) throw new InvalidDataException("bad ac category");
                k += r;
                if (k >= 64) throw new InvalidDataException("ac run past end of block");
                int nat = ZigZag[k];
                _coef[nat] = Extend(ReadBits(s), s) * _quant[comp.Quant][nat];
                onlyDc = false;
                k++;
            }

            var plane = _planes[comp.Index];
            int stride = comp.PlaneW;
            if (onlyDc)
            {
                int level = ClampByte((int)Math.Round(_coef[0] / 8.0) + 128);
                byte b = (byte)level;
                for (int j = 0; j < 8; j++)
                {
                    int row = (oy + j) * stride + ox;
                    for (int i = 0; i < 8; i++) plane[row + i] = b;
                }
                return;
            }

            Idct(_coef, _spatial);
            for (int j = 0; j < 8; j++)
                Buffer.BlockCopy(_spatial, j * 8, plane, (oy + j) * stride + ox, 8);
        }

        private void WriteRgb(byte[] rgb)
        {
            bool gray = _nComp == 1;
            bool rgbDirect = _rgb;
            Comp y = _frame[0];
            Comp cb = _nComp > 1 ? _frame[1] : null;
            Comp cr = _nComp > 2 ? _frame[2] : null;
            var yPlane = _planes[y.Index];
            var cbPlane = cb != null ? _planes[cb.Index] : null;
            var crPlane = cr != null ? _planes[cr.Index] : null;
            int o = 0;
            for (int py = 0; py < _height; py++)
            {
                for (int px = 0; px < _width; px++)
                {
                    int Y = Sample(yPlane, y, px, py);
                    if (gray || cb == null)
                    {
                        rgb[o++] = (byte)Y;
                        rgb[o++] = (byte)Y;
                        rgb[o++] = (byte)Y;
                        continue;
                    }
                    int c1 = Sample(cbPlane, cb, px, py);
                    int c2 = Sample(crPlane, cr, px, py);
                    if (rgbDirect)
                    {
                        rgb[o++] = (byte)Y;
                        rgb[o++] = (byte)c1;
                        rgb[o++] = (byte)c2;
                        continue;
                    }
                    int Cb = c1 - 128;
                    int Cr = c2 - 128;
                    int R = ClampByte((int)Math.Round(Y + 1.402 * Cr));
                    int G = ClampByte((int)Math.Round(Y - 0.344136 * Cb - 0.714136 * Cr));
                    int B = ClampByte((int)Math.Round(Y + 1.772 * Cb));
                    rgb[o++] = (byte)R;
                    rgb[o++] = (byte)G;
                    rgb[o++] = (byte)B;
                }
            }
        }

        private int Sample(byte[] plane, Comp c, int x, int y)
        {
            int sx = x * c.H / _maxH;
            int sy = y * c.V / _maxV;
            if (sx < 0) sx = 0;
            if (sy < 0) sy = 0;
            if (sx >= c.PlaneW) sx = c.PlaneW - 1;
            if (sy >= c.PlaneH) sy = c.PlaneH - 1;
            return plane[sy * c.PlaneW + sx];
        }

        private void Idct(int[] coef, byte[] dest)
        {
            for (int v = 0; v < 8; v++)
            {
                for (int x = 0; x < 8; x++)
                {
                    double sum = 0;
                    int row = v * 8;
                    for (int u = 0; u < 8; u++)
                        sum += Cos[u, x] * coef[row + u];
                    _tmp[row + x] = sum;
                }
            }
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    double sum = 0;
                    for (int v = 0; v < 8; v++)
                        sum += Cos[v, y] * _tmp[v * 8 + x];
                    dest[y * 8 + x] = (byte)ClampByte((int)Math.Round(sum) + 128);
                }
            }
        }

        private int ReadSymbol(HuffmanTable table)
        {
            int code = 0;
            for (int len = 1; len <= 16; len++)
            {
                code = (code << 1) | ReadBit();
                if (table.MaxCode[len] >= 0 && code >= table.MinCode[len] && code <= table.MaxCode[len])
                {
                    int idx = table.ValPtr[len] + (code - table.MinCode[len]);
                    if ((uint)idx >= (uint)table.SymbolCount)
                        throw new InvalidDataException("huffman symbol out of range");
                    return table.Symbols[idx];
                }
            }
            throw new InvalidDataException("huffman miss");
        }

        private int ReadBits(int n)
        {
            int v = 0;
            for (int i = 0; i < n; i++)
                v = (v << 1) | ReadBit();
            return v;
        }

        private int ReadBit()
        {
            if (_bitCount == 0)
            {
                int b = ReadEntropyByte();
                _bitBuf = b;
                _bitCount = 8;
            }
            _bitCount--;
            return (_bitBuf >> _bitCount) & 1;
        }

        private int ReadEntropyByte()
        {
            if (_pos >= _len) throw new InvalidDataException("truncated entropy");
            int b = _data[_pos++];
            if (b != 0xFF) return b;
            if (_pos >= _len) throw new InvalidDataException("truncated stuffed byte");
            int n = _data[_pos++];
            while (n == 0xFF && _pos < _len) n = _data[_pos++];
            if (n == 0x00) return 0xFF;
            throw new InvalidDataException("marker inside entropy");
        }

        private static int Extend(int v, int n)
        {
            int vt = 1 << (n - 1);
            if (v < vt) v += ((-1) << n) + 1;
            return v;
        }

        private static int ClampByte(int v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return v;
        }

        private static double[,] BuildCos()
        {
            var t = new double[8, 8];
            for (int u = 0; u < 8; u++)
            {
                double c = u == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0;
                for (int x = 0; x < 8; x++)
                    t[u, x] = 0.5 * c * Math.Cos((2 * x + 1) * u * Math.PI / 16.0);
            }
            return t;
        }

        private sealed class Comp
        {
            public int Id;
            public int H;
            public int V;
            public int Quant;
            public int DcTable;
            public int AcTable;
            public int Dc;
            public int Index;
            public int PlaneW;
            public int PlaneH;
        }

        private sealed class HuffmanTable
        {
            public readonly int[] MinCode = new int[17];
            public readonly int[] MaxCode = new int[17];
            public readonly int[] ValPtr = new int[17];
            public byte[] Symbols = new byte[256];
            public int SymbolCount;

            public void Fill(int[] counts, byte[] symbols, int total)
            {
                if (Symbols == null || Symbols.Length < total)
                    Symbols = new byte[total];
                if (total > 0) Buffer.BlockCopy(symbols, 0, Symbols, 0, total);
                SymbolCount = total;
                int code = 0;
                int si = 0;
                for (int len = 1; len <= 16; len++)
                {
                    if (counts[len] == 0)
                    {
                        MinCode[len] = -1;
                        MaxCode[len] = -1;
                        ValPtr[len] = -1;
                    }
                    else
                    {
                        MinCode[len] = code;
                        MaxCode[len] = code + counts[len] - 1;
                        ValPtr[len] = si;
                        si += counts[len];
                    }
                    code += counts[len];
                    code <<= 1;
                }
            }
        }
    }
}
