// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* Copyright (C) 2026 C. Lovell, Dev_Ranger
*/

using System;

namespace fnecore.DMR
{
    /// <summary>Per-stream DMR state. Use separate instances for TX and RX, with serialized access.</summary>
    public sealed class DMRCallData : RemoteCallData, IDisposable
    {
        public bool Group = true;
        public byte ColorCode;
        public bool HasCallInfo { get; internal set; }
        public bool IsEncrypted { get; internal set; }
        public bool IsReleased { get; internal set; }
        public bool LastPacketAccepted { get; internal set; }
        public DMRDataType DataType { get; internal set; }
        public LC LinkControl => Control == null ? null : new LC(Control.GetBytes());

        /// <summary>Number of silence bursts needed before the terminator, from zero to five.</summary>
        public int PendingVoiceBursts => (6 - VoiceBurst) % 6;

        internal readonly EmbeddedData Embedded = new EmbeddedData();
        internal readonly byte[] Frame = new byte[DMRFrame.FrameBytes];
        internal LC Control;
        internal byte VoiceBurst;
        internal byte Sequence;
        internal int LastSequence = -1;
        internal int LastVoiceBurst = -1;
        internal bool Transmitting;
        internal bool Receiving;
        internal bool Disposed;
        internal uint Source, Destination, Stream;
        internal byte TimeSlot, AccessCode, Options;
        internal bool GroupCall;

        public DMRCallData()
        {
            Slot = 1;
            AlgorithmId = 0;
            MessageIndicator = new byte[4];
        }

        public override void Reset()
        {
            base.Reset();
            TxStreamID = 0;
            Slot = 1;
            AlgorithmId = 0;
            MessageIndicator = new byte[4];
            Group = true;
            ColorCode = 0;
            HasCallInfo = IsEncrypted = IsReleased = LastPacketAccepted = false;
            Transmitting = Receiving = false;
            LastSequence = LastVoiceBurst = -1;
            VoiceBurst = Sequence = 0;
            Control = null;
            Embedded.Reset();
            Array.Clear(Frame, 0, Frame.Length);
        }

        internal void CheckActive()
        {
            if (Disposed) throw new ObjectDisposedException(nameof(DMRCallData));
            if (IsReleased) throw new InvalidOperationException("DMR call has ended.");
        }

        internal void CheckTransmit()
        {
            CheckActive();
            if (!Transmitting) throw new InvalidOperationException("Create the DMR header before voice or release.");
            if (SrcId != Source || DstId != Destination || TxStreamID != Stream || Slot != TimeSlot ||
                ColorCode != AccessCode || ServiceOptions != Options || Group != GroupCall || MFId != 0 ||
                AlgorithmId != 0 || KeyId != 0)
                throw new InvalidOperationException("DMR call metadata cannot change during transmission.");
        }

        public void Dispose()
        {
            if (Disposed) return;
            Reset();
            Disposed = true;
        }
    }
}
