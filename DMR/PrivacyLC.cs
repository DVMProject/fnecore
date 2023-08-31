/**
* Digital Voice Modem - Fixed Network Equipment
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment
*
*/
//
// Based on code from the MMDVMHost project. (https://github.com/g4klx/MMDVMHost)
// Licensed under the GPLv2 License (https://opensource.org/licenses/GPL-2.0)
//
/*
*   Copyright (C) 2022 by Bryan Biedenkapp N2PLL
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

namespace fnecore.DMR
{
    /// <summary>
    /// Represents DMR privacy indicator link control data.
    /// </summary>
    public class PrivacyLC
    {
        private byte[] mi;

        /// <summary>
        /// Feature ID.
        /// </summary>
        public byte FID;

        /// <summary>
        /// Destination ID.
        /// </summary>
        public uint DstId;

        /** Service Options */
        /// <summary>
        /// Flag indicating a group/talkgroup operation.
        /// </summary>
        public bool Group;

        /** Encryption Data */
        /// <summary>
        /// Encryption algorithm ID.
        /// </summary>
        public byte AlgId;
        /// <summary>
        /// Encryption key ID.
        /// </summary>
        public uint KId;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="PrivacyLC"/> class.
        /// </summary>
        public PrivacyLC()
        {
            mi = new byte[4];

            FID = 0;

            DstId = 0;

            Group = false;

            AlgId = 0;
            KId = 0;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PrivacyLC"/> class.
        /// </summary>
        /// <param name="bytes"></param>
        public PrivacyLC(byte[] bytes)
        {
            mi = new byte[4];

            Group = (bytes[0U] & 0x20U) == 0x20U;
            AlgId = (byte)(bytes[0U] & 7);                                            // Algorithm ID

            FID = bytes[1U];
            KId = bytes[2U];

            mi[0U] = bytes[3U];
            mi[1U] = bytes[4U];
            mi[2U] = bytes[5U];
            mi[3U] = bytes[6U];

            DstId = (uint)(bytes[7U] << 16 | bytes[8U] << 8 | bytes[9U]);             // Destination Address
        }

        /// <summary>
        /// Gets LC data as bytes.
        /// </summary>
        /// <returns></returns>
        public byte[] GetBytes()
        {
            byte[] lcData = new byte[12];
            GetData(ref lcData);

            return lcData;
        }

        /// <summary>
        /// Gets LC data as bytes.
        /// </summary>
        /// <param name="bytes"></param>
        public void GetData(ref byte[] bytes)
        {
            if (bytes == null)
                throw new NullReferenceException("bytes");

            bytes[0U] = (byte)((Group ? 0x20U : 0x00U) +
                (AlgId & 0x07U));                                                      // Algorithm ID

            bytes[1U] = FID;
            bytes[2U] = (byte)KId;

            bytes[3U] = mi[0U];
            bytes[4U] = mi[1U];
            bytes[5U] = mi[2U];
            bytes[6U] = mi[3U];

            bytes[7U] = (byte)(DstId >> 16);                                          // Destination Address
            bytes[8U] = (byte)(DstId >> 8);                                           // ..
            bytes[9U] = (byte)(DstId >> 0);                                           // ..
        }
    } // public class PrivacyLC
} // namespace fnecore.DMR
