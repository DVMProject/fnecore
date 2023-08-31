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
*   Copyright (C) 2022-2023 by Bryan Biedenkapp N2PLL
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

using fnecore.EDAC;

namespace fnecore.DMR
{
    /// <summary>
    /// Represents DMR embedded signalling.
    /// </summary>
    public class EMB
    {
        /// <summary>
        /// DMR access color code.
        /// </summary>
        public byte ColorCode;

        /// <summary>
        /// Flag indicating whether the privacy indicator is set or not.
        /// </summary>
        public bool PI;

        /// <summary>
        /// Link control start/stop.
        /// </summary>
        public byte LCSS;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="EMB"/> class.
        /// </summary>
        public EMB()
        {
            ColorCode = 0;
            PI = false;
            LCSS = 0;
        }

        /// <summary>
        /// Decodes DMR embedded signalling data.
        /// </summary>
        /// <param name="data"></param>
        public void Decode(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            byte[] DMREMB = new byte[2U];
            DMREMB[0U] = (byte)((data[13U] << 4) & 0xF0U);
            DMREMB[0U] |= (byte)((data[14U] >> 4) & 0x0FU);
            DMREMB[1U] = (byte)((data[18U] << 4) & 0xF0U);
            DMREMB[1U] |= (byte)((data[19U] >> 4) & 0x0FU);

            // decode QR (16,7,6) FEC
            QR1676.Decode(DMREMB);

            ColorCode = (byte)((DMREMB[0U] >> 4) & 0x0FU);
            PI = (DMREMB[0U] & 0x08U) == 0x08U;
            LCSS = (byte)((DMREMB[0U] >> 1) & 0x03U);
        }

        /// <summary>
        /// Encodes DMR embedded signalling data.
        /// </summary>
        /// <param name="data"></param>
        public void Encode(ref byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            byte[] DMREMB = new byte[2U];
            DMREMB[0U] = (byte)((ColorCode << 4) & 0xF0U);
            DMREMB[0U] |= (byte)(PI ? 0x08U : 0x00U);
            DMREMB[0U] |= (byte)((LCSS << 1) & 0x06U);
            DMREMB[1U] = 0x00;

            // encode QR (16,7,6) FEC
            QR1676.Encode(ref DMREMB);

            data[13U] = (byte)((data[13U] & 0xF0U) | ((DMREMB[0U] >> 4) & 0x0FU));
            data[14U] = (byte)((data[14U] & 0x0FU) | ((DMREMB[0U] << 4) & 0xF0U));
            data[18U] = (byte)((data[18U] & 0xF0U) | ((DMREMB[1U] >> 4) & 0x0FU));
            data[19U] = (byte)((data[19U] & 0x0FU) | ((DMREMB[1U] << 4) & 0xF0U));
        }
    } // public class EMB
} // namespace fnecore.DMR
