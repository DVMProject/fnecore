/**
* Digital Voice Modem - Fixed Network Equipment
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment
*
*/
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
    /// DMR Data Types
    /// </summary>
    public enum DMRDataType : byte
    {
        /// <summary>
        /// Voice Privacy Indicator Header
        /// </summary>
        VOICE_PI_HEADER = 0x00,
        /// <summary>
        /// Voice Link Control Header
        /// </summary>
        VOICE_LC_HEADER = 0x01,
        /// <summary>
        /// Terminator with Link Control
        /// </summary>
        TERMINATOR_WITH_LC = 0x02,
        /// <summary>
        /// Control Signalling Block
        /// </summary>
        CSBK = 0x03,
        /// <summary>
        /// Data Header
        /// </summary>
        DATA_HEADER = 0x06,
        /// <summary>
        /// 1/2 Rate Data
        /// </summary>
        RATE_12_DATA = 0x07,
        /// <summary>
        /// 3/4 Rate Data
        /// </summary>
        RATE_34_DATA = 0x08,
        /// <summary>
        /// Idle Burst
        /// </summary>
        IDLE = 0x09,
        /// <summary>
        /// 1 Rate Data
        /// </summary>
        RATE_1_DATA = 0x0A,
    } // public enum DMRDataType : byte

    /// <summary>
    /// DMR Full-Link Opcodes
    /// </summary>
    public enum DMRFLCO : byte
    {
        /// <summary>
        /// GRP VCH USER - Group Voice Channel User
        /// </summary>
        FLCO_GROUP = 0x00,
        /// <summary>
        /// UU VCH USER - Unit-to-Unit Voice Channel User
        /// </summary>
        FLCO_PRIVATE = 0x01,
    } // public enum DMRFLCO : byte
} // namespace fnecore.DMR
