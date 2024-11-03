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
        public IOSP_CALL_ALRT(uint dstId, uint srcId)
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
        public bool Decode(byte[] data, bool rawTSBK)
        {
            byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];
            FneUtils.Memset(tsbk, 0x00, tsbk.Length);

            bool ret = base.Decode(data, ref tsbk, rawTSBK);
            if (!ret)
                return false;

            DstId = FneUtils.Bytes3ToUInt32(tsbk, 3);       // Target Radio Address
            SrcId = FneUtils.Bytes3ToUInt32(tsbk, 0);       // Source Radio Address

            return true;
        }

        /// <summary>
        /// Encode CALL_ALRT TSBK
        /// </summary>
        /// <param name="data"></param>
        /// <param name="payload"></param>
        /// <param name="rawTSBK"></param>
        /// <param name="noTrellis"></param>
        public override void Encode(ref byte[] data, ref byte[] payload, bool rawTSBK, bool noTrellis)
        {
            ulong tsbkValue = 0;
            tsbkValue = (tsbkValue << 40) + DstId;      // Target Radio Address
            tsbkValue = (tsbkValue << 24) + SrcId;      // Source Radio Address

            FneUtils.Memset(payload, 0x00, payload.Length);
            FneUtils.WriteBytes(tsbkValue, ref payload, 0);

            base.Encode(ref data, ref payload, rawTSBK, noTrellis);
        }
    } // public class IOSP_CALL_ALRT
} // namespace fnecore.P25.LC.TSBK
