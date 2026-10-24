// Citymaker gallery — a tiny dependency-free rasterizer: supersampled scanline polygon fill with
// alpha blending, thick polylines, circles, and a PNG writer (zlib from the BCL, own CRC32).
// World units are metres; the canvas maps world XZ to pixels (X right, Z up).

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Unity.Mathematics;

namespace Citymaker.Gallery
{
    public struct Rgba
    {
        public float R, G, B, A;
        public Rgba(float r, float g, float b, float a = 1f) { R = r; G = g; B = b; A = a; }
        public static Rgba Hex(string hex, float a = 1f)
        {
            hex = hex.TrimStart('#');
            return new Rgba(Convert.ToInt32(hex.Substring(0, 2), 16) / 255f, Convert.ToInt32(hex.Substring(2, 2), 16) / 255f,
                Convert.ToInt32(hex.Substring(4, 2), 16) / 255f, a);
        }
        public Rgba WithA(float a) => new Rgba(R, G, B, a);
        public Rgba Darker(float f) => new Rgba(R * f, G * f, B * f, A);
    }

    public sealed class Canvas
    {
        public readonly int W, H;          // output size
        private readonly int _ss;          // supersampling factor
        private readonly int _w, _h;       // internal size
        private readonly float[] _px;      // RGB
        private readonly float2 _origin;   // world XZ at output pixel (0, H)
        private readonly float _ppm;       // output pixels per metre

        public float PixelsPerMetre => _ppm;

        public Canvas(int w, int h, float2 worldMin, float ppm, Rgba background, int ss = 3)
        {
            W = w; H = h; _ss = ss; _w = w * ss; _h = h * ss; _origin = worldMin; _ppm = ppm;
            _px = new float[_w * _h * 3];
            for (int i = 0; i < _w * _h; i++) { _px[i * 3] = background.R; _px[i * 3 + 1] = background.G; _px[i * 3 + 2] = background.B; }
        }

        /// <summary>World XZ → internal (supersampled) pixel coordinates.</summary>
        private float2 P(float2 w) => new float2((w.x - _origin.x) * _ppm * _ss, _h - (w.y - _origin.y) * _ppm * _ss);
        /// <summary>(s33) Output pixel (x right, y down) of a world point.</summary>
        public int2 OutPx(float2 w) { var p = P(w) / _ss; return new int2((int)p.x, (int)p.y); }

        /// <summary>World metres for one output pixel.</summary>
        public float Px(float pixels) => pixels / _ppm;

        public void FillPolygon(IReadOnlyList<float2> ring, Rgba c) => FillPolygons(new[] { ring }, c);

        /// <summary>Even-odd fill of one or more rings (world XZ) in one blend pass.</summary>
        public void FillPolygons(IReadOnlyList<IReadOnlyList<float2>> rings, Rgba c)
        {
            if (c.A <= 0f) return;
            var edges = new List<(float2 a, float2 b)>();
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var ring in rings)
            {
                if (ring == null || ring.Count < 3) continue;
                for (int i = 0; i < ring.Count; i++)
                {
                    var a = P(ring[i]); var b = P(ring[(i + 1) % ring.Count]);
                    if (a.y == b.y) continue;
                    edges.Add((a, b));
                    minY = math.min(minY, math.min(a.y, b.y)); maxY = math.max(maxY, math.max(a.y, b.y));
                }
            }
            if (edges.Count == 0) return;
            int y0 = math.max(0, (int)math.floor(minY)), y1 = math.min(_h - 1, (int)math.ceil(maxY));
            var xs = new List<float>();
            for (int y = y0; y <= y1; y++)
            {
                float sy = y + 0.5f;
                xs.Clear();
                foreach (var (a, b) in edges)
                {
                    if ((sy >= a.y && sy < b.y) || (sy >= b.y && sy < a.y))
                        xs.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                if (xs.Count < 2) continue;
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int xa = math.max(0, (int)math.ceil(xs[k] - 0.5f)), xb = math.min(_w - 1, (int)math.floor(xs[k + 1] - 0.5f));
                    int row = y * _w;
                    for (int x = xa; x <= xb; x++) Blend(row + x, c);
                }
            }
        }

        /// <summary>Filled rectangle in OUTPUT pixel coordinates (top-left origin), for legends.</summary>
        public void RectPx(int x, int y, int w, int h, Rgba c)
        {
            int x0 = math.max(0, x * _ss), y0 = math.max(0, y * _ss), x1 = math.min(_w, (x + w) * _ss), y1 = math.min(_h, (y + h) * _ss);
            for (int yy = y0; yy < y1; yy++)
                for (int xx = x0; xx < x1; xx++) Blend(yy * _w + xx, c);
        }

        // 5×7 pixel font (A–Z, 0–9 and a few signs), one string per glyph: 7 rows of 5 bits.
        private static readonly Dictionary<char, string> Font = new Dictionary<char, string>
        {
            ['A'] = "01110100011000111111100011000110001", ['B'] = "11110100011000111110100011000111110", ['C'] = "01110100011000010000100001000101110",
            ['D'] = "11110100011000110001100011000111110", ['E'] = "11111100001000011110100001000011111", ['F'] = "11111100001000011110100001000010000",
            ['G'] = "01110100011000010111100011000101111", ['H'] = "10001100011000111111100011000110001", ['I'] = "01110001000010000100001000010001110",
            ['J'] = "00111000100001000010000101001001100", ['K'] = "10001100101010011000101001001010001", ['L'] = "10000100001000010000100001000011111",
            ['M'] = "10001110111010110101100011000110001", ['N'] = "10001110011010110011100011000110001", ['O'] = "01110100011000110001100011000101110",
            ['P'] = "11110100011000111110100001000010000", ['Q'] = "01110100011000110001101011001001101", ['R'] = "11110100011000111110101001001010001",
            ['S'] = "01111100001000001110000010000111110", ['T'] = "11111001000010000100001000010000100", ['U'] = "10001100011000110001100011000101110",
            ['V'] = "10001100011000110001100010101000100", ['W'] = "10001100011000110101101011010101010", ['X'] = "10001100010101000100010101000110001",
            ['Y'] = "10001100010101000100001000010000100", ['Z'] = "11111000010001000100010001000011111",
            ['0'] = "01110100011001110101110011000101110", ['1'] = "00100011000010000100001000010001110", ['2'] = "01110100010000100010001000100011111",
            ['3'] = "11111000100010000010000011000101110", ['4'] = "00010001100101010010111110001000010", ['5'] = "11111100001111000001000011000101110",
            ['6'] = "00110010001000011110100011000101110", ['7'] = "11111000010001000100010000100001000", ['8'] = "01110100011000101110100011000101110",
            ['9'] = "01110100011000101111000010001001100", ['.'] = "00000000000000000000000000110001100", ['-'] = "00000000000000011111000000000000000",
            ['%'] = "11000110010001000100010001001100011", ['&'] = "01100100101010001000101011001001101", [':'] = "00000011000110000000011000110000000",
            ['/'] = "00000000010001000100010001000000000", ['('] = "00010001000100001000010000010000010", [')'] = "01000001000001000010000100010001000",
            [' '] = "00000000000000000000000000000000000",
        };

        /// <summary>Text in OUTPUT pixel coordinates (top-left of the first glyph), 5×7 font × scale.</summary>
        public void TextPx(int x, int y, string s, Rgba c, int scale = 2)
        {
            int cx = x;
            foreach (var ch0 in s ?? "")
            {
                char ch = char.ToUpperInvariant(ch0);
                if (!Font.TryGetValue(ch, out var g)) g = Font[' '];
                for (int r = 0; r < 7; r++)
                    for (int k = 0; k < 5; k++)
                        if (g[r * 5 + k] == '1') RectPx(cx + k * scale, y + r * scale, scale, scale, c);
                cx += 6 * scale;
            }
        }

        private void Blend(int i, Rgba c)
        {
            i *= 3;
            float a = c.A, ia = 1f - a;
            _px[i] = _px[i] * ia + c.R * a;
            _px[i + 1] = _px[i + 1] * ia + c.G * a;
            _px[i + 2] = _px[i + 2] * ia + c.B * a;
        }

        public void FillCircle(float2 centre, float radius, Rgba c, int segments = 0)
        {
            if (segments <= 0) segments = math.clamp((int)(radius * _ppm * 1.5f), 10, 48);
            var ring = new float2[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = i * math.PI * 2f / segments;
                ring[i] = centre + new float2(math.cos(a), math.sin(a)) * radius;
            }
            FillPolygon(ring, c);
        }

        /// <summary>Annulus (ring) between two radii.</summary>
        public void StrokeCircle(float2 centre, float radius, float width, Rgba c)
        {
            int seg = math.clamp((int)(radius * _ppm * 1.5f), 12, 64);
            var outer = new float2[seg]; var inner = new float2[seg];
            for (int i = 0; i < seg; i++)
            {
                float a = i * math.PI * 2f / seg;
                var d = new float2(math.cos(a), math.sin(a));
                outer[i] = centre + d * (radius + width * 0.5f);
                inner[i] = centre + d * math.max(0f, radius - width * 0.5f);
            }
            FillPolygons(new IReadOnlyList<float2>[] { outer, inner }, c);
        }

        /// <summary>Thick polyline as one even-odd-free union: quads + round joints drawn into a mask, blended once.</summary>
        public void StrokePolyline(IReadOnlyList<float2> pts, float width, Rgba c, bool closed = false)
        {
            if (pts == null || pts.Count < 2 || c.A <= 0f) return;
            var mask = new HashSet<int>();
            float hw = width * 0.5f;
            int n = closed ? pts.Count : pts.Count - 1;
            for (int i = 0; i < n; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                var d = b - a; float len = math.length(d);
                if (len < 1e-5f) continue;
                var nrm = new float2(-d.y, d.x) / len * hw;
                Rasterize(new[] { a + nrm, b + nrm, b - nrm, a - nrm }, mask);
            }
            if (width * _ppm > 2.5f)
                foreach (var p in pts) Rasterize(CirclePts(p, hw, 12), mask);
            foreach (var i in mask) Blend(i, c);
        }

        public void DashedPolyline(IReadOnlyList<float2> pts, float width, float dash, float gap, Rgba c)
        {
            if (pts == null || pts.Count < 2) return;
            float pos = 0f; bool on = true; var cur = new List<float2> { pts[0] };
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i]; var b = pts[i + 1];
                float len = math.distance(a, b), t = 0f;
                while (len - t > 1e-4f)
                {
                    float lim = on ? dash : gap;
                    float step = math.min(lim - pos, len - t);
                    t += step; pos += step;
                    var p = math.lerp(a, b, t / len);
                    if (on) cur.Add(p);
                    if (pos >= lim - 1e-5f)
                    {
                        if (on && cur.Count >= 2) StrokePolyline(cur, width, c);
                        on = !on; pos = 0f; cur = new List<float2> { p };
                    }
                }
            }
            if (on && cur.Count >= 2) StrokePolyline(cur, width, c);
        }

        private static float2[] CirclePts(float2 c, float r, int n)
        {
            var a = new float2[n];
            for (int i = 0; i < n; i++) { float t = i * math.PI * 2f / n; a[i] = c + new float2(math.cos(t), math.sin(t)) * r; }
            return a;
        }

        private void Rasterize(float2[] worldRing, HashSet<int> mask)
        {
            var r = new float2[worldRing.Length];
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < r.Length; i++) { r[i] = P(worldRing[i]); minY = math.min(minY, r[i].y); maxY = math.max(maxY, r[i].y); }
            int y0 = math.max(0, (int)math.floor(minY)), y1 = math.min(_h - 1, (int)math.ceil(maxY));
            var xs = new List<float>(4);
            for (int y = y0; y <= y1; y++)
            {
                float sy = y + 0.5f; xs.Clear();
                for (int i = 0; i < r.Length; i++)
                {
                    var a = r[i]; var b = r[(i + 1) % r.Length];
                    if ((sy >= a.y && sy < b.y) || (sy >= b.y && sy < a.y)) xs.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                if (xs.Count < 2) continue;
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int xa = math.max(0, (int)math.ceil(xs[k] - 0.5f)), xb = math.min(_w - 1, (int)math.floor(xs[k + 1] - 0.5f));
                    for (int x = xa; x <= xb; x++) mask.Add(y * _w + x);
                }
            }
        }

        /// <summary>Per-output-pixel shading callback (world XZ at the pixel centre → colour to blend, A=0 for none).</summary>
        public void Shade(Func<float2, Rgba> f, float2 min, float2 max)
        {
            var p0 = P(new float2(min.x, max.y)); var p1 = P(new float2(max.x, min.y));
            int x0 = math.max(0, (int)p0.x), x1 = math.min(_w - 1, (int)p1.x), y0 = math.max(0, (int)p0.y), y1 = math.min(_h - 1, (int)p1.y);
            for (int y = y0; y <= y1; y += _ss)
                for (int x = x0; x <= x1; x += _ss)
                {
                    var w = new float2(_origin.x + (x + _ss * 0.5f) / (_ppm * _ss), _origin.y + (_h - y - _ss * 0.5f) / (_ppm * _ss));
                    var c = f(w);
                    if (c.A <= 0f) continue;
                    for (int dy = 0; dy < _ss && y + dy < _h; dy++)
                        for (int dx = 0; dx < _ss && x + dx < _w; dx++) Blend((y + dy) * _w + x + dx, c);
                }
        }

        /// <summary>(s20) Averaged output colour at a world point (A = 0 outside the canvas).</summary>
        public Rgba SampleWorld(float2 w)
        {
            var p = P(w);
            int x0 = (int)math.floor(p.x / _ss) * _ss, y0 = (int)math.floor(p.y / _ss) * _ss;
            if (x0 < 0 || y0 < 0 || x0 + _ss > _w || y0 + _ss > _h) return default;
            float r = 0, g = 0, b = 0;
            for (int dy = 0; dy < _ss; dy++)
                for (int dx = 0; dx < _ss; dx++)
                {
                    int i = ((y0 + dy) * _w + x0 + dx) * 3;
                    r += _px[i]; g += _px[i + 1]; b += _px[i + 2];
                }
            float inv = 1f / (_ss * _ss);
            return new Rgba(r * inv, g * inv, b * inv, 1f);
        }

        /// <summary>(s20) Writes one output pixel (all its supersamples), y = 0 at the top.</summary>
        public void SetPixel(int x, int y, Rgba c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            for (int dy = 0; dy < _ss; dy++)
                for (int dx = 0; dx < _ss; dx++)
                {
                    int i = ((y * _ss + dy) * _w + x * _ss + dx) * 3;
                    _px[i] = c.R; _px[i + 1] = c.G; _px[i + 2] = c.B;
                }
        }

        public void SavePng(string path)
        {
            var raw = new byte[H * (W * 3 + 1)];
            float inv = 1f / (_ss * _ss);
            for (int y = 0; y < H; y++)
            {
                int o = y * (W * 3 + 1);
                raw[o++] = 0;
                for (int x = 0; x < W; x++)
                {
                    float r = 0, g = 0, b = 0;
                    for (int dy = 0; dy < _ss; dy++)
                        for (int dx = 0; dx < _ss; dx++)
                        {
                            int i = ((y * _ss + dy) * _w + x * _ss + dx) * 3;
                            r += _px[i]; g += _px[i + 1]; b += _px[i + 2];
                        }
                    raw[o++] = ToByte(r * inv); raw[o++] = ToByte(g * inv); raw[o++] = ToByte(b * inv);
                }
            }
            byte[] z;
            using (var ms = new MemoryStream())
            {
                using (var zs = new ZLibStream(ms, CompressionLevel.Optimal, true)) zs.Write(raw, 0, raw.Length);
                z = ms.ToArray();
            }
            using var f = File.Create(path);
            f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var ihdr = new byte[13];
            BE(ihdr, 0, (uint)W); BE(ihdr, 4, (uint)H); ihdr[8] = 8; ihdr[9] = 2;
            Chunk(f, "IHDR", ihdr); Chunk(f, "IDAT", z); Chunk(f, "IEND", Array.Empty<byte>());
        }

        private static byte ToByte(float v) => (byte)math.clamp((int)math.round(v * 255f), 0, 255);

        private static void BE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; BE(len, 0, (uint)data.Length); s.Write(len);
            var t = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(t); s.Write(data);
            uint crc = Crc(Crc(0xFFFFFFFFu, t), data) ^ 0xFFFFFFFFu;
            var c = new byte[4]; BE(c, 0, crc); s.Write(c);
        }

        private static uint[] _table;
        private static uint Crc(uint crc, byte[] data)
        {
            if (_table == null)
            {
                var t = new uint[256];
                for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[n] = c; }
                _table = t;
            }
            foreach (var b in data) crc = _table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
