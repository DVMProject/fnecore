// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment Core Library
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/

using System;

namespace fnecore.NXDN.LC
{
    /// <summary>Encodes and decodes voice call, initialization vector, and release messages.</summary>
    public sealed class RTCH
    {
        public NXDNMessageType MessageType = NXDNMessageType.MESSAGE_TYPE_VCALL;
        public ushort SrcId;
        public ushort DstId;
        public byte CallType = 1;
        public bool Emergency;
        public bool Priority;
        public bool Duplex;
        public byte TransmissionMode;
        public byte CipherType;
        public byte KeyId;
        public ulong MessageIndicator;

        /// <summary>Decodes raw link control without modifying state on invalid input.</summary>
        public bool Decode(ReadOnlySpan<byte> data)
        {
            if (data.Length < 9) return false;
            byte type = (byte)(data[0] & 0x3F);
            if (type != 1 && type != 3 && type != 7 && type != 8 && type != 0x10) return false;
            MessageType = (NXDNMessageType)type;
            SrcId = DstId = 0;
            CipherType = KeyId = TransmissionMode = 0;
            Emergency = Priority = Duplex = false;
            MessageIndicator = 0;
            CallType = 1;
            if (type == 3)
                MessageIndicator = NXDNCrypto.ReadIndicator(data.Slice(1, 8));
            else if (type != 0x10)
            {
                Emergency = (data[1] & 0x80) != 0;
                Priority = (data[1] & 0x20) != 0;
                CallType = (byte)(data[2] >> 5);
                SrcId = (ushort)(data[3] * 256 + data[4]);
                DstId = (ushort)(data[5] * 256 + data[6]);
                if (type == 1)
                {
                    Duplex = (data[2] & 0x10) != 0;
                    TransmissionMode = (byte)(data[2] & 7);
                    CipherType = (byte)(data[7] >> 6);
                    KeyId = (byte)(data[7] & 63);
                }
            }
            return true;
        }

        /// <summary>Encodes the raw message for SACCH or FACCH1 channel coding.</summary>
        public void Encode(Span<byte> data)
        {
            if (data.Length < 9) throw new ArgumentException("Link control needs nine bytes.", nameof(data));
            if (CallType > 7 || TransmissionMode > 7 || CipherType > 3 || KeyId > 63)
                throw new ArgumentOutOfRangeException(nameof(data), "Invalid link control field.");
            byte type = (byte)MessageType;
            if (type != 1 && type != 3 && type != 7 && type != 8 && type != 0x10)
                throw new ArgumentOutOfRangeException(nameof(MessageType));
            data.Clear();
            data[0] = type;
            if (type == 3)
                NXDNCrypto.WriteIndicator(MessageIndicator, data.Slice(1, 8));
            else if (type != 0x10)
            {
                data[1] = (byte)((Emergency ? 0x80 : 0) | (Priority ? 0x20 : 0));
                data[2] = (byte)(CallType << 5);
                data[3] = (byte)(SrcId >> 8);
                data[4] = (byte)SrcId;
                data[5] = (byte)(DstId >> 8);
                data[6] = (byte)DstId;
                if (type == 1)
                {
                    data[2] |= (byte)((Duplex ? 0x10 : 0) | TransmissionMode);
                    data[7] = (byte)((CipherType << 6) | KeyId);
                }
            }
        }
    }
}
