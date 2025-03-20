// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment Core Library
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2024 Caleb, K4PHP
*
*/

namespace fnecore.P25.LC.TSBK
{
    /// <summary>
    /// IOSP_ACK_RSP TSBK
    /// </summary>
    public class IOSP_ACK_RSP : TSBKBase
    {
        public uint DstId;
        public uint SrcId;
        public uint SysId;
        public uint Wacn;
        public bool Aiv;
        public bool ExtendedAddr;
        public byte Service;

        /// <summary>
        /// Creates an instance of <see cref="IOSP_ACK_RSP"/>
        /// </summary>
        /// <param name="dstId"></param>
        /// <param name="srcId"></param>
        /// <param name="aivFlag"></param>
        /// <param name="service"></param>
        public IOSP_ACK_RSP(uint dstId = 0, uint srcId = 0, bool aivFlag = false, byte service = 0x00)
        {
            DstId = dstId;
            SrcId = srcId;
            Aiv = aivFlag;
            Service = service;
            Lco = P25Defines.TSBK_IOSP_ACK_RSP;
        }

        /// <summary>
        /// Decode ACK_RSP TSBK
        /// </summary>
        /// <param name="data"></param>
        /// <param name="rawTSBK"></param>
        /// <returns></returns>
        public override bool Decode(byte[] data, bool rawTSBK = true)
        {
            if (!base.Decode(data, rawTSBK))
                return false;

            ulong tsbkValue = FneUtils.ToUInt64(Payload, 0);

            Aiv = ((tsbkValue >> 56) & 0x80U) == 0x80U;     // Additional Info Flag
            Service = (byte)((tsbkValue >> 56) & 0x3FU);    // Service Type

            SrcId = FneUtils.Bytes3ToUInt32(Payload, 3);       // Target Radio Address
            DstId = FneUtils.Bytes3ToUInt32(Payload, 0);       // Source Radio Address

            return true;
        }

        /// <summary>
        /// Encode ACK_RSP TSBK
        /// </summary>
        /// <param name="data"></param>
        /// <param name="payload"></param>
        /// <param name="rawTSBK"></param>
        /// <param name="noTrellis"></param>
        public override void Encode(ref byte[] data, bool rawTSBK = true, bool noTrellis = true)
        {
            ulong tsbkValue = 0;

            tsbkValue = Service & 0x3FU;                    // Service Type
            tsbkValue |= Aiv ? 0x80UL : 0x00UL;             // Additional Info Flag
            tsbkValue |= ExtendedAddr ? 0x40UL : 0x00UL;    // Extended Addressing Flag

            if (Aiv && ExtendedAddr)
            {
                tsbkValue = (tsbkValue << 20) + Wacn;       // Network ID
                tsbkValue = (tsbkValue << 12) + SysId;      // System ID
            }
            else
            {
                tsbkValue = (tsbkValue << 32) + SrcId;      // Target Radio Address
            }

            tsbkValue = (tsbkValue << 24) + DstId;          // Source Radio Address

            FneUtils.Memset(Payload, 0x00, Payload.Length);
            FneUtils.WriteBytes(tsbkValue, ref Payload, 0);

            base.Encode(ref data, rawTSBK, noTrellis);
        }
    } // public class IOSP_ACK_RSP
} // namespace fnecore.P25.LC.TSBK
