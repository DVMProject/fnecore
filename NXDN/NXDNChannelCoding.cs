// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* Copyright (C) 2026 C. Lovell, Dev_Ranger
*/

using System;

namespace fnecore.NXDN
{
    /// <summary>
    /// Shares channel coding between SACCH and FACCH1. The decoder ignores
    /// punctured bits rather than counting them as received errors.
    /// </summary>
    internal static class NXDNChannelCoding
    {
        internal static int Bit(ReadOnlySpan<byte> data, int bit) => (data[bit >> 3] >> (7 - (bit & 7))) & 1;

        internal static void Put(Span<byte> data, int bit, int value)
        {
            int mask = 0x80 >> (bit & 7);
            data[bit >> 3] = (byte)((data[bit >> 3] & ~mask) | ((value & 1) != 0 ? mask : 0));
        }

        internal static void Copy(ReadOnlySpan<byte> source, int from, Span<byte> target, int to, int count)
        {
            for (int i = 0; i < count; i++) Put(target, to + i, Bit(source, from + i));
        }

        private static int Parity(int value)
        {
            value ^= value >> 4;
            value ^= value >> 2;
            return (value ^ (value >> 1)) & 1;
        }

        private static int Checksum(ReadOnlySpan<byte> data, int bits, int width)
        {
            int mask = (1 << width) - 1;
            int crc = mask;
            int polynomial = width == 6 ? 0x27 : 0x80F;
            for (int i = 0; i < bits; i++)
                crc = ((crc << 1) ^ (((crc >> (width - 1)) ^ Bit(data, i)) != 0 ? polynomial : 0)) & mask;
            return crc;
        }

        private static bool Punctured(int position, bool slow) => slow ? position % 6 == 5 : position % 4 == 1;
        private static int Interleave(int position, bool slow) => slow ? position % 12 * 5 + position / 12 : position % 16 * 9 + position / 16;

        internal static void Encode(ReadOnlySpan<byte> data, Span<byte> frame, int offset, bool slow)
        {
            int bits = slow ? 26 : 80;
            int width = slow ? 6 : 12;
            Span<byte> block = stackalloc byte[12];
            block.Clear();
            Copy(data, 0, block, 0, bits);
            int crc = Checksum(data, bits, width);
            for (int i = 0; i < width; i++) Put(block, bits + i, crc >> (width - 1 - i));
            int register = 0, output = 0;
            for (int i = 0; i < bits + width + 4; i++)
            {
                register = ((register << 1) | Bit(block, i)) & 31;
                for (int branch = 0; branch < 2; branch++)
                    if (!Punctured(i * 2 + branch, slow))
                        Put(frame, offset + Interleave(output++, slow), Parity(register & (branch == 0 ? 0x19 : 0x17)));
            }
        }

        internal static bool Decode(ReadOnlySpan<byte> frame, int offset, Span<byte> data, bool slow)
        {
            int bits = slow ? 26 : 80;
            int width = slow ? 6 : 12;
            int length = bits + width + 4;
            Span<int> metric = stackalloc int[16];
            Span<int> next = stackalloc int[16];
            Span<byte> previous = stackalloc byte[96 * 16];
            metric.Fill(10000);
            metric[0] = 0;
            int input = 0;
            for (int time = 0; time < length; time++)
            {
                int first = Punctured(time * 2, slow) ? -1 : Bit(frame, offset + Interleave(input++, slow));
                int second = Punctured(time * 2 + 1, slow) ? -1 : Bit(frame, offset + Interleave(input++, slow));
                next.Fill(10000);
                for (int state = 0; state < 16; state++)
                {
                    for (int bit = 0; bit < 2; bit++)
                    {
                        int register = state * 2 + bit;
                        int target = register & 15;
                        int distance = metric[state] +
                            (first >= 0 && first != Parity(register & 0x19) ? 1 : 0) +
                            (second >= 0 && second != Parity(register & 0x17) ? 1 : 0);
                        if (distance < next[target])
                        {
                            next[target] = distance;
                            previous[time * 16 + target] = (byte)state;
                        }
                    }
                }
                next.CopyTo(metric);
            }
            Span<byte> decoded = stackalloc byte[12];
            decoded.Clear();
            int trace = 0;
            for (int time = length - 1; time >= 0; time--)
            {
                Put(decoded, time, trace & 1);
                trace = previous[time * 16 + trace];
            }
            int received = 0;
            for (int i = 0; i < width; i++) received = received * 2 + Bit(decoded, bits + i);
            if (received != Checksum(decoded, bits, width)) return false;
            data.Clear();
            Copy(decoded, 0, data, 0, bits);
            return true;
        }
    }
}
