// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment Core Library
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 Bryan Biedenkapp, N2PLL
*
*/

namespace fnecore.P25.LC.TSBK
{
    /// <summary>
    /// OSP_DVM_LC_CALL_TERM TSBK
    /// </summary>
    public class OSP_DVM_LC_CALL_TERM : TSBKBase
    {
        public byte GrpVchId;
        public uint GrpVchNo;
        public uint DstId;
        public uint SrcId;

        /// <summary>
        /// Creates an instance of <see cref="OSP_DVM_LC_CALL_TERM"/>
        /// </summary>
        /// <param name="dstId"></param>
        /// <param name="srcId"></param>
        public OSP_DVM_LC_CALL_TERM(uint dstId = 0, uint srcId = 0)
        {
            DstId = dstId;
            SrcId = srcId;
            Lco = P25Defines.LC_CALL_TERM;
        }

        /// <summary>
        /// Decode CALL_ALRT TSBK
        /// </summary>
        /// <param name="data"></param>
        /// <param name="rawTSBK"></param>
        /// <returns></returns>
        public override bool Decode(byte[] data, bool rawTSBK = true)
        {
            if (!base.Decode(data, rawTSBK))
                return false;

            ulong tsbkValue = FneUtils.ToUInt64(Payload, 0);

            GrpVchId = (byte)((tsbkValue >> 52) & 0x0F);    // Channel ID
            GrpVchNo = (uint)((tsbkValue >> 40) & 0xFFF);   // Channel Number
            DstId = (uint)((tsbkValue >> 24) & 0xFFFF);     // Target Radio Address
            SrcId = (uint)(tsbkValue & 0xFFFFFF);           // Source Radio Address

            return true;
        }

        /// <summary>
        /// Encode CALL_ALRT TSBK
        /// </summary>
        /// <param name="data"></param>
        /// <param name="payload"></param>
        /// <param name="rawTSBK"></param>
        /// <param name="noTrellis"></param>
        public override void Encode(ref byte[] data, bool rawTSBK = true, bool noTrellis = true)
        {
            ulong tsbkValue = 0;
            tsbkValue = (tsbkValue << 4) + GrpVchNo;        // Channel ID
            tsbkValue = (tsbkValue << 12) + GrpVchId;       // Channel Number
            tsbkValue = (tsbkValue << 16) + DstId;          // Target Radio Address
            tsbkValue = (tsbkValue << 24) + SrcId;          // Source Radio Address

            FneUtils.Memset(Payload, 0x00, Payload.Length);
            FneUtils.WriteBytes(tsbkValue, ref Payload, 0);

            base.Encode(ref data, rawTSBK, noTrellis);
        }
    } // public class OSP_DVM_LC_CALL_TERM
} // namespace fnecore.P25.LC.TSBK
