/**
* Digital Voice Modem - Fixed Network Equipment
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment
*
*/
/*
*   This program is free software: you can redistribute it and/or modify
*   it under the terms of the GNU Affero General Public License as published by
*   the Free Software Foundation, either version 3 of the License, or
*   (at your option) any later version.
*
*   This program is distributed in the hope that it will be useful,
*   but WITHOUT ANY WARRANTY; without even the implied warranty of
*   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
*   GNU Affero General Public License for more details.
*/

using System;

namespace fnecore.EDAC.RS
{
    /// <summary>
    /// 
    /// </summary>
    public enum ErrorCorrectionCodeType
    {
        /// <summary>
        /// (63,47,17)
        /// </summary>
        ReedSolomon_362017,
        /// <summary>
        /// (24,12,13)
        /// </summary>
        ReedSolomon_241213,
        /// <summary>
        /// (24,16,9)
        /// </summary>
        ReedSolomon_24169
    } // public enum ErrorCorrectionCodeType

    /// <summary>
    /// 
    /// </summary>
    public static class ReedSolomonAlgorithm
    {
        /*
        ** Methods
        */

        /// <summary>
        /// Produces error correction codewords for a message using the Reed-Solomon algorithm.
        /// </summary>
        /// <param name="message">The message to compute the error correction codewords.</param>
        /// <param name="eccType">The type of Galois field to use to encode error correction codewords.</param>
        /// <returns>Returns the computed message with error correction codewords.</returns>
        public static byte[] Encode(byte[] message, ErrorCorrectionCodeType eccType)
        {
            ReedSolomonEncoder reedSolomonEncoder = new ReedSolomonEncoder();
            byte[] output = new byte[message.Length];
            Buffer.BlockCopy(message, 0, output, 0, message.Length);

            if (eccType == ErrorCorrectionCodeType.ReedSolomon_362017)
            {
                reedSolomonEncoder.encode362017(ref output);
                return output;
            }
            else if (eccType == ErrorCorrectionCodeType.ReedSolomon_241213)
            {
                reedSolomonEncoder.encode241213(ref output);
                return output;
            }
            else if (eccType == ErrorCorrectionCodeType.ReedSolomon_24169)
            {
                reedSolomonEncoder.encode24169(ref output);
                return output;
            }
            else
                throw new ArgumentException($"Invalid '{nameof(eccType)}' argument.", nameof(eccType));
        }

        /// <summary>
        /// Repairs a possibly broken message using the Reed-Solomon algorithm.
        /// </summary>
        /// <param name="message">The possibly broken message to repair.</param>
        /// <param name="eccType">The type of Galois field to use to decode message.</param>
        /// <returns>Returns the repaired message, or null if it cannot be repaired.</returns>
        public static byte[] Decode(byte[] message, ErrorCorrectionCodeType eccType)
        {
            byte[] codeword = new byte[63];

            if (eccType == ErrorCorrectionCodeType.ReedSolomon_362017)
            {
                uint offset = 0U;
                for (uint i = 0U; i < 36U; i++, offset += 6)
                    codeword[27 + i] = bin2Hex(message, offset);

                ReedSolomonDecoder rs362017 = new ReedSolomonDecoder(64, 47, 16, 0x43);
                byte[] codewordEC = rs362017.DecodeEx(codeword);

                byte[] messageOut = new byte[message.Length];
                offset = 0U;
                for (uint i = 0U; i < 20U; i++, offset += 6)
                    hex2Bin(codewordEC[27 + i], ref messageOut, offset);
                return messageOut;
            }
            else if (eccType == ErrorCorrectionCodeType.ReedSolomon_241213)
            {
                uint offset = 0U;
                for (uint i = 0U; i < 24U; i++, offset += 6)
                    codeword[39 + i] = bin2Hex(message, offset);

                ReedSolomonDecoder rs241213 = new ReedSolomonDecoder(64, 51, 12, 0x43);
                byte[] codewordEC = rs241213.DecodeEx(codeword);

                byte[] messageOut = new byte[message.Length];
                offset = 0U;
                for (uint i = 0U; i < 12U; i++, offset += 6)
                    hex2Bin(codewordEC[39 + i], ref messageOut, offset);
                return messageOut;
            }
            else if (eccType == ErrorCorrectionCodeType.ReedSolomon_24169)
            {
                uint offset = 0U;
                for (uint i = 0U; i < 24U; i++, offset += 6)
                    codeword[39 + i] = bin2Hex(message, offset);

                ReedSolomonDecoder rs24169 = new ReedSolomonDecoder(64, 55, 8, 0x43);
                byte[] codewordEC = rs24169.DecodeEx(codeword);

                byte[] messageOut = new byte[message.Length];
                offset = 0U;
                for (uint i = 0U; i < 16U; i++, offset += 6)
                    hex2Bin(codewordEC[39 + i], ref messageOut, offset);
                return messageOut;
            }
            else
                throw new ArgumentException($"Invalid '{nameof(eccType)}' argument.", nameof(eccType));
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="input"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        internal static byte bin2Hex(byte[] input, uint offset)
        {
            byte output = 0x00;

            output |= (byte)(FneUtils.ReadBit(input, offset + 0U) ? 0x20U : 0x00U);
            output |= (byte)(FneUtils.ReadBit(input, offset + 1U) ? 0x10U : 0x00U);
            output |= (byte)(FneUtils.ReadBit(input, offset + 2U) ? 0x08U : 0x00U);
            output |= (byte)(FneUtils.ReadBit(input, offset + 3U) ? 0x04U : 0x00U);
            output |= (byte)(FneUtils.ReadBit(input, offset + 4U) ? 0x02U : 0x00U);
            output |= (byte)(FneUtils.ReadBit(input, offset + 5U) ? 0x01U : 0x00U);

            return output;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="input"></param>
        /// <param name="output"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        internal static void hex2Bin(byte input, ref byte[] output, uint offset)
        {
            FneUtils.WriteBit(ref output, offset + 0U, (input & 0x20U) == 0x20U);
            FneUtils.WriteBit(ref output, offset + 1U, (input & 0x10U) == 0x10U);
            FneUtils.WriteBit(ref output, offset + 2U, (input & 0x08U) == 0x08U);
            FneUtils.WriteBit(ref output, offset + 3U, (input & 0x04U) == 0x04U);
            FneUtils.WriteBit(ref output, offset + 4U, (input & 0x02U) == 0x02U);
            FneUtils.WriteBit(ref output, offset + 5U, (input & 0x01U) == 0x01U);
        }
    } // public static class ReedSolomonAlgorithm
} // namespace fnecore.EDAC.RS
