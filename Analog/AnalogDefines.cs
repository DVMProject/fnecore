// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment Core Library
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Bryan Biedenkapp, N2PLL
*
*/

using System;

namespace fnecore.Analog
{
    /// <summary>
    /// Audio Frame Type(s)
    /// </summary>
    public enum AudioFrameType : byte
    {
        /// <summary>
        /// Voice Start Frame
        /// </summary>
        VOICE_START = 0x00,
        /// <summary>
        /// Voice
        /// </summary>
        VOICE = 0x01,
        /// <summary>
        /// Voice End Frame / Call Terminator
        /// </summary>
        TERMINATOR = 0x02
    } // public enum AudioFrameType : byte
} // namespace fnecore.Analog
