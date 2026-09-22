// SPDX-FileCopyrightText: 2026 RdWing
// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger

* Voice framing, privacy sequencing and call completion includes portions adapted from
* src/DvmConsole.Media/DmrTxAudioSession.cs,
* src/DvmConsole.Media/DmrTxCallSession.cs
* in DVM Console NEO (https://github.com/RdWing/dvmconsole), AGPL-3.0-only.
*/
using System;
using System.Collections.Generic;

namespace fnecore.DMR
{
    public struct DmrOutboundPacket
    {
        public DmrOutboundPacket(byte[] payload, ushort sequence)
        {
            Payload = payload;
            Sequence = sequence;
        }
        public byte[] Payload { get; }
        public ushort Sequence { get; }
    }

    /// <summary>Owns DMR call framing, sequence state, privacy and superframe completion.</summary>
    public sealed class DmrVoiceEncoder : IDisposable
    {
        private readonly uint sourceId;
        private readonly uint destinationId;
        private readonly byte slot;
        private readonly EmbeddedData embeddedData = new EmbeddedData();
        private readonly DmrPrivacyOptions privacyOptions;
        private readonly DmrPrivacyProcessor privacy;
        private readonly DmrBurstFSignaling? encryptedBurstFSignaling;
        private readonly byte[] pendingAmbe = new byte[DmrVoicePacketCodec.AmbeBytes];
        private DmrLateEntryMessageIndicator lateEntryMessageIndicator;
        private ushort packetSequence;
        private byte frameSequence;
        private byte embeddedSequence;
        private int pendingCodewordCount;
        private bool started;
        private bool ended;
        private bool disposed;

        public DmrVoiceEncoder(uint sourceId, uint destinationId, byte slot,
            DmrPrivacyOptions privacyOptions = null, IDmrAmbeCodec codec = null,
            ushort packetSequence = 0, byte frameSequence = 0)
        {
            if (sourceId == 0 || sourceId > 0xFFFFFF)
                throw new ArgumentOutOfRangeException(nameof(sourceId));
            if (destinationId == 0 || destinationId > 0xFFFFFF)
                throw new ArgumentOutOfRangeException(nameof(destinationId));
            if (slot < 1 || slot > 2)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (packetSequence == DmrVoicePacketCodec.RtpCallEndSequence)
                throw new ArgumentOutOfRangeException(nameof(packetSequence));
            if (privacyOptions != null && codec == null)
                throw new ArgumentNullException(nameof(codec));

            this.sourceId = sourceId;
            this.destinationId = destinationId;
            this.slot = slot;
            this.privacyOptions = privacyOptions;
            this.packetSequence = packetSequence;
            this.frameSequence = frameSequence;
            if (privacyOptions != null)
            {
                privacy = new DmrPrivacyProcessor(privacyOptions, codec);
                encryptedBurstFSignaling = DmrBurstFSignaling.EncryptionIdentifiers(
                    privacyOptions.AlgorithmId, privacyOptions.KeyId);
            }

            LC lc = new LC
            {
                FLCO = (byte)DMRFLCO.FLCO_GROUP,
                FID = privacyOptions == null ? (byte)0 : DmrPrivacyAlgorithms.FeatureId,
                Encrypted = privacyOptions != null,
                SrcId = sourceId,
                DstId = destinationId
            };
            embeddedData.SetLC(lc);
        }

        public int PendingCodewordCount => pendingCodewordCount;
        public byte EmbeddedSequence => embeddedSequence;
        public bool IsEncrypted => privacyOptions != null;

        public IReadOnlyList<DmrOutboundPacket> CreateCallStartPackets()
        {
            ThrowIfDisposed();
            if (started)
                throw new InvalidOperationException("The DMR call has already started.");
            if (ended)
                throw new InvalidOperationException("The DMR call has already ended.");

            List<DmrOutboundPacket> packets = new List<DmrOutboundPacket>();
            packets.Add(CreateOutbound(DmrVoicePacketCodec.CreateVoiceLcHeaderPacket(
                sourceId, destinationId, slot, frameSequence, IsEncrypted)));
            if (privacyOptions != null)
            {
                packets.Add(CreateOutbound(DmrVoicePacketCodec.CreatePrivacyIndicatorPacket(
                    sourceId, destinationId, slot, frameSequence, privacyOptions)));
            }
            started = true;
            return packets;
        }

        /// <summary>Returns a voice packet after every third 20 ms AMBE codeword, otherwise null.</summary>
        public DmrOutboundPacket? ProcessCodeword(ReadOnlySpan<byte> codeword)
        {
            ThrowIfDisposed();
            if (!started || ended)
                throw new InvalidOperationException("The DMR call must be active before processing audio.");
            if (codeword.Length != DmrVoicePacketCodec.CodewordBytes)
                throw new ArgumentException("DMR requires one 9-byte AMBE codeword.", nameof(codeword));

            if (privacy != null && embeddedSequence == 0 && pendingCodewordCount == 0)
                lateEntryMessageIndicator = new DmrLateEntryMessageIndicator(privacy.GetNextMessageIndicator());

            Span<byte> destination = pendingAmbe.AsSpan(
                pendingCodewordCount * DmrVoicePacketCodec.CodewordBytes,
                DmrVoicePacketCodec.CodewordBytes);
            if (privacy != null)
                privacy.ProcessCodeword(codeword, destination);
            else
                codeword.CopyTo(destination);
            if (lateEntryMessageIndicator != null)
                lateEntryMessageIndicator.ApplyFragment(destination, embeddedSequence, pendingCodewordCount);

            pendingCodewordCount++;
            if (pendingCodewordCount < DmrVoicePacketCodec.CodewordsPerPacket)
                return null;

            bool voiceSync = embeddedSequence == 0;
            byte[] packet = DmrVoicePacketCodec.CreateVoicePacket(sourceId, destinationId, slot,
                voiceSync, embeddedSequence, frameSequence, pendingAmbe, embeddedData,
                embeddedSequence == 5 ? encryptedBurstFSignaling : null);
            DmrOutboundPacket outbound = CreateOutbound(packet);
            pendingCodewordCount = 0;
            Array.Clear(pendingAmbe, 0, pendingAmbe.Length);
            if (embeddedSequence == 5)
            {
                embeddedSequence = 0;
                lateEntryMessageIndicator = null;
            }
            else
                embeddedSequence++;
            return outbound;
        }

        /// <summary>Completes the current superframe with silence and appends the LC terminator.</summary>
        public IReadOnlyList<DmrOutboundPacket> Complete(ReadOnlySpan<byte> silenceCodeword)
        {
            ThrowIfDisposed();
            if (!started)
                throw new InvalidOperationException("The DMR call has not started.");
            if (ended)
                return Array.Empty<DmrOutboundPacket>();
            if (silenceCodeword.Length != DmrVoicePacketCodec.CodewordBytes)
                throw new ArgumentException("DMR silence must be one 9-byte AMBE codeword.", nameof(silenceCodeword));

            List<DmrOutboundPacket> packets = new List<DmrOutboundPacket>();
            while (pendingCodewordCount > 0)
            {
                DmrOutboundPacket? packet = ProcessCodeword(silenceCodeword);
                if (packet.HasValue)
                    packets.Add(packet.Value);
            }
            while (embeddedSequence != 0)
            {
                for (int index = 0; index < DmrVoicePacketCodec.CodewordsPerPacket; index++)
                {
                    DmrOutboundPacket? packet = ProcessCodeword(silenceCodeword);
                    if (packet.HasValue)
                        packets.Add(packet.Value);
                }
            }
            packets.Add(new DmrOutboundPacket(DmrVoicePacketCodec.CreateTerminatorPacket(
                sourceId, destinationId, slot, frameSequence, IsEncrypted),
                DmrVoicePacketCodec.RtpCallEndSequence));
            ended = true;
            return packets;
        }

        public void DiscardPending()
        {
            pendingCodewordCount = 0;
            Array.Clear(pendingAmbe, 0, pendingAmbe.Length);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            privacy?.Dispose();
            DiscardPending();
            disposed = true;
        }

        private DmrOutboundPacket CreateOutbound(byte[] payload)
        {
            DmrOutboundPacket result = new DmrOutboundPacket(payload, packetSequence);
            packetSequence = packetSequence >= DmrVoicePacketCodec.RtpCallEndSequence - 1
                ? (ushort)0 : (ushort)(packetSequence + 1);
            frameSequence++;
            return result;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(DmrVoiceEncoder));
        }
    }
}
