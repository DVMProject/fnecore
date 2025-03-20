// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment Core Library
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Caleb, K4PHP
*
*/

namespace fnecore.P25.KMM
{
    /// <summary>
    /// KMM Response Kind
    /// </summary>
    public enum KmmResponseKind
    {
        NONE = 0x00,
        DELAYED = 0x01,
        IMMEDIATE = 0x02
    }
} // namespace fnecore.P25.kmm
