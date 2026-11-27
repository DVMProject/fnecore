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
using fnecore.DMR;

namespace fnecore
{
    public abstract partial class FneSystemBase
    {
        public byte[] CreateDMRHeader(DMRCallData callData) => DMRFrame.EncodeHeader(callData);
        public byte[] CreateDMRVoice(DMRCallData callData, byte[] codewords) => DMRFrame.EncodeVoice(callData, codewords);
        public byte[] CreateDMRSilence(DMRCallData callData) => DMRFrame.EncodeSilence(callData);
        public byte[] CreateDMRRelease(DMRCallData callData) => DMRFrame.EncodeRelease(callData);

        /// <summary>Sends a prepared DMR packet. The caller owns pacing and RTP sequence numbers.</summary>
        public virtual void SendDMRFrame(DMRCallData callData, byte[] data, ushort packetSequence)
        {
            if (callData == null || callData.TxStreamID == 0)
                throw new ArgumentException("DMR stream ID must be nonzero.", nameof(callData));
            fne.SendMasterTraffic(FneBase.CreateOpcode(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_DMR),
                data, packetSequence, callData.TxStreamID);
        }

        /// <summary>Sends release signaling after the caller has completed the voice superframe.</summary>
        public virtual void SendDMRRelease(DMRCallData callData) =>
            SendDMRFrame(callData, CreateDMRRelease(callData), Constants.RtpCallEndSeq);
    }
}
