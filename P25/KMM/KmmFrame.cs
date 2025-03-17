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

using System;

namespace fnecore.P25.kmm
{
    /// <summary>
    /// KMM Frame base class
    /// </summary>
    public class KmmFrame
    {
        public byte MessageId { get; set; }
        public ushort MessageLength { get; set; }
        public byte RespKind { get; private set; } = 0;
        public bool Complete { get; private set; } = true;
        public ushort DstLlId { get; private set; }
        public ushort SrcLlId { get; private set; }

        /// <summary>
        /// Creates an instance of <see cref="KmmFrame"/>
        /// </summary>
        public KmmFrame() { /* sub */ }

        /// <summary>
        /// KMM Frame length
        /// </summary>
        public virtual ushort Length => P25Defines.KMM_FRAME_LENGTH;

        /// <summary>
        /// Encode KMM Frame Header
        /// </summary>
        /// <param name="data"></param>
        /// <exception cref="ArgumentException"></exception>
        protected void EncodeHeader(byte[] data)
        {
            if (data.Length < 8)
                throw new ArgumentException("Data buffer too small");

            data[0] = MessageId;
            FneUtils.WriteBytes(Length, ref data, 1);

            data[3] = (byte)((RespKind << 6) | (Complete ? 0x00 : 0x01));
            FneUtils.WriteBytes(DstLlId, ref data, 4);
            FneUtils.WriteBytes(SrcLlId, ref data, 6);
        }

        /// <summary>
        /// Decode KMM Frame Header
        /// </summary>
        /// <param name="data"></param>
        /// <exception cref="ArgumentException"></exception>
        protected void DecodeHeader(byte[] data)
        {
            if (data.Length < 8)
                throw new ArgumentException("Data buffer too small");

            MessageId = data[0];
            MessageLength = FneUtils.ToUInt16(data, 1);
            RespKind = (byte)((data[3] >> 6) & 0x03);
            Complete = (data[3] & 0x01) == 0;
            DstLlId = FneUtils.ToUInt16(data, 4);
            SrcLlId = FneUtils.ToUInt16(data, 6);
        }
    } // public class KmmFrame
} // namespace fnecore.P25.kmm