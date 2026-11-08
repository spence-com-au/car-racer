using System;
using System.Collections.Generic;
using System.Text;

namespace CarRacer.Core.Platform
{
    public enum QrErrorCorrection
    {
        Low = 0,
        Medium = 1,
        Quartile = 2,
        High = 3,
    }

    /// <summary>
    /// A QR code (ISO/IEC 18004) for a short text, in byte mode, versions 1–10 (up to 271 bytes at
    /// Low): enough for the join link the host shows (design doc §1.1). Builds the module grid;
    /// drawing it is up to the caller. Follows the structure of Project Nayuki's reference QR
    /// generator; tests compare it with an independent encoder.
    /// </summary>
    public sealed class QrCode
    {
        public const int MinVersion = 1;
        public const int MaxVersion = 10;

        // Per error correction level (L, M, Q, H) and version (1..10).
        private static readonly int[,] EccCodewordsPerBlock =
        {
            { 7, 10, 15, 20, 26, 18, 20, 24, 30, 18 },
            { 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 },
            { 13, 22, 18, 26, 18, 24, 18, 22, 20, 24 },
            { 17, 28, 22, 16, 22, 28, 26, 26, 24, 28 },
        };

        private static readonly int[,] ErrorCorrectionBlocks =
        {
            { 1, 1, 1, 1, 1, 2, 2, 2, 2, 4 },
            { 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 },
            { 1, 1, 2, 2, 4, 4, 6, 6, 8, 8 },
            { 1, 1, 2, 4, 4, 4, 5, 6, 8, 8 },
        };

        // The format bits' 2-bit code for each level, which isn't in level order.
        private static readonly int[] FormatBitsForLevel = { 1, 0, 3, 2 };

        private readonly bool[,] _modules;
        private readonly bool[,] _isFunction;

        private QrCode(int version, QrErrorCorrection level)
        {
            Version = version;
            ErrorCorrection = level;
            Size = version * 4 + 17;
            _modules = new bool[Size, Size];
            _isFunction = new bool[Size, Size];
        }

        public int Version { get; }
        public QrErrorCorrection ErrorCorrection { get; }
        public int Mask { get; private set; }

        /// <summary>Modules along each side, not counting the quiet zone (4 modules of white all round) a reader needs.</summary>
        public int Size { get; }

        /// <summary>Whether the module at column <paramref name="x"/>, row <paramref name="y"/> is dark.</summary>
        public bool IsDark(int x, int y) => x >= 0 && x < Size && y >= 0 && y < Size && _modules[y, x];

        /// <summary>
        /// Encodes <paramref name="text"/> as UTF-8 in the smallest version that holds it at
        /// <paramref name="level"/>. <paramref name="mask"/> picks a mask pattern (0–7); by default
        /// the one with the lowest penalty is used, as the standard asks.
        /// </summary>
        public static QrCode Encode(string text, QrErrorCorrection level = QrErrorCorrection.Medium, int mask = -1)
        {
            if (mask < -1 || mask > 7)
            {
                throw new ArgumentOutOfRangeException(nameof(mask), mask, "A mask pattern is 0–7, or -1 for the automatic choice.");
            }

            byte[] data = Encoding.UTF8.GetBytes(text ?? string.Empty);
            int version = MinVersion;
            while (DataCapacityBytes(version, level) < data.Length)
            {
                version++;
                if (version > MaxVersion)
                {
                    throw new ArgumentException($"{data.Length} bytes is too long for a version {MaxVersion} QR code at {level}.", nameof(text));
                }
            }

            var code = new QrCode(version, level);
            code.DrawFunctionPatterns();
            code.DrawCodewords(code.AddEccAndInterleave(code.DataCodewords(data)));
            code.ChooseMask(mask);
            return code;
        }

        /// <summary>How many bytes of text fit in a version at a level, in byte mode.</summary>
        public static int DataCapacityBytes(int version, QrErrorCorrection level)
        {
            int dataBits = DataCodewordCount(version, level) * 8;
            int headerBits = 4 + CharacterCountBits(version);
            return (dataBits - headerBits) / 8;
        }

        private static int CharacterCountBits(int version) => version < 10 ? 8 : 16;

        private static int RawDataModules(int version)
        {
            int result = (16 * version + 128) * version + 64;
            if (version >= 2)
            {
                int alignment = version / 7 + 2;
                result -= (25 * alignment - 10) * alignment - 55;
                if (version >= 7)
                {
                    result -= 36;
                }
            }

            return result;
        }

        private static int DataCodewordCount(int version, QrErrorCorrection level) =>
            RawDataModules(version) / 8 - EccCodewordsPerBlock[(int)level, version - 1] * ErrorCorrectionBlocks[(int)level, version - 1];

        /// <summary>The byte-mode segment, terminator and padding, as data codewords.</summary>
        private byte[] DataCodewords(byte[] data)
        {
            var bits = new List<bool>();
            AppendBits(bits, 0b0100, 4);
            AppendBits(bits, data.Length, CharacterCountBits(Version));
            foreach (byte b in data)
            {
                AppendBits(bits, b, 8);
            }

            int capacity = DataCodewordCount(Version, ErrorCorrection) * 8;
            AppendBits(bits, 0, Math.Min(4, capacity - bits.Count));
            AppendBits(bits, 0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11)
            {
                AppendBits(bits, pad, 8);
            }

            var codewords = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++)
            {
                if (bits[i])
                {
                    codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));
                }
            }

            return codewords;
        }

        private static void AppendBits(List<bool> bits, int value, int length)
        {
            for (int i = length - 1; i >= 0; i--)
            {
                bits.Add(((value >> i) & 1) != 0);
            }
        }

        /// <summary>Splits the data into blocks, adds each block's Reed–Solomon codewords, and interleaves them.</summary>
        private byte[] AddEccAndInterleave(byte[] data)
        {
            int blocks = ErrorCorrectionBlocks[(int)ErrorCorrection, Version - 1];
            int eccLength = EccCodewordsPerBlock[(int)ErrorCorrection, Version - 1];
            int rawCodewords = RawDataModules(Version) / 8;
            int shortBlocks = blocks - rawCodewords % blocks;
            int shortBlockLength = rawCodewords / blocks;

            byte[] divisor = ReedSolomonDivisor(eccLength);
            var blockData = new List<byte[]>();
            for (int i = 0, k = 0; i < blocks; i++)
            {
                int dataLength = shortBlockLength - eccLength + (i < shortBlocks ? 0 : 1);
                var block = new byte[shortBlockLength + 1];
                Array.Copy(data, k, block, 0, dataLength);
                k += dataLength;
                byte[] ecc = ReedSolomonRemainder(block, dataLength, divisor);
                Array.Copy(ecc, 0, block, shortBlockLength + 1 - eccLength, eccLength);
                blockData.Add(block);
            }

            var result = new List<byte>(rawCodewords);
            for (int i = 0; i < shortBlockLength + 1; i++)
            {
                for (int j = 0; j < blocks; j++)
                {
                    // Short blocks have no codeword at the position where long blocks have their last data byte.
                    if (i != shortBlockLength - eccLength || j >= shortBlocks)
                    {
                        result.Add(blockData[j][i]);
                    }
                }
            }

            return result.ToArray();
        }

        private static byte[] ReedSolomonDivisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = Multiply(result[j], root);
                    if (j + 1 < degree)
                    {
                        result[j] ^= result[j + 1];
                    }
                }

                root = Multiply(root, 0x02);
            }

            return result;
        }

        private static byte[] ReedSolomonRemainder(byte[] block, int dataLength, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            for (int i = 0; i < dataLength; i++)
            {
                int factor = block[i] ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int j = 0; j < result.Length; j++)
                {
                    result[j] ^= Multiply(divisor[j], factor);
                }
            }

            return result;
        }

        /// <summary>Multiplication in GF(2^8) modulo x^8 + x^4 + x^3 + x^2 + 1.</summary>
        private static byte Multiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }

            return (byte)z;
        }

        private void DrawFunctionPatterns()
        {
            for (int i = 0; i < Size; i++)
            {
                SetFunction(6, i, i % 2 == 0);
                SetFunction(i, 6, i % 2 == 0);
            }

            DrawFinder(3, 3);
            DrawFinder(Size - 4, 3);
            DrawFinder(3, Size - 4);

            int[] alignment = AlignmentPositions();
            int count = alignment.Length;
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    // Not where the finders are.
                    if (!(i == 0 && j == 0 || i == 0 && j == count - 1 || i == count - 1 && j == 0))
                    {
                        DrawAlignment(alignment[i], alignment[j]);
                    }
                }
            }

            DrawFormatBits(0);
            DrawVersion();
        }

        private void DrawFinder(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int xx = x + dx;
                    int yy = y + dy;
                    if (xx >= 0 && xx < Size && yy >= 0 && yy < Size)
                    {
                        SetFunction(xx, yy, distance != 2 && distance != 4);
                    }
                }
            }
        }

        private void DrawAlignment(int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            }
        }

        private int[] AlignmentPositions()
        {
            if (Version == 1)
            {
                return new int[0];
            }

            int count = Version / 7 + 2;
            int step = (Version * 8 + count * 3 + 5) / (count * 4 - 4) * 2;
            var result = new int[count];
            result[0] = 6;
            for (int i = count - 1, position = Size - 7; i >= 1; i--, position -= step)
            {
                result[i] = position;
            }

            return result;
        }

        private void DrawFormatBits(int mask)
        {
            int data = FormatBitsForLevel[(int)ErrorCorrection] << 3 | mask;
            int remainder = data;
            for (int i = 0; i < 10; i++)
            {
                remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
            }

            int bits = (data << 10 | remainder) ^ 0x5412;
            for (int i = 0; i <= 5; i++)
            {
                SetFunction(8, i, Bit(bits, i));
            }

            SetFunction(8, 7, Bit(bits, 6));
            SetFunction(8, 8, Bit(bits, 7));
            SetFunction(7, 8, Bit(bits, 8));
            for (int i = 9; i < 15; i++)
            {
                SetFunction(14 - i, 8, Bit(bits, i));
            }

            for (int i = 0; i < 8; i++)
            {
                SetFunction(Size - 1 - i, 8, Bit(bits, i));
            }

            for (int i = 8; i < 15; i++)
            {
                SetFunction(8, Size - 15 + i, Bit(bits, i));
            }

            SetFunction(8, Size - 8, true);
        }

        private void DrawVersion()
        {
            if (Version < 7)
            {
                return;
            }

            int remainder = Version;
            for (int i = 0; i < 12; i++)
            {
                remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1F25);
            }

            int bits = Version << 12 | remainder;
            for (int i = 0; i < 18; i++)
            {
                bool bit = Bit(bits, i);
                int a = Size - 11 + i % 3;
                int b = i / 3;
                SetFunction(a, b, bit);
                SetFunction(b, a, bit);
            }
        }

        private static bool Bit(int value, int i) => ((value >> i) & 1) != 0;

        private void SetFunction(int x, int y, bool dark)
        {
            _modules[y, x] = dark;
            _isFunction[y, x] = true;
        }

        /// <summary>Lays the codewords out in the zigzag, two columns at a time from the bottom right.</summary>
        private void DrawCodewords(byte[] codewords)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                {
                    right = 5;
                }

                for (int vertical = 0; vertical < Size; vertical++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vertical : vertical;
                        if (!_isFunction[y, x] && i < codewords.Length * 8)
                        {
                            _modules[y, x] = Bit(codewords[i >> 3], 7 - (i & 7));
                            i++;
                        }
                    }
                }
            }
        }

        private void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }

                    _modules[y, x] ^= invert && !_isFunction[y, x];
                }
            }
        }

        private void ChooseMask(int mask)
        {
            if (mask < 0)
            {
                int best = int.MaxValue;
                for (int candidate = 0; candidate < 8; candidate++)
                {
                    ApplyMask(candidate);
                    DrawFormatBits(candidate);
                    int penalty = Penalty();
                    if (penalty < best)
                    {
                        mask = candidate;
                        best = penalty;
                    }

                    ApplyMask(candidate);
                }
            }

            Mask = mask;
            ApplyMask(mask);
            DrawFormatBits(mask);
        }

        /// <summary>The standard's penalty score: runs, 2×2 blocks, finder-like patterns and dark/light balance.</summary>
        private int Penalty()
        {
            const int n1 = 3, n2 = 3, n3 = 40, n4 = 10;
            int result = 0;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int a = 0; a < Size; a++)
                {
                    bool runColour = false;
                    int run = 0;
                    var history = new int[7];
                    for (int b = 0; b < Size; b++)
                    {
                        bool dark = pass == 0 ? _modules[a, b] : _modules[b, a];
                        if (dark == runColour)
                        {
                            run++;
                            if (run == 5)
                            {
                                result += n1;
                            }
                            else if (run > 5)
                            {
                                result++;
                            }
                        }
                        else
                        {
                            AddHistory(run, history);
                            if (!runColour)
                            {
                                result += CountFinderLike(history) * n3;
                            }

                            runColour = dark;
                            run = 1;
                        }
                    }

                    result += TerminateAndCount(runColour, run, history) * n3;
                }
            }

            for (int y = 0; y < Size - 1; y++)
            {
                for (int x = 0; x < Size - 1; x++)
                {
                    bool colour = _modules[y, x];
                    if (colour == _modules[y, x + 1] && colour == _modules[y + 1, x] && colour == _modules[y + 1, x + 1])
                    {
                        result += n2;
                    }
                }
            }

            int darkCount = 0;
            foreach (bool dark in _modules)
            {
                if (dark)
                {
                    darkCount++;
                }
            }

            int total = Size * Size;
            int k = (Math.Abs(darkCount * 20 - total * 10) + total - 1) / total - 1;
            result += k * n4;
            return result;

            void AddHistory(int length, int[] history)
            {
                if (history[0] == 0)
                {
                    // The light area before the first run counts as part of the quiet zone.
                    length += Size;
                }

                Array.Copy(history, 0, history, 1, history.Length - 1);
                history[0] = length;
            }

            int CountFinderLike(int[] history)
            {
                int unit = history[1];
                bool core = unit > 0 && history[2] == unit && history[3] == unit * 3 && history[4] == unit && history[5] == unit;
                return (core && history[0] >= unit * 4 && history[6] >= unit ? 1 : 0)
                    + (core && history[6] >= unit * 4 && history[0] >= unit ? 1 : 0);
            }

            int TerminateAndCount(bool colour, int run, int[] history)
            {
                if (colour)
                {
                    AddHistory(run, history);
                    run = 0;
                }

                run += Size;
                AddHistory(run, history);
                return CountFinderLike(history);
            }
        }
    }
}
