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
    /// Represents an RTP header.
    /// </summary>
    public class RtpFNEHeader : RtpExtensionHeader
    {
        /// <summary>
        /// Traffic payload packet CRC-16.
        /// </summary>
        public ushort CRC { get; set; }

        /// <summary>
        /// Function.
        /// </summary>
        public byte Function { get; set; }
        
        /// <summary>
        /// Sub-function.
        /// </summary>
        public byte SubFunction { get; set; }

        /// <summary>
        /// Traffic Stream ID.
        /// </summary>
        public uint StreamID { get; set; }

        /// <summary>
        /// Traffic Peer ID.
        /// </summary>
        public uint PeerID { get; set; }

        /// <summary>
        /// Traffic Message Length.
        /// </summary>
        public uint MessageLength { get; set; }

        /*
        ** Methods
        */
        /// <summary>
        /// Initializes a new instance of the <see cref="RtpFNEHeader"/> class.
        /// </summary>
        /// <param name="offset"></param>
        public RtpFNEHeader(int offset = 12) : base(offset) // 12 bytes is the length of the RTP Header
        {
            CRC = 0;
            StreamID = 0;
            PeerID = 0;
            MessageLength = 0;
        }

        /// <summary>
        /// Decode a RTP header.
        /// </summary>
        /// <param name="data"></param>
        public override bool Decode(byte[] data)
        {
            if (data == null)
                return false;

            if (!base.Decode(data))
                return false;

            if (PayloadLength != Constants.RtpFNEHeaderExtLength)
                return false;
            if (PayloadType != Constants.DVMFrameStart)
                return false;

            CRC = (ushort)((data[4 + offset] << 8) | (data[5 + offset] << 0));  // CRC-16
            Function = data[6 + offset];                                        // Function
            SubFunction = data[7 + offset];                                     // Sub-Function

            StreamID = FneUtils.ToUInt32(data, 8 + offset);                     // Stream ID
            PeerID = FneUtils.ToUInt32(data, 12 + offset);                      // Peer ID
            MessageLength = FneUtils.ToUInt32(data, 16 + offset);               // Message Length

            return true;
        }

        /// <summary>
        /// Encode a RTP header.
        /// </summary>
        /// <param name="data"></param>
        public override void Encode(ref byte[] data)
        {
            if (data == null)
                return;

            PayloadType = Constants.DVMFrameStart;
            PayloadLength = Constants.RtpFNEHeaderExtLength;
            base.Encode(ref data);

            data[4 + offset] = (byte)((CRC >> 8) & 0xFFU);                      // CRC-16 MSB
            data[5 + offset] = (byte)((CRC >> 0) & 0xFFU);                      // CRC-16 LSB
            data[6 + offset] = Function;                                        // Function
            data[7 + offset] = SubFunction;                                     // Sub-Functon

            FneUtils.WriteBytes(StreamID, ref data, 8 + offset);                // Stream ID
            FneUtils.WriteBytes(PeerID, ref data, 12 + offset);                 // Peer ID
            FneUtils.WriteBytes(MessageLength, ref data, 16 + offset);          // Message Length
        }
    } // public class RtpFNEHeader : RtpExtensionHeader
} // namespace fnecore
