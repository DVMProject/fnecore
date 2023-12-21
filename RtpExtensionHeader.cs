/**
* Digital Voice Modem - Fixed Network Equipment
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment
*
*/
/*
*   Copyright (C) 2023 by Bryan Biedenkapp N2PLL
*
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

namespace fnecore
{
    /// <summary>
    /// 
    /// </summary>
    public class RtpExtensionHeader
    {
        protected int offset = 0;

        /// <summary>
        /// Format of the extension header payload contained within the packet.
        /// </summary>
        public ushort PayloadType { get; set; }

        /// <summary>
        /// Length of the extension header payload (in 32-bit units).
        /// </summary>
        public ushort PayloadLength { get; set; }

        /*
        ** Methods
        */
        /// <summary>
        /// Initializes a new instance of the <see cref="RtpExtensionHeader"/> class.
        /// </summary>
        /// <param name="offset"></param>
        public RtpExtensionHeader(int offset = 12) // 12 bytes is the length of the RTP Header
        {
            this.offset = offset;
            PayloadType = 0;
            PayloadLength = 0;
        }

        /// <summary>
        /// Decode a RTP header.
        /// </summary>
        /// <param name="data"></param>
        public virtual bool Decode(byte[] data)
        {
            if (data == null)
                return false;

            PayloadType = (ushort)((data[0 + offset] << 8) | (data[1 + offset] << 0));      // Payload Type
            PayloadLength = (ushort)((data[2 + offset] << 8) | (data[3 + offset] << 0));    // Payload Length

            return true;
        }

        /// <summary>
        /// Encode a RTP header.
        /// </summary>
        /// <param name="data"></param>
        public virtual void Encode(ref byte[] data)
        {
            if (data == null)
                return;

            data[0 + offset] = (byte)((PayloadType >> 8) & 0xFFU);              // Payload Type MSB
            data[1 + offset] = (byte)((PayloadType >> 0) & 0xFFU);              // Payload Type LSB
            data[2 + offset] = (byte)((PayloadLength >> 8) & 0xFFU);            // Payload Length MSB
            data[3 + offset] = (byte)((PayloadLength >> 0) & 0xFFU);            // Payload Length LSB
        }
    } // public class RtpExtensionHeader
} // namespace fnecore
