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
        ReedSolomon_634717,
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

            if (eccType == ErrorCorrectionCodeType.ReedSolomon_634717)
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
            throw new NotImplementedException();
        }
    } // public static class ReedSolomonAlgorithm
} // namespace fnecore.EDAC.RS
