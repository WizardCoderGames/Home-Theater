// A small QR Code encoder (byte mode, error-correction level M, versions 1-10), enough for a URL.
// Follows the QR Code specification (ISO/IEC 18004) the same way most reference encoders do.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;

namespace Theater
{
    static class Qr
    {
        // Error-correction codewords per block and number of blocks for level M, versions 0..10.
        static readonly int[] EccPerBlock = { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
        static readonly int[] NumBlocks = { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };

        // Returns the module grid ([y, x], true = dark), or null if the text is too long.
        public static bool[,] Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            int ver = 1;
            for (; ver <= 10; ver++)
            {
                int capacityBits = DataCodewords(ver) * 8;
                int needed = 4 + (ver < 10 ? 8 : 16) + data.Length * 8;
                if (needed <= capacityBits) break;
            }
            if (ver > 10) return null;

            // Bit stream: byte mode, length, data, terminator, padding.
            var bits = new List<bool>();
            Append(bits, 0x4, 4);
            Append(bits, data.Length, ver < 10 ? 8 : 16);
            foreach (byte b in data) Append(bits, b, 8);
            int cap = DataCodewords(ver) * 8;
            Append(bits, 0, Math.Min(4, cap - bits.Count));
            Append(bits, 0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < cap; pad ^= 0xEC ^ 0x11) Append(bits, pad, 8);
            var codewords = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

            byte[] all = AddEcc(codewords, ver);
            int size = ver * 4 + 17;
            var modules = new bool[size, size];
            var isFunc = new bool[size, size];
            DrawFunctionPatterns(modules, isFunc, ver);
            DrawCodewords(modules, isFunc, all);

            // Choose the mask with the lowest penalty.
            int bestMask = 0;
            long bestScore = long.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                ApplyMask(modules, isFunc, m);
                DrawFormatBits(modules, isFunc, m);
                long score = Penalty(modules);
                if (score < bestScore) { bestScore = score; bestMask = m; }
                ApplyMask(modules, isFunc, m);   // undo (XOR)
            }
            ApplyMask(modules, isFunc, bestMask);
            DrawFormatBits(modules, isFunc, bestMask);
            return modules;
        }

        // Renders with a 4-module quiet zone, each module exactly `px` pixels (crisp edges).
        public static Bitmap Render(bool[,] m, int px, Color dark, Color light)
        {
            int n = m.GetLength(0), total = (n + 8) * px;
            var bmp = new Bitmap(total, total, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bmp))
            using (var db = new SolidBrush(dark))
            {
                g.Clear(light);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        if (m[y, x]) g.FillRectangle(db, (x + 4) * px, (y + 4) * px, px, px);
            }
            return bmp;
        }

        static int RawDataModules(int ver)
        {
            int result = (16 * ver + 128) * ver + 64;
            if (ver >= 2)
            {
                int numAlign = ver / 7 + 2;
                result -= (25 * numAlign - 10) * numAlign - 55;
                if (ver >= 7) result -= 36;
            }
            return result;
        }

        static int DataCodewords(int ver) { return RawDataModules(ver) / 8 - EccPerBlock[ver] * NumBlocks[ver]; }

        static void Append(List<bool> bits, int val, int len)
        {
            for (int i = len - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0);
        }

        static byte[] AddEcc(byte[] data, int ver)
        {
            int numBlocks = NumBlocks[ver], eccLen = EccPerBlock[ver];
            int raw = RawDataModules(ver) / 8;
            int numShort = numBlocks - raw % numBlocks, shortLen = raw / numBlocks;
            byte[] divisor = RsDivisor(eccLen);
            var blocks = new List<byte[]>();
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                int datLen = shortLen - eccLen + (i < numShort ? 0 : 1);
                var dat = new byte[datLen];
                Array.Copy(data, k, dat, 0, datLen);
                k += datLen;
                byte[] ecc = RsRemainder(dat, divisor);
                var block = new byte[shortLen + 1];
                Array.Copy(dat, block, datLen);
                Array.Copy(ecc, 0, block, block.Length - eccLen, eccLen);
                blocks.Add(block);
            }
            var result = new List<byte>();
            for (int i = 0; i < shortLen + 1; i++)
                for (int j = 0; j < blocks.Count; j++)
                    if (i != shortLen - eccLen || j >= numShort) result.Add(blocks[j][i]);
            return result.ToArray();
        }

        static byte[] RsDivisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < result.Length; j++)
                {
                    result[j] = Mul(result[j], root);
                    if (j + 1 < result.Length) result[j] ^= result[j + 1];
                }
                root = Mul(root, 0x02);
            }
            return result;
        }

        static byte[] RsRemainder(byte[] data, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= Mul(divisor[i], factor);
            }
            return result;
        }

        static byte Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }

        static void Set(bool[,] m, bool[,] f, int x, int y, bool dark) { m[y, x] = dark; f[y, x] = true; }

        static void DrawFunctionPatterns(bool[,] m, bool[,] f, int ver)
        {
            int size = m.GetLength(0);
            for (int i = 0; i < size; i++) { Set(m, f, 6, i, i % 2 == 0); Set(m, f, i, 6, i % 2 == 0); }
            Finder(m, f, 3, 3);
            Finder(m, f, size - 4, 3);
            Finder(m, f, 3, size - 4);
            int[] pos = AlignmentPositions(ver);
            for (int i = 0; i < pos.Length; i++)
                for (int j = 0; j < pos.Length; j++)
                {
                    if ((i == 0 && j == 0) || (i == 0 && j == pos.Length - 1) || (i == pos.Length - 1 && j == 0)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            Set(m, f, pos[i] + dx, pos[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            DrawFormatBits(m, f, 0);   // reserve the area; real bits are drawn after masking
            if (ver >= 7)
            {
                int rem = ver;
                for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                long vbits = (long)ver << 12 | (uint)rem;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((vbits >> i) & 1) != 0;
                    int a = size - 11 + i % 3, b = i / 3;
                    Set(m, f, a, b, bit);
                    Set(m, f, b, a, bit);
                }
            }
        }

        static void Finder(bool[,] m, bool[,] f, int x, int y)
        {
            int size = m.GetLength(0);
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || xx >= size || yy < 0 || yy >= size) continue;
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(m, f, xx, yy, dist != 2 && dist != 4);
                }
        }

        static int[] AlignmentPositions(int ver)
        {
            if (ver == 1) return new int[0];
            int numAlign = ver / 7 + 2;
            int step = (ver * 4 + numAlign * 2 + 1) / (numAlign * 2 - 2) * 2;
            var result = new int[numAlign];
            result[0] = 6;
            for (int i = numAlign - 1, p = ver * 4 + 17 - 7; i >= 1; i--, p -= step) result[i] = p;
            return result;
        }

        static void DrawFormatBits(bool[,] m, bool[,] f, int mask)
        {
            int size = m.GetLength(0);
            int data = mask;   // level M has format bits 00, so only the mask remains
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            Func<int, bool> bit = i => ((bits >> i) & 1) != 0;
            for (int i = 0; i <= 5; i++) Set(m, f, 8, i, bit(i));
            Set(m, f, 8, 7, bit(6));
            Set(m, f, 8, 8, bit(7));
            Set(m, f, 7, 8, bit(8));
            for (int i = 9; i < 15; i++) Set(m, f, 14 - i, 8, bit(i));
            for (int i = 0; i < 8; i++) Set(m, f, size - 1 - i, 8, bit(i));
            for (int i = 8; i < 15; i++) Set(m, f, 8, size - 15 + i, bit(i));
            Set(m, f, 8, size - 8, true);   // the always-dark module
        }

        static void DrawCodewords(bool[,] m, bool[,] f, byte[] data)
        {
            int size = m.GetLength(0), i = 0;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? size - 1 - vert : vert;
                        if (!f[y, x] && i < data.Length * 8)
                        {
                            m[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                            i++;
                        }
                    }
            }
        }

        static void ApplyMask(bool[,] m, bool[,] f, int mask)
        {
            int size = m.GetLength(0);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (f[y, x]) continue;
                    bool inv;
                    switch (mask)
                    {
                        case 0: inv = (x + y) % 2 == 0; break;
                        case 1: inv = y % 2 == 0; break;
                        case 2: inv = x % 3 == 0; break;
                        case 3: inv = (x + y) % 3 == 0; break;
                        case 4: inv = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: inv = x * y % 2 + x * y % 3 == 0; break;
                        case 6: inv = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: inv = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (inv) m[y, x] = !m[y, x];
                }
        }

        // Standard penalty rules: long runs, 2x2 blocks, finder-like patterns, dark/light balance.
        static long Penalty(bool[,] m)
        {
            int size = m.GetLength(0);
            long result = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < size; a++)
                {
                    int run = 1;
                    for (int b = 1; b < size; b++)
                    {
                        bool cur = pass == 0 ? m[a, b] : m[b, a], prev = pass == 0 ? m[a, b - 1] : m[b - 1, a];
                        if (cur == prev) { run++; if (run == 5) result += 3; else if (run > 5) result++; }
                        else run = 1;
                    }
                    for (int b = 0; b + 10 < size; b++)
                    {
                        bool match1 = true, match2 = true;
                        bool[] pat = { true, false, true, true, true, false, true, false, false, false, false };
                        for (int k = 0; k < 11; k++)
                        {
                            bool v = pass == 0 ? m[a, b + k] : m[b + k, a];
                            if (v != pat[k]) match1 = false;
                            if (v != pat[10 - k]) match2 = false;
                        }
                        if (match1) result += 40;
                        if (match2) result += 40;
                    }
                }
            int dark = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (m[y, x]) dark++;
                    if (x < size - 1 && y < size - 1 && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1]) result += 3;
                }
            int total = size * size;
            int k2 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            result += Math.Max(0, k2) * 10;
            return result;
        }
    }
}
