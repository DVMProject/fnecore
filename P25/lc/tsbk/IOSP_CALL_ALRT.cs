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
    /// IOSP_CALL_ALRT TSBK
    /// </summary>
    public class IOSP_CALL_ALRT : TSBKBase
    {
        public uint DstId;
        public uint SrcId;

        /// <summary>
        /// Creates an instance of <see cref="IOSP_CALL_ALRT"/>
        /// </summary>
        /// <param name="dstId"></param>
        /// <param name="srcId"></param>
        public IOSP_CALL_ALRT(uint dstId = 0, uint srcId = 0)
        {
            DstId = dstId;
            SrcId = srcId;
            Lco = P25Defines.TSBK_IOSP_CALL_ALRT;
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

            DstId = FneUtils.Bytes3ToUInt32(Payload, 3);       // Target Radio Address
            SrcId = FneUtils.Bytes3ToUInt32(Payload, 0);       // Source Radio Address

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
            tsbkValue = (tsbkValue << 40) + DstId;         // Target Radio Address
            tsbkValue = (tsbkValue << 24) + SrcId;         // Source Radio Address

            FneUtils.Memset(Payload, 0x00, Payload.Length);
            FneUtils.WriteBytes(tsbkValue, ref Payload, 0);

            base.Encode(ref data, rawTSBK, noTrellis);
        }
    } // public class IOSP_CALL_ALRT
} // namespace fnecore.P25.LC.TSBK
