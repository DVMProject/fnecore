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

namespace fnecore.NXDN
{
    /// <summary>
    /// NXDN Message Type
    /// </summary>
    public enum NXDNMessageType : byte
    {
        /// <summary>
        /// Voice Call
        /// </summary>
        MESSAGE_TYPE_VCALL = 0x01,
        /// <summary>
        /// Voice Call - Individual
        /// </summary>
        MESSAGE_TYPE_VCALL_IV = 0x03,
        /// <summary>
        /// Data Call Header
        /// </summary>
        MESSAGE_TYPE_DCALL_HDR = 0x09,
        /// <summary>
        /// Data Call Header
        /// </summary>
        MESSAGE_TYPE_DCALL_DATA = 0x0B,
        /// <summary>
        /// Data Call Header
        /// </summary>
        MESSAGE_TYPE_DCALL_ACK = 0x0C,
        /// <summary>
        /// Transmit Release
        /// </summary>
        MESSAGE_TYPE_TX_REL = 0x08,
        /// <summary>
        /// Idle
        /// </summary>
        MESSAGE_TYPE_IDLE = 0x10,
    } // public enum NXDNMessageType : byte
} // namespace fnecore.NXDN
