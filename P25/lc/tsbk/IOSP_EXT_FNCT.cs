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

namespace fnecore.P25.LC.TSBK
{
    /// <summary>
    /// IOSP_EXT_FNCT TSBK
    /// </summary>
    public class IOSP_EXT_FNCT : TSBKBase
    {
        public ushort ExtendedFunction { get; set; }
        public uint SrcId { get; set; }
        public uint DstId { get; set; }

        /// <summary>
        /// Creates an instance of <see cref="IOSP_EXT_FNCT"/>
        /// </summary>
        /// <param name="extendedFunction"></param>
        /// <param name="srcId"></param>
        /// <param name="dstId"></param>
        public IOSP_EXT_FNCT(ushort extendedFunction = 0, uint srcId = 0, uint dstId = 0)
        {
            ExtendedFunction = extendedFunction;
            SrcId = srcId;
            DstId = dstId;
            Lco = P25Defines.TSBK_IOSP_EXT_FNCT;
        }

        /// <summary>
        /// Decode EXT_FNCT TSBK
        /// </summary>
        /// <param name="data"></param>
        /// <param name="rawTSBK"></param>
        /// <returns></returns>
        public override bool Decode(byte[] data, bool rawTSBK = true)
        {
            if (!base.Decode(data, rawTSBK))
                return false;

            ulong tsbkValue = FneUtils.ToUInt64(Payload, 0);

            ExtendedFunction = (ushort)((tsbkValue >> 48) & 0xFFFF);    // Extended Function
            SrcId = (uint)((tsbkValue >> 24) & 0xFFFFFF);               // Argument
            DstId = (uint)(tsbkValue & 0xFFFFFF);                       // Target Radio Address

            return true;
        }

        /// <summary>
        /// Encode EXT_FNCT TSBK
        /// </summary>
        /// <param name="data"></param>
        /// <param name="payload"></param>
        /// <param name="rawTSBK"></param>
        /// <param name="noTrellis"></param>
        public override void Encode(ref byte[] data, bool rawTSBK = true, bool noTrellis = true)
        {
            ulong tsbkValue = 0;

            tsbkValue = (tsbkValue << 16) | ExtendedFunction;  // Extended Function
            tsbkValue = (tsbkValue << 24) | SrcId;             // Argument
            tsbkValue = (tsbkValue << 24) | DstId;             // Target Radio Address

            FneUtils.Memset(Payload, 0x00, Payload.Length);
            FneUtils.WriteBytes(tsbkValue, ref Payload, 0);

            base.Encode(ref data, rawTSBK, noTrellis);
        }
    } // public class IOSP_EXT_FNCT
} // namespace fnecore.P25.LC.TSBK
