// SPDX-FileCopyrightText: 2026 RdWing
// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 C. Lovell, Dev_Ranger
// Voice aggregation and signaling includes portions adapted from
// src/DvmConsole.Media/NxdnTxAudioSession.cs,
// src/DvmConsole.Media/NxdnTxCallSession.cs
// in DVM Console NEO (https://github.com/RdWing/dvmconsole), AGPL-3.0-only.
using System;

namespace fnecore.NXDN
{
    /// <summary>
    /// Encodes a group voice call from AMBE codewords, including privacy and
    /// SACCH/IV rotation. The caller owns vocoding, transport and 80 ms pacing.
    /// </summary>
    public sealed class NxdnVoiceEncoder : IDisposable
    {
        private readonly uint sourceId;
        private readonly uint destinationId;
        private readonly byte ran;
        private readonly byte cipherType;
        private readonly byte keyId;
        private readonly byte[] startPacket;
        private readonly byte[] ambe = new byte[NxdnVoicePacketCodec.AmbeBytes];
        private readonly NxdnPrivacyProcessor privacy;
        private readonly NxdnVoiceSignalingCycle signaling;
        private byte frameSequence = 1;
        private bool disposed;

        public int PendingCodewordCount { get; private set; }

        public NxdnVoiceEncoder(uint sourceId, uint destinationId, byte ran,
            NxdnPrivacyOptions options = null, INxdnAmbeCodec codec = null)
        {
            this.sourceId = sourceId;
            this.destinationId = destinationId;
            this.ran = ran;
            cipherType = options?.AlgorithmId ?? 0;
            keyId = options?.KeyId ?? 0;
            if (options != null && codec == null)
                throw new ArgumentNullException(nameof(codec));
            startPacket = cipherType == NxdnPrivacyAlgorithms.Des || cipherType == NxdnPrivacyAlgorithms.Aes256
                ? NxdnVoicePacketCodec.CreatePrivacyCallStartPacket(sourceId, destinationId, true, 0,
                    cipherType, keyId, options.MessageIndicator.Span, ran)
                : NxdnVoicePacketCodec.CreateCallControlPacket(sourceId, destinationId, true,
                    NxdnVoicePacketCodec.VoiceCallMessageType, 0, cipherType, keyId, ran: ran);
            privacy = options == null ? null : new NxdnPrivacyProcessor(options, codec);
            signaling = new NxdnVoiceSignalingCycle(sourceId, destinationId, true, cipherType, keyId,
                options?.MessageIndicator ?? ReadOnlyMemory<byte>.Empty);
        }

        public byte[] CreateCallStartPacket()
        {
            ThrowIfDisposed();
            return (byte[])startPacket.Clone();
        }

        /// <summary>Returns a voice packet after every fourth 20 ms codeword, otherwise null.</summary>
        public byte[] ProcessCodeword(ReadOnlySpan<byte> word)
        {
            ThrowIfDisposed();
            if (word.Length != NxdnVoicePacketCodec.CodewordBytes)
                throw new ArgumentException("NXDN requires one 9-byte AMBE codeword.", nameof(word));
            Span<byte> destination = ambe.AsSpan(PendingCodewordCount * 9, 9);
            if (privacy != null)
                privacy.ProcessCodeword(word, destination);
            else
                word.CopyTo(destination);
            PendingCodewordCount++;
            if (PendingCodewordCount != NxdnVoicePacketCodec.CodewordsPerFrame)
                return null;
            byte[] packet = NxdnVoicePacketCodec.CreateVoicePacket(sourceId, destinationId, true,
                frameSequence++, ambe, ran, signaling.SuperframePart, cipherType, keyId, signaling.CurrentMetadata);
            signaling.AdvanceAfterVoiceFrame(privacy);
            DiscardPending();
            return packet;
        }

        // Release remains available after disposal so an asynchronous sender can unkey.
        public byte[] CreateReleasePacket() => NxdnVoicePacketCodec.CreateCallControlPacket(
            sourceId, destinationId, true, NxdnVoicePacketCodec.TransmitReleaseMessageType, 0, ran: ran);

        public void DiscardPending()
        {
            PendingCodewordCount = 0;
            Array.Clear(ambe, 0, ambe.Length);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            privacy?.Dispose();
            signaling.Dispose();
            DiscardPending();
            Array.Clear(startPacket, 0, startPacket.Length);
            disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(NxdnVoiceEncoder));
        }
    }
}
