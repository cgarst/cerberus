using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RoomAgent
{
    /// <summary>
    /// Minimal zero-dependency QR code generator (ISO/IEC 18004, Versions 1-10, EC Level M, Byte mode).
    /// </summary>
    internal static class QrCodeGenerator
    {
        private readonly struct VersionSpec
        {
            public readonly int Version;
            public readonly int TotalDataCodewords;
            public readonly int EccPerBlock;
            public readonly int Group1Blocks;
            public readonly int Group1DataCodewords;
            public readonly int Group2Blocks;
            public readonly int Group2DataCodewords;
            public readonly int[] AlignmentCenters;

            public VersionSpec(
                int version,
                int totalDataCodewords,
                int eccPerBlock,
                int group1Blocks,
                int group1DataCodewords,
                int group2Blocks,
                int group2DataCodewords,
                int[] alignmentCenters)
            {
                Version = version;
                TotalDataCodewords = totalDataCodewords;
                EccPerBlock = eccPerBlock;
                Group1Blocks = group1Blocks;
                Group1DataCodewords = group1DataCodewords;
                Group2Blocks = group2Blocks;
                Group2DataCodewords = group2DataCodewords;
                AlignmentCenters = alignmentCenters;
            }
        }

        // Error Correction Level M specifications for QR Versions 1 through 10.
        private static readonly VersionSpec[] Specs = new VersionSpec[]
        {
            new VersionSpec(1, 16, 10, 1, 16, 0, 0, Array.Empty<int>()),
            new VersionSpec(2, 28, 16, 1, 28, 0, 0, new[] { 6, 18 }),
            new VersionSpec(3, 44, 26, 1, 44, 0, 0, new[] { 6, 22 }),
            new VersionSpec(4, 64, 18, 2, 32, 0, 0, new[] { 6, 26 }),
            new VersionSpec(5, 86, 24, 2, 43, 0, 0, new[] { 6, 30 }),
            new VersionSpec(6, 108, 16, 4, 27, 0, 0, new[] { 6, 34 }),
            new VersionSpec(7, 124, 18, 4, 31, 0, 0, new[] { 6, 22, 38 }),
            new VersionSpec(8, 154, 22, 2, 38, 2, 39, new[] { 6, 24, 42 }),
            new VersionSpec(9, 182, 22, 3, 36, 2, 37, new[] { 6, 26, 46 }),
            new VersionSpec(10, 216, 26, 4, 43, 1, 44, new[] { 6, 28, 50 })
        };

        /// <summary>
        /// Generates a QR code boolean matrix (<c>true</c> = dark module) for the specified text.
        /// </summary>
        public static bool[,] Generate(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            byte[] payload = Encoding.UTF8.GetBytes(text);

            VersionSpec spec = SelectVersion(payload.Length);
            byte[] codewords = BuildInterleavedCodewords(payload, spec);

            int size = spec.Version * 4 + 17;
            bool[,] modules = new bool[size, size];
            bool[,] isFunction = new bool[size, size];

            DrawFunctionPatterns(modules, isFunction, spec);
            DrawCodewords(modules, isFunction, codewords);

            int bestMask = 0;
            int bestPenalty = int.MaxValue;
            bool[,] candidate = new bool[size, size];

            for (int mask = 0; mask < 8; mask++)
            {
                Array.Copy(modules, candidate, modules.Length);
                ApplyMask(candidate, isFunction, mask);
                DrawFormatBits(candidate, mask);
                int penalty = CalculatePenalty(candidate);
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    bestMask = mask;
                }
            }

            ApplyMask(modules, isFunction, bestMask);
            DrawFormatBits(modules, bestMask);
            return modules;
        }

        /// <summary>
        /// Renders a QR code for <paramref name="text"/> as a frozen black-on-white <see cref="BitmapSource"/>.
        /// </summary>
        public static BitmapSource CreateBitmapSource(string text, int pixelsPerModule = 8, int quietZoneModules = 4)
        {
            if (pixelsPerModule < 1) throw new ArgumentOutOfRangeException("pixelsPerModule");
            if (quietZoneModules < 0) throw new ArgumentOutOfRangeException("quietZoneModules");

            bool[,] matrix = Generate(text);
            int moduleCount = matrix.GetLength(0);
            int totalModules = moduleCount + quietZoneModules * 2;
            int pixelSize = totalModules * pixelsPerModule;
            byte[] pixels = new byte[pixelSize * pixelSize];

            // Fill white background
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = 255;
            }

            for (int my = 0; my < moduleCount; my++)
            {
                for (int mx = 0; mx < moduleCount; mx++)
                {
                    if (!matrix[my, mx]) continue;
                    int startY = (my + quietZoneModules) * pixelsPerModule;
                    int startX = (mx + quietZoneModules) * pixelsPerModule;
                    for (int dy = 0; dy < pixelsPerModule; dy++)
                    {
                        int rowOffset = (startY + dy) * pixelSize + startX;
                        for (int dx = 0; dx < pixelsPerModule; dx++)
                        {
                            pixels[rowOffset + dx] = 0;
                        }
                    }
                }
            }

            BitmapSource bitmap = BitmapSource.Create(
                pixelSize,
                pixelSize,
                96,
                96,
                PixelFormats.Gray8,
                null,
                pixels,
                pixelSize);
            bitmap.Freeze();
            return bitmap;
        }

        private static VersionSpec SelectVersion(int byteLength)
        {
            for (int i = 0; i < Specs.Length; i++)
            {
                VersionSpec spec = Specs[i];
                int charCountBits = spec.Version <= 9 ? 8 : 16;
                int requiredBits = 4 + charCountBits + byteLength * 8;
                if (requiredBits <= spec.TotalDataCodewords * 8)
                {
                    return spec;
                }
            }
            throw new ArgumentException("Input text is too long for QR versions 1-10 (EC Level M).");
        }

        private static byte[] BuildInterleavedCodewords(byte[] payload, VersionSpec spec)
        {
            List<int> bits = new List<int>(spec.TotalDataCodewords * 8);
            AppendBits(bits, 0b0100, 4); // Byte mode indicator
            int charCountBits = spec.Version <= 9 ? 8 : 16;
            AppendBits(bits, payload.Length, charCountBits);
            for (int i = 0; i < payload.Length; i++)
            {
                AppendBits(bits, payload[i], 8);
            }

            int capacityBits = spec.TotalDataCodewords * 8;
            int terminatorBits = Math.Min(4, capacityBits - bits.Count);
            AppendBits(bits, 0, terminatorBits);

            while (bits.Count % 8 != 0)
            {
                bits.Add(0);
            }

            byte[] dataBytes = new byte[spec.TotalDataCodewords];
            int byteCount = bits.Count / 8;
            for (int i = 0; i < byteCount; i++)
            {
                int val = 0;
                for (int b = 0; b < 8; b++)
                {
                    val = (val << 1) | bits[i * 8 + b];
                }
                dataBytes[i] = (byte)val;
            }

            for (int i = byteCount; i < dataBytes.Length; i++)
            {
                dataBytes[i] = (byte)(((i - byteCount) % 2 == 0) ? 0xEC : 0x11);
            }

            int totalBlocks = spec.Group1Blocks + spec.Group2Blocks;
            byte[][] dataBlocks = new byte[totalBlocks][];
            byte[][] eccBlocks = new byte[totalBlocks][];
            byte[] generator = ComputeReedSolomonDivisor(spec.EccPerBlock);

            int offset = 0;
            for (int b = 0; b < totalBlocks; b++)
            {
                int len = b < spec.Group1Blocks ? spec.Group1DataCodewords : spec.Group2DataCodewords;
                byte[] block = new byte[len];
                Array.Copy(dataBytes, offset, block, 0, len);
                offset += len;
                dataBlocks[b] = block;
                eccBlocks[b] = ComputeReedSolomonRemainder(block, generator);
            }

            List<byte> result = new List<byte>(spec.TotalDataCodewords + totalBlocks * spec.EccPerBlock);
            int maxDataLen = Math.Max(spec.Group1DataCodewords, spec.Group2DataCodewords);
            for (int i = 0; i < maxDataLen; i++)
            {
                for (int b = 0; b < totalBlocks; b++)
                {
                    if (i < dataBlocks[b].Length)
                    {
                        result.Add(dataBlocks[b][i]);
                    }
                }
            }

            for (int i = 0; i < spec.EccPerBlock; i++)
            {
                for (int b = 0; b < totalBlocks; b++)
                {
                    result.Add(eccBlocks[b][i]);
                }
            }

            return result.ToArray();
        }

        private static void AppendBits(List<int> bits, int value, int length)
        {
            for (int i = length - 1; i >= 0; i--)
            {
                bits.Add((value >> i) & 1);
            }
        }

        private static byte[] ComputeReedSolomonDivisor(int degree)
        {
            byte[] result = new byte[degree];
            result[degree - 1] = 1;
            byte root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = MultiplyGf256(result[j], root);
                    if (j + 1 < degree)
                    {
                        result[j] ^= result[j + 1];
                    }
                }
                root = MultiplyGf256(root, 0x02);
            }
            return result;
        }

        private static byte[] ComputeReedSolomonRemainder(byte[] data, byte[] divisor)
        {
            byte[] result = new byte[divisor.Length];
            for (int i = 0; i < data.Length; i++)
            {
                byte factor = (byte)(data[i] ^ result[0]);
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int j = 0; j < divisor.Length; j++)
                {
                    result[j] ^= MultiplyGf256(divisor[j], factor);
                }
            }
            return result;
        }

        private static byte MultiplyGf256(byte x, byte y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }

        private static void DrawFunctionPatterns(bool[,] modules, bool[,] isFunction, VersionSpec spec)
        {
            int size = modules.GetLength(0);

            // Timing patterns
            for (int i = 0; i < size; i++)
            {
                bool dark = (i % 2) == 0;
                SetFunctionModule(modules, isFunction, 6, i, dark);
                SetFunctionModule(modules, isFunction, i, 6, dark);
            }

            // Finder patterns + separators
            DrawFinderPattern(modules, isFunction, 3, 3);
            DrawFinderPattern(modules, isFunction, 3, size - 4);
            DrawFinderPattern(modules, isFunction, size - 4, 3);

            // Alignment patterns
            int[] centers = spec.AlignmentCenters;
            for (int i = 0; i < centers.Length; i++)
            {
                for (int j = 0; j < centers.Length; j++)
                {
                    int r = centers[i];
                    int c = centers[j];
                    if ((r <= 8 && c <= 8) || (r <= 8 && c >= size - 8) || (r >= size - 8 && c <= 8))
                    {
                        continue;
                    }
                    DrawAlignmentPattern(modules, isFunction, r, c);
                }
            }

            // Reserve format info areas (actual bits written after masking)
            for (int i = 0; i <= 8; i++)
            {
                if (i != 6)
                {
                    SetFunctionModule(modules, isFunction, 8, i, false);
                    SetFunctionModule(modules, isFunction, i, 8, false);
                }
            }
            for (int i = size - 8; i < size; i++)
            {
                SetFunctionModule(modules, isFunction, 8, i, false);
                SetFunctionModule(modules, isFunction, i, 8, false);
            }
            SetFunctionModule(modules, isFunction, size - 8, 8, true); // Dark module

            // Version info (Versions 7+)
            if (spec.Version >= 7)
            {
                int rem = spec.Version;
                for (int i = 0; i < 12; i++)
                {
                    rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                }
                int bits = (spec.Version << 12) | rem;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((bits >> i) & 1) != 0;
                    int a = size - 11 + (i % 3);
                    int b = i / 3;
                    SetFunctionModule(modules, isFunction, a, b, bit);
                    SetFunctionModule(modules, isFunction, b, a, bit);
                }
            }
        }

        private static void DrawFinderPattern(bool[,] modules, bool[,] isFunction, int centerRow, int centerCol)
        {
            int size = modules.GetLength(0);
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int r = centerRow + dy;
                    int c = centerCol + dx;
                    if (r < 0 || r >= size || c < 0 || c >= size) continue;
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    bool dark = dist != 2 && dist != 4;
                    SetFunctionModule(modules, isFunction, r, c, dark);
                }
            }
        }

        private static void DrawAlignmentPattern(bool[,] modules, bool[,] isFunction, int centerRow, int centerCol)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunctionModule(modules, isFunction, centerRow + dy, centerCol + dx, dist != 1);
                }
            }
        }

        private static void SetFunctionModule(bool[,] modules, bool[,] isFunction, int row, int col, bool dark)
        {
            modules[row, col] = dark;
            isFunction[row, col] = true;
        }

        private static void DrawCodewords(bool[,] modules, bool[,] isFunction, byte[] codewords)
        {
            int size = modules.GetLength(0);
            int bitIndex = 0;
            int totalBits = codewords.Length * 8;

            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < size; vert++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int col = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int row = upward ? (size - 1 - vert) : vert;
                        if (!isFunction[row, col] && bitIndex < totalBits)
                        {
                            bool bit = ((codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                            modules[row, col] = bit;
                            bitIndex++;
                        }
                    }
                }
            }
        }

        private static void ApplyMask(bool[,] modules, bool[,] isFunction, int mask)
        {
            int size = modules.GetLength(0);
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    if (isFunction[r, c]) continue;
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (r + c) % 2 == 0; break;
                        case 1: invert = r % 2 == 0; break;
                        case 2: invert = c % 3 == 0; break;
                        case 3: invert = (r + c) % 3 == 0; break;
                        case 4: invert = (r / 2 + c / 3) % 2 == 0; break;
                        case 5: invert = (r * c) % 2 + (r * c) % 3 == 0; break;
                        case 6: invert = ((r * c) % 2 + (r * c) % 3) % 2 == 0; break;
                        case 7: invert = ((r + c) % 2 + (r * c) % 3) % 2 == 0; break;
                        default: invert = false; break;
                    }
                    if (invert)
                    {
                        modules[r, c] = !modules[r, c];
                    }
                }
            }
        }

        private static void DrawFormatBits(bool[,] modules, int mask)
        {
            int size = modules.GetLength(0);
            // EC Level M = 00
            int data = (0b00 << 3) | mask;
            int rem = data;
            for (int i = 0; i < 10; i++)
            {
                rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            }
            int bits = ((data << 10) | rem) ^ 0x5412;

            for (int i = 0; i <= 5; i++)
            {
                modules[8, i] = ((bits >> i) & 1) != 0;
            }
            modules[8, 7] = ((bits >> 6) & 1) != 0;
            modules[8, 8] = ((bits >> 7) & 1) != 0;
            modules[7, 8] = ((bits >> 8) & 1) != 0;
            for (int i = 9; i < 15; i++)
            {
                modules[14 - i, 8] = ((bits >> i) & 1) != 0;
            }

            for (int i = 0; i < 8; i++)
            {
                modules[size - 1 - i, 8] = ((bits >> i) & 1) != 0;
            }
            for (int i = 8; i < 15; i++)
            {
                modules[8, size - 15 + i] = ((bits >> i) & 1) != 0;
            }
            modules[size - 8, 8] = true;
        }

        private static int CalculatePenalty(bool[,] modules)
        {
            int size = modules.GetLength(0);
            int penalty = 0;

            // Rule 1: 5+ consecutive same-color modules in rows and columns
            for (int r = 0; r < size; r++)
            {
                int runLen = 1;
                for (int c = 1; c < size; c++)
                {
                    if (modules[r, c] == modules[r, c - 1])
                    {
                        runLen++;
                    }
                    else
                    {
                        if (runLen >= 5) penalty += 3 + (runLen - 5);
                        runLen = 1;
                    }
                }
                if (runLen >= 5) penalty += 3 + (runLen - 5);
            }

            for (int c = 0; c < size; c++)
            {
                int runLen = 1;
                for (int r = 1; r < size; r++)
                {
                    if (modules[r, c] == modules[r - 1, c])
                    {
                        runLen++;
                    }
                    else
                    {
                        if (runLen >= 5) penalty += 3 + (runLen - 5);
                        runLen = 1;
                    }
                }
                if (runLen >= 5) penalty += 3 + (runLen - 5);
            }

            // Rule 2: 2x2 blocks of same color
            for (int r = 0; r < size - 1; r++)
            {
                for (int c = 0; c < size - 1; c++)
                {
                    bool color = modules[r, c];
                    if (modules[r, c + 1] == color && modules[r + 1, c] == color && modules[r + 1, c + 1] == color)
                    {
                        penalty += 3;
                    }
                }
            }

            // Rule 3: finder-like 1:1:3:1:1 patterns with 4 light modules on either side
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c <= size - 11; c++)
                {
                    if (MatchesFinderLikeRow(modules, r, c))
                    {
                        penalty += 40;
                    }
                }
            }
            for (int c = 0; c < size; c++)
            {
                for (int r = 0; r <= size - 11; r++)
                {
                    if (MatchesFinderLikeCol(modules, r, c))
                    {
                        penalty += 40;
                    }
                }
            }

            // Rule 4: balance of dark modules
            int darkCount = 0;
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    if (modules[r, c]) darkCount++;
                }
            }
            int total = size * size;
            int k = Math.Abs(darkCount * 20 - total * 10) / total;
            penalty += k * 10;

            return penalty;
        }

        private static bool MatchesFinderLikeRow(bool[,] m, int r, int c)
        {
            bool a =
                m[r, c] && !m[r, c + 1] && m[r, c + 2] && m[r, c + 3] && m[r, c + 4] && !m[r, c + 5] && m[r, c + 6] &&
                !m[r, c + 7] && !m[r, c + 8] && !m[r, c + 9] && !m[r, c + 10];
            bool b =
                !m[r, c] && !m[r, c + 1] && !m[r, c + 2] && !m[r, c + 3] &&
                m[r, c + 4] && !m[r, c + 5] && m[r, c + 6] && m[r, c + 7] && m[r, c + 8] && !m[r, c + 9] && m[r, c + 10];
            return a || b;
        }

        private static bool MatchesFinderLikeCol(bool[,] m, int r, int c)
        {
            bool a =
                m[r, c] && !m[r + 1, c] && m[r + 2, c] && m[r + 3, c] && m[r + 4, c] && !m[r + 5, c] && m[r + 6, c] &&
                !m[r + 7, c] && !m[r + 8, c] && !m[r + 9, c] && !m[r + 10, c];
            bool b =
                !m[r, c] && !m[r + 1, c] && !m[r + 2, c] && !m[r + 3, c] &&
                m[r + 4, c] && !m[r + 5, c] && m[r + 6, c] && m[r + 7, c] && m[r + 8, c] && !m[r + 9, c] && m[r + 10, c];
            return a || b;
        }
    }
}
