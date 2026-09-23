// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* Copyright (C) 2026 C. Lovell, Dev_Ranger
*/

using System;
using fnecore.NXDN;

namespace fnecore
{
    public abstract partial class FneSystemBase
    {
        /// <summary>Creates an NXDN network header from remote call metadata.</summary>
        public void CreateNXDNMessageHdr(byte messageType, NXDNCallData callData, ref byte[] data)
        {
            NXDNFrame.CreateMessageHeader((NXDNMessageType)messageType, callData, data);
        }

        /// <summary>Creates call-start signaling, including an IV for DES/AES.</summary>
        public byte[] CreateNXDNHeader(NXDNCallData callData) => NXDNFrame.EncodeHeader(callData);

        /// <summary>Creates one 80 ms voice packet; the caller schedules its transmission.</summary>
        public byte[] CreateNXDNVoice(NXDNCallData callData, byte[] codewords, INxdnAmbeCodec codec) =>
            NXDNFrame.EncodeVoice(callData, codewords, codec);

        /// <summary>Creates a release packet without scheduling or padding audio.</summary>
        public byte[] CreateNXDNRelease(NXDNCallData callData) => NXDNFrame.EncodeRelease(callData);

        /// <summary>Sends a prepared packet using the caller's stream and RTP sequence.</summary>
        public virtual void SendNXDNFrame(NXDNCallData callData, byte[] data, ushort packetSequence)
        {
            if (callData.TxStreamID == 0) throw new ArgumentException("NXDN stream ID must be nonzero.", nameof(callData));
            fne.SendMasterTraffic(FneBase.CreateOpcode(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_NXDN),
                data, packetSequence, callData.TxStreamID);
        }

        /// <summary>Sends transmission-release signaling, like the P25 TDU helper.</summary>
        public virtual void SendNXDNRelease(NXDNCallData callData)
        {
            SendNXDNFrame(callData, CreateNXDNRelease(callData), Constants.RtpCallEndSeq);
        }
    }
}
