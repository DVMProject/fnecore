// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* Copyright (C) 2026 C. Lovell, Dev_Ranger
*/

using System;
using fnecore.NXDN.LC;

namespace fnecore.NXDN
{
    /// <summary>
    /// Per-stream NXDN metadata and protocol state. Use distinct instances for
    /// transmit and receive, and serialize access as with other remote call data.
    /// </summary>
    public sealed class NXDNCallData : RemoteCallData, IDisposable
    {
        public byte Ran;
        public bool Group = true;
        public bool IsReleased { get; internal set; }
        public bool IsEncrypted => AlgorithmId != NXDNCrypto.Clear;
        public bool HasCallInfo { get; internal set; }
        public NXDNCrypto Crypto { get; } = new NXDNCrypto();

        internal readonly byte[] Fragments = new byte[9];
        internal readonly byte[] Word = new byte[9];
        internal readonly byte[] VoiceBits = new byte[49];
        internal readonly RTCH LinkControl = new RTCH();
        internal int FragmentQuarter = -1;
        internal int LastSequence = -1;
        internal int VoiceFrame;
        internal int PreparedSession = -1;
        internal ulong CurrentIndicator;
        internal bool IndicatorKnown;
        internal bool Transmitting;
        internal bool Disposed;
        internal bool Invalid;
        internal uint TxSource, TxDestination;
        internal byte TxAlgorithm, TxRan;
        internal ushort TxKey;
        internal bool TxGroup;

        public NXDNCallData()
        {
            AlgorithmId = NXDNCrypto.Clear;
            MessageIndicator = new byte[8];
        }

        /// <summary>Clears keys, synchronization, and inherited call metadata.</summary>
        public override void Reset()
        {
            base.Reset();
            Crypto.Reset();
            AlgorithmId = NXDNCrypto.Clear;
            MessageIndicator = new byte[8];
            TxStreamID = 0;
            Ran = 0;
            Group = true;
            IsReleased = HasCallInfo = IndicatorKnown = Transmitting = Invalid = false;
            FragmentQuarter = LastSequence = PreparedSession = -1;
            VoiceFrame = 0;
            CurrentIndicator = 0;
            Array.Clear(Fragments, 0, Fragments.Length);
            Array.Clear(Word, 0, Word.Length);
            Array.Clear(VoiceBits, 0, VoiceBits.Length);
        }

        internal void CheckActive()
        {
            if (Disposed) throw new ObjectDisposedException(nameof(NXDNCallData));
            if (Invalid || IsReleased) throw new InvalidOperationException("NXDN call is no longer active.");
        }

        internal void SetLinkControl(NXDNMessageType type)
        {
            LinkControl.MessageType = type;
            LinkControl.SrcId = checked((ushort)SrcId);
            LinkControl.DstId = checked((ushort)DstId);
            LinkControl.CallType = (byte)(Group ? 1 : 4);
            LinkControl.Emergency = (ServiceOptions & 0x80) != 0;
            LinkControl.Priority = (ServiceOptions & 0x20) != 0;
            LinkControl.CipherType = AlgorithmId;
            LinkControl.KeyId = checked((byte)KeyId);
        }

        internal void CheckTransmitMetadata()
        {
            if (SrcId != TxSource || DstId != TxDestination || AlgorithmId != TxAlgorithm ||
                KeyId != TxKey || Ran != TxRan || Group != TxGroup)
                throw new InvalidOperationException("NXDN call metadata cannot change during transmission.");
        }

        public void Dispose()
        {
            if (Disposed) return;
            Reset();
            Crypto.Dispose();
            Disposed = true;
        }
    }
}
