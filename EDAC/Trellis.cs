// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment Core Library
* @derivedfrom MMDVMHost (https://github.com/g4klx/MMDVMHost)
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2023,2026 Bryan Biedenkapp, N2PLL
*
*/

using System;

namespace fnecore.EDAC
{
    /// <summary>
    /// Implements 1/2 rate and 3/4 rate Trellis for DMR/P25.
    /// </summary>
    public sealed class Trellis
    {
        public const int TRELLIS_34_MAX_LIST_PATHS = 8;
        public const int TRELLIS_34_PAYLOAD_LENGTH_BYTES = 18;

        private const int TRELLIS_STEPS = 49;
        private const int TRELLIS_34_STATES = 8;
        private const int TRELLIS_12_STATES = 4;

        private static readonly uint[] INTERLEAVE_TABLE = new uint[98] {
            0, 1, 8,   9, 16, 17, 24, 25, 32, 33, 40, 41, 48, 49, 56, 57, 64, 65, 72, 73, 80, 81, 88, 89, 96, 97,
            2, 3, 10, 11, 18, 19, 26, 27, 34, 35, 42, 43, 50, 51, 58, 59, 66, 67, 74, 75, 82, 83, 90, 91,
            4, 5, 12, 13, 20, 21, 28, 29, 36, 37, 44, 45, 52, 53, 60, 61, 68, 69, 76, 77, 84, 85, 92, 93,
            6, 7, 14, 15, 22, 23, 30, 31, 38, 39, 46, 47, 54, 55, 62, 63, 70, 71, 78, 79, 86, 87, 94, 95
        };

        private static readonly byte[] ENCODE_TABLE_34 = new byte[64] {
            0,  8, 4, 12, 2, 10, 6, 14,
            4, 12, 2, 10, 6, 14, 0,  8,
            1,  9, 5, 13, 3, 11, 7, 15,
            5, 13, 3, 11, 7, 15, 1,  9,
            3, 11, 7, 15, 1,  9, 5, 13,
            7, 15, 1,  9, 5, 13, 3, 11,
            2, 10, 6, 14, 0,  8, 4, 12,
            6, 14, 0,  8, 4, 12, 2, 10
        };

        private static readonly byte[] ENCODE_TABLE_12 = new byte[16] {
            0,  15, 12,  3,
            4,  11,  8,  7,
            13,  2,  1, 14,
            9,   6,  5, 10
        };

        private static readonly int[,] POINT_DIBITS = new int[16, 2] {
            { +1, -1 }, { -1, -1 }, { +3, -3 }, { -3, -3 }, { -3, -1 }, { +3, -1 }, { -1, -3 }, { +1, -3 },
            { -3, +3 }, { +3, +3 }, { -1, +1 }, { +1, +1 }, { +1, +3 }, { -1, +3 }, { +3, +1 }, { -3, +1 }
        };

        private struct ListSurvivor
        {
            public ushort Metric;
            public byte PreviousState;
            public byte PreviousRank;
            public bool Truncated;
        }

        private struct Survivor
        {
            public ushort Metric;
            public byte PreviousState;
        }

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="Trellis"/> class.
        /// </summary>
        public Trellis()
        {
            /* stub */
        }

        /// <summary>
        /// Decodes 3/4 rate Trellis.
        /// </summary>
        /// <param name="data">Trellis symbol bytes.</param>
        /// <param name="payload">Output bytes.</param>
        /// <returns>True, if decoded, otherwise false.</returns>
        public bool Decode34(byte[] data, ref byte[] payload)
        {
            if (data == null)
                throw new NullReferenceException("data");
            if (payload == null)
                throw new NullReferenceException("payload");

            bool truncated;
            return Decode34List(data, ref payload, 1, out truncated) == 1;
        }

        /// <summary>
        /// Decodes 3/4 rate Trellis and returns candidates tied for the best path metric.
        /// </summary>
        /// <param name="data">Trellis symbol bytes.</param>
        /// <param name="payloads">Contiguous output buffer containing 18 bytes per candidate.</param>
        /// <param name="maxPaths">Maximum number of candidates to return, from one through eight.</param>
        /// <param name="truncated">True when additional equally likely candidates were omitted.</param>
        /// <returns>Number of candidates written, or zero when <paramref name="maxPaths"/> is invalid.</returns>
        public int Decode34List(byte[] data, ref byte[] payloads, int maxPaths, out bool truncated)
        {
            if (data == null)
                throw new NullReferenceException("data");
            if (payloads == null)
                throw new NullReferenceException("payloads");

            truncated = false;
            if (maxPaths < 1 || maxPaths > TRELLIS_34_MAX_LIST_PATHS)
                return 0;
            if (payloads.Length < maxPaths * TRELLIS_34_PAYLOAD_LENGTH_BYTES)
                throw new ArgumentException("Output buffer is too small for the requested candidates.", "payloads");

            int[] dibits = new int[98];
            Deinterleave(data, ref dibits);

            ushort[] metrics = new ushort[TRELLIS_34_MAX_LIST_PATHS];
            int count = Viterbi34(dibits, payloads, metrics, maxPaths, out truncated);
            int tied = 0;
            while (tied < count && metrics[tied] == metrics[0])
                tied++;

            return tied;
        }

        /// <summary>
        /// Encodes 3/4 rate Trellis.
        /// </summary>
        /// <param name="payload">Input bytes.</param>
        /// <param name="data">Trellis symbol bytes.</param>
        public void Encode34(byte[] payload, ref byte[] data)
        {
            if (data == null)
                throw new NullReferenceException("data");
            if (payload == null)
                throw new NullReferenceException("payload");

            byte[] tribits = new byte[49U];
            BitsToTribits(payload, ref tribits);

            byte[] points = new byte[49U];
            byte state = 0;

            for (uint i = 0U; i < 49U; i++)
            {
                byte tribit = tribits[i];

                points[i] = ENCODE_TABLE_34[state * 8U + tribit];

                state = tribit;
            }

            int[] dibits = new int[98U];
            PointsToDibits(points, ref dibits);

            Interleave(dibits, ref data);
        }

        /// <summary>
        /// Decodes 1/2 rate Trellis.
        /// </summary>
        /// <param name="data">Trellis symbol bytes.</param>
        /// <param name="payload">Output bytes.</param>
        /// <returns>True, if decoded, otherwise false.</returns>
        public bool Decode12(byte[] data, ref byte[] payload)
        {
            if (data == null)
                throw new NullReferenceException("data");
            if (payload == null)
                throw new NullReferenceException("payload");

            int[] dibits = new int[98];
            Deinterleave(data, ref dibits);
            return Viterbi12(dibits, ref payload);
        }

        /// <summary>
        /// Encodes 1/2 rate Trellis.
        /// </summary>
        /// <param name="payload">Input bytes.</param>
        /// <param name="data">Trellis symbol bytes.</param>
        public void Encode12(byte[] payload, ref byte[] data)
        {
            if (data == null)
                throw new NullReferenceException("data");
            if (payload == null)
                throw new NullReferenceException("payload");

            byte[] bits = new byte[49U];
            BitsToDibits(payload, ref bits);

            byte[] points = new byte[49U];
            byte state = 0;

            for (uint i = 0U; i < 49U; i++)
            {
                byte bit = bits[i];

                points[i] = ENCODE_TABLE_12[state * 4U + bit];

                state = bit;
            }

            int[] dibits = new int[98U];
            PointsToDibits(points, ref dibits);

            Interleave(dibits, ref data);
        }

        /// <summary>
        /// Helper to deinterleave the input symbols into dibits.
        /// </summary>
        /// <param name="data">Trellis symbol bytes.</param>
        /// <param name="dibits">Dibits.</param>
        private void Deinterleave(byte[] data, ref int[] dibits)
        {
            for (uint i = 0U; i < 98U; i++) {
                uint n = i * 2U + 0U;
                bool b1 = FneUtils.ReadBit(data, n) != false;

                n = i * 2U + 1U;
                bool b2 = FneUtils.ReadBit(data, n) != false;

                int dibit;
                if (!b1 && b2)
                    dibit = +3;
                else if (!b1 && !b2)
                    dibit = +1;
                else if (b1 && !b2)
                    dibit = -1;
                else
                    dibit = -3;

                n = INTERLEAVE_TABLE[i];
                dibits[n] = dibit;
            }
        }

        /// <summary>
        /// Helper to interleave the input dibits into symbols.
        /// </summary>
        /// <param name="dibits">Dibits.</param>
        /// <param name="data">Trellis symbol bytes.</param>
        private void Interleave(int[] dibits, ref byte[] data)
        {
            for (uint i = 0U; i < 98U; i++) {
                uint n = INTERLEAVE_TABLE[i];

                bool b1, b2;
                switch (dibits[n])
                {
                    case +3:
                        b1 = false;
                        b2 = true;
                        break;
                    case +1:
                        b1 = false;
                        b2 = false;
                        break;
                    case -1:
                        b1 = true;
                        b2 = false;
                        break;
                    default:
                        b1 = true;
                        b2 = true;
                        break;
                }

                n = i * 2U + 0U;
                FneUtils.WriteBit(ref data, n, b1);

                n = i * 2U + 1U;
                FneUtils.WriteBit(ref data, n, b2);
            }
        }

        /// <summary>
        /// Helper to map dibits to 4FSK constellation points.
        /// </summary>
        /// <param name="dibits">Dibits.</param>
        /// <param name="points">4FSK constellation points.</param>
        private void DibitsToPoints(int[] dibits, ref byte[] points)
        {
            for (uint i = 0U; i < 49U; i++) {
                if (dibits[i * 2U + 0U] == +1 && dibits[i * 2U + 1U] == -1)
                    points[i] = 0;
                else if (dibits[i * 2U + 0U] == -1 && dibits[i * 2U + 1U] == -1)
                    points[i] = 1;
                else if (dibits[i * 2U + 0U] == +3 && dibits[i * 2U + 1U] == -3)
                    points[i] = 2;
                else if (dibits[i * 2U + 0U] == -3 && dibits[i * 2U + 1U] == -3)
                    points[i] = 3;
                else if (dibits[i * 2U + 0U] == -3 && dibits[i * 2U + 1U] == -1)
                    points[i] = 4;
                else if (dibits[i * 2U + 0U] == +3 && dibits[i * 2U + 1U] == -1)
                    points[i] = 5;
                else if (dibits[i * 2U + 0U] == -1 && dibits[i * 2U + 1U] == -3)
                    points[i] = 6;
                else if (dibits[i * 2U + 0U] == +1 && dibits[i * 2U + 1U] == -3)
                    points[i] = 7;
                else if (dibits[i * 2U + 0U] == -3 && dibits[i * 2U + 1U] == +3)
                    points[i] = 8;
                else if (dibits[i * 2U + 0U] == +3 && dibits[i * 2U + 1U] == +3)
                    points[i] = 9;
                else if (dibits[i * 2U + 0U] == -1 && dibits[i * 2U + 1U] == +1)
                    points[i] = 10;
                else if (dibits[i * 2U + 0U] == +1 && dibits[i * 2U + 1U] == +1)
                    points[i] = 11;
                else if (dibits[i * 2U + 0U] == +1 && dibits[i * 2U + 1U] == +3)
                    points[i] = 12;
                else if (dibits[i * 2U + 0U] == -1 && dibits[i * 2U + 1U] == +3)
                    points[i] = 13;
                else if (dibits[i * 2U + 0U] == +3 && dibits[i * 2U + 1U] == +1)
                    points[i] = 14;
                else if (dibits[i * 2U + 0U] == -3 && dibits[i * 2U + 1U] == +1)
                    points[i] = 15;
            }
        }

        /// <summary>
        /// Helper to map 4FSK constellation points to dibits.
        /// </summary>
        /// <param name="points">4FSK Constellation points.</param>
        /// <param name="dibits">Dibits.</param>
        private void PointsToDibits(byte[] points, ref int[] dibits)
        {
            for (uint i = 0U; i < 49U; i++) {
                switch (points[i])
                {
                    case 0:
                        dibits[i * 2U + 0U] = +1;
                        dibits[i * 2U + 1U] = -1;
                        break;
                    case 1:
                        dibits[i * 2U + 0U] = -1;
                        dibits[i * 2U + 1U] = -1;
                        break;
                    case 2:
                        dibits[i * 2U + 0U] = +3;
                        dibits[i * 2U + 1U] = -3;
                        break;
                    case 3:
                        dibits[i * 2U + 0U] = -3;
                        dibits[i * 2U + 1U] = -3;
                        break;
                    case 4:
                        dibits[i * 2U + 0U] = -3;
                        dibits[i * 2U + 1U] = -1;
                        break;
                    case 5:
                        dibits[i * 2U + 0U] = +3;
                        dibits[i * 2U + 1U] = -1;
                        break;
                    case 6:
                        dibits[i * 2U + 0U] = -1;
                        dibits[i * 2U + 1U] = -3;
                        break;
                    case 7:
                        dibits[i * 2U + 0U] = +1;
                        dibits[i * 2U + 1U] = -3;
                        break;
                    case 8:
                        dibits[i * 2U + 0U] = -3;
                        dibits[i * 2U + 1U] = +3;
                        break;
                    case 9:
                        dibits[i * 2U + 0U] = +3;
                        dibits[i * 2U + 1U] = +3;
                        break;
                    case 10:
                        dibits[i * 2U + 0U] = -1;
                        dibits[i * 2U + 1U] = +1;
                        break;
                    case 11:
                        dibits[i * 2U + 0U] = +1;
                        dibits[i * 2U + 1U] = +1;
                        break;
                    case 12:
                        dibits[i * 2U + 0U] = +1;
                        dibits[i * 2U + 1U] = +3;
                        break;
                    case 13:
                        dibits[i * 2U + 0U] = -1;
                        dibits[i * 2U + 1U] = +3;
                        break;
                    case 14:
                        dibits[i * 2U + 0U] = +3;
                        dibits[i * 2U + 1U] = +1;
                        break;
                    default:
                        dibits[i * 2U + 0U] = -3;
                        dibits[i * 2U + 1U] = +1;
                        break;
                }
            }
        }

        /// <summary>
        /// Helper to convert a byte payload into tribits.
        /// </summary>
        /// <param name="payload">Byte payload.</param>
        /// <param name="tribits">Tribits.</param>
        private void BitsToTribits(byte[] payload, ref byte[] tribits)
        {
            for (uint i = 0U; i < 48U; i++) {
                uint n = i * 3U;

                bool b1 = FneUtils.ReadBit(payload, n) != false;
                n++;
                bool b2 = FneUtils.ReadBit(payload, n) != false;
                n++;
                bool b3 = FneUtils.ReadBit(payload, n) != false;

                byte tribit = 0;
                tribit |= (byte)(b1 ? 4U : 0U);
                tribit |= (byte)(b2 ? 2U : 0U);
                tribit |= (byte)(b3 ? 1U : 0U);

                tribits[i] = tribit;
            }

            tribits[48U] = 0;
        }

        /// <summary>
        /// Helper to convert a byte payload into dibits.
        /// </summary>
        /// <param name="payload">Byte payload.</param>
        /// <param name="dibits">Dibits.</param>
        private void BitsToDibits(byte[] payload, ref byte[] dibits)
        {
            for (uint i = 0U; i < 48U; i++) {
                uint n = i * 2U;

                bool b1 = FneUtils.ReadBit(payload, n) != false;
                n++;
                bool b2 = FneUtils.ReadBit(payload, n) != false;

                byte dibit = 0;
                dibit |= (byte)(b1 ? 2U : 0U);
                dibit |= (byte)(b2 ? 1U : 0U);

                dibits[i] = dibit;
            }

            dibits[48U] = 0;
        }

        /// <summary>
        /// Helper to convert tribits into a byte payload.
        /// </summary>
        /// <param name="tribits">Tribits.</param>
        /// <param name="payload">Byte payload.</param>
        private void TribitsToBits(byte[] tribits, ref byte[] payload)
        {
            for (uint i = 0U; i < 48U; i++) {
                byte tribit = tribits[i];

                bool b1 = (tribit & 0x04U) == 0x04U;
                bool b2 = (tribit & 0x02U) == 0x02U;
                bool b3 = (tribit & 0x01U) == 0x01U;

                uint n = i * 3U;

                FneUtils.WriteBit(ref payload, n, b1);
                n++;
                FneUtils.WriteBit(ref payload, n, b2);
                n++;
                FneUtils.WriteBit(ref payload, n, b3);
            }
        }

        /// <summary>
        /// Helper to convert tribits into a byte payload.
        /// </summary>
        /// <param name="dibits">Dibits.</param>
        /// <param name="payload">Byte payload.</param>
        private void DibitsToBits(byte[] dibits, ref byte[] payload)
        {
            for (uint i = 0U; i < 48U; i++) {
                byte dibit = dibits[i];

                bool b1 = (dibit & 0x02U) == 0x02U;
                bool b2 = (dibit & 0x01U) == 0x01U;

                uint n = i * 2U;

                FneUtils.WriteBit(ref payload, n, b1);
                n++;
                FneUtils.WriteBit(ref payload, n, b2);
            }
        }

        /// <summary>
        /// Runs a bounded list Viterbi decode of the 3/4 rate Trellis.
        /// </summary>
        private int Viterbi34(int[] dibits, byte[] payloads, ushort[] metrics, int maxPaths, out bool truncated)
        {
            ListSurvivor[,,] trace = new ListSurvivor[TRELLIS_STEPS, TRELLIS_34_STATES, TRELLIS_34_MAX_LIST_PATHS];
            byte[,] traceCount = new byte[TRELLIS_STEPS, TRELLIS_34_STATES];
            ListSurvivor[,] current = new ListSurvivor[TRELLIS_34_STATES, TRELLIS_34_MAX_LIST_PATHS];
            byte[] currentCount = new byte[TRELLIS_34_STATES];
            current[0, 0].Metric = 0;
            currentCount[0] = 1;

            for (int step = 0; step < TRELLIS_STEPS; step++)
            {
                int rx0 = dibits[step * 2];
                int rx1 = dibits[step * 2 + 1];

                for (int next = 0; next < TRELLIS_34_STATES; next++)
                {
                    int bestCount = 0;
                    for (int state = 0; state < TRELLIS_34_STATES; state++)
                    {
                        byte point = ENCODE_TABLE_34[state * TRELLIS_34_STATES + next];
                        int branch = (Math.Abs(POINT_DIBITS[point, 0] - rx0) + Math.Abs(POINT_DIBITS[point, 1] - rx1)) / 2;

                        for (int rank = 0; rank < currentCount[state]; rank++)
                        {
                            ushort metric = (ushort)(current[state, rank].Metric + branch);
                            if (bestCount == maxPaths && metric >= trace[step, next, bestCount - 1].Metric)
                            {
                                if (metric == trace[step, next, bestCount - 1].Metric)
                                {
                                    for (int retained = 0; retained < bestCount; retained++)
                                    {
                                        if (trace[step, next, retained].Metric == metric)
                                        {
                                            ListSurvivor survivor = trace[step, next, retained];
                                            survivor.Truncated = true;
                                            trace[step, next, retained] = survivor;
                                        }
                                    }
                                }
                                continue;
                            }

                            int position = bestCount < maxPaths ? bestCount++ : bestCount - 1;
                            while (position > 0 && trace[step, next, position - 1].Metric > metric)
                            {
                                trace[step, next, position] = trace[step, next, position - 1];
                                position--;
                            }

                            trace[step, next, position] = new ListSurvivor {
                                Metric = metric,
                                PreviousState = (byte)state,
                                PreviousRank = (byte)rank,
                                Truncated = current[state, rank].Truncated
                            };
                        }
                    }

                    traceCount[step, next] = (byte)bestCount;
                }

                for (int state = 0; state < TRELLIS_34_STATES; state++)
                {
                    currentCount[state] = traceCount[step, state];
                    for (int rank = 0; rank < currentCount[state]; rank++)
                        current[state, rank] = trace[step, state, rank];
                }
            }

            truncated = false;
            int count = traceCount[TRELLIS_STEPS - 1, 0];
            ushort bestMetric = trace[TRELLIS_STEPS - 1, 0, 0].Metric;
            for (int path = 0; path < count; path++)
            {
                byte[] tribits = new byte[TRELLIS_STEPS];
                byte state = 0;
                byte rank = (byte)path;
                metrics[path] = trace[TRELLIS_STEPS - 1, 0, path].Metric;
                if (metrics[path] == bestMetric && trace[TRELLIS_STEPS - 1, 0, path].Truncated)
                    truncated = true;

                for (int step = TRELLIS_STEPS - 1; step >= 0; step--)
                {
                    ListSurvivor survivor = trace[step, state, rank];
                    tribits[step] = state;
                    state = survivor.PreviousState;
                    rank = survivor.PreviousRank;
                }

                byte[] payload = new byte[TRELLIS_34_PAYLOAD_LENGTH_BYTES];
                TribitsToBits(tribits, ref payload);
                Buffer.BlockCopy(payload, 0, payloads, path * TRELLIS_34_PAYLOAD_LENGTH_BYTES, payload.Length);
            }

            return count;
        }

        /// <summary>
        /// Runs a single-best Viterbi decode of the 1/2 rate Trellis.
        /// </summary>
        private bool Viterbi12(int[] dibits, ref byte[] payload)
        {
            Survivor[,] trace = new Survivor[TRELLIS_STEPS, TRELLIS_12_STATES];
            ushort[] currentMetric = new ushort[] { 0, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue };

            for (int step = 0; step < TRELLIS_STEPS; step++)
            {
                int rx0 = dibits[step * 2];
                int rx1 = dibits[step * 2 + 1];

                for (int next = 0; next < TRELLIS_12_STATES; next++)
                {
                    ushort bestMetric = ushort.MaxValue;
                    byte bestState = 0;
                    for (int state = 0; state < TRELLIS_12_STATES; state++)
                    {
                        if (currentMetric[state] == ushort.MaxValue)
                            continue;

                        byte point = ENCODE_TABLE_12[state * TRELLIS_12_STATES + next];
                        int branch = (Math.Abs(POINT_DIBITS[point, 0] - rx0) + Math.Abs(POINT_DIBITS[point, 1] - rx1)) / 2;
                        ushort candidate = (ushort)(currentMetric[state] + branch);
                        if (candidate < bestMetric)
                        {
                            bestMetric = candidate;
                            bestState = (byte)state;
                        }
                    }

                    trace[step, next] = new Survivor { Metric = bestMetric, PreviousState = bestState };
                }

                for (int state = 0; state < TRELLIS_12_STATES; state++)
                    currentMetric[state] = trace[step, state].Metric;
            }

            if (trace[TRELLIS_STEPS - 1, 0].Metric == ushort.MaxValue)
                return false;

            byte[] decoded = new byte[TRELLIS_STEPS];
            byte tracebackState = 0;
            for (int step = TRELLIS_STEPS - 1; step >= 0; step--)
            {
                decoded[step] = tracebackState;
                tracebackState = trace[step, tracebackState].PreviousState;
            }

            DibitsToBits(decoded, ref payload);
            return true;
        }
    } // public sealed class Trellis
} // namespace fnecore.EDAC
