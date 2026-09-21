// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 C. Lovell, Dev_Ranger
using System;
using System.Linq;
using System.Security.Cryptography;

namespace fnecore.NXDN
{
    /// <summary>
    /// Tracks NXDN privacy and late entry without ever decoding unknown cipher audio as clear voice.
    /// </summary>
    public sealed class NxdnRxCall : IDisposable
    {
        private readonly INxdnAmbeCodec codec;
        private readonly uint sourceId;
        private readonly uint destinationId;
        private readonly Func<byte, ushort, byte[]> resolveKey;
        private readonly NxdnSacchMessageCollector sacch = new NxdnSacchMessageCollector();
        private NxdnPrivacyProcessor privacy;
        private byte[] key = Array.Empty<byte>();
        private bool metadataKnown;
        private bool hasSequence;
        private ushort lastSequence;
        private byte[] lastIv = Array.Empty<byte>();

        public uint StreamId { get; }
        public byte CipherType { get; private set; }
        public byte KeyId { get; private set; }
        public bool IsEncrypted => CipherType != 0;
        public bool IsMuted => !metadataKnown || (IsEncrypted && privacy == null);
        public bool IsReleased { get; private set; }

        // The resolver returns an owned key copy; it is cleared when the call ends.
        public NxdnRxCall(uint sourceId, uint destinationId, uint streamId, Func<byte, ushort, byte[]> resolveKey, INxdnAmbeCodec codec)
        {
            this.sourceId = sourceId;
            this.destinationId = destinationId;
            StreamId = streamId;
            this.resolveKey = resolveKey ?? throw new ArgumentNullException(nameof(resolveKey));
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
        }

        public byte[] Process(byte[] packet, ushort sequence)
        {
            if (IsReleased)
                return Array.Empty<byte>();
            if (sequence != ushort.MaxValue && hasSequence)
            {
                int delta = (sequence - lastSequence + ushort.MaxValue) % ushort.MaxValue;
                if (delta == 0 || delta > ushort.MaxValue / 2)
                    return Array.Empty<byte>();
                if (delta != 1)
                {
                    sacch.Reset();
                    InvalidatePrivacy();
                }
            }
            if (sequence != ushort.MaxValue)
            {
                hasSequence = true;
                lastSequence = sequence;
            }

            bool control = false;
            for (int index = 0; index < 2; index++)
            {
                if (!NxdnVoicePacketCodec.TryExtractFacchCallMetadata(packet, index, out var metadata))
                    continue;
                HandleMetadata(metadata, fromSacch: false);
                control = true;
            }
            byte[] ambe = new byte[NxdnVoicePacketCodec.AmbeBytes];
            bool hasVoice = NxdnVoicePacketCodec.TryExtractAmbe(packet, ambe, out int count);
            if ((control && !hasVoice) || IsReleased)
                return Array.Empty<byte>();

            NxdnVoicePacketCodec.CallMetadata? afterVoice = null;
            if (sacch.TryAccept(packet, out var completed))
            {
                // The SACCH successor IV applies after this frame's voice, not before it.
                if (completed.MessageType == NxdnVoicePacketCodec.VoiceCallIvMessageType)
                    afterVoice = completed;
                else
                    HandleMetadata(completed, fromSacch: true);
            }

            if (!hasVoice || (IsEncrypted && count != NxdnVoicePacketCodec.CodewordsPerFrame))
            {
                // A stolen/missing voice interval cannot be fed through an uncertain
                // keystream position. Resume encrypted audio at fresh SACCH/IV sync.
                sacch.Reset();
                InvalidatePrivacy();
                return Array.Empty<byte>();
            }
            byte[] clear = IsMuted ? new byte[0] : new byte[count * 9];
            if (!IsMuted)
            {
                for (int index = 0; index < count; index++)
                {
                    ReadOnlySpan<byte> input = ambe.AsSpan(index * 9, 9);
                    Span<byte> output = clear.AsSpan(index * 9, 9);
                    if (privacy != null)
                        privacy.ProcessCodeword(input, output);
                    else
                        input.CopyTo(output);
                }
            }
            if (afterVoice.HasValue)
                HandleMetadata(afterVoice.Value, fromSacch: true);
            return clear;
        }

        private void HandleMetadata(NxdnVoicePacketCodec.CallMetadata metadata, bool fromSacch)
        {
            if (metadata.MessageType != NxdnVoicePacketCodec.VoiceCallIvMessageType &&
                (metadata.SourceId != sourceId || metadata.DestinationId != destinationId || !metadata.Group))
                return;
            if (metadata.MessageType == NxdnVoicePacketCodec.TransmitReleaseMessageType)
            {
                IsReleased = true;
                InvalidatePrivacy();
                return;
            }
            if (metadata.MessageType == NxdnVoicePacketCodec.VoiceCallMessageType)
            {
                bool unchanged = metadataKnown && CipherType == metadata.CipherType && KeyId == metadata.KeyId;
                metadataKnown = true;
                CipherType = metadata.CipherType;
                KeyId = metadata.KeyId;
                if (unchanged && (privacy != null || CipherType == 0))
                    return;
                InvalidatePrivacy();
                CryptographicOperations.ZeroMemory(key);
                key = CipherType == 0 ? Array.Empty<byte>() : resolveKey(CipherType, KeyId) ?? Array.Empty<byte>();
                if (CipherType == NxdnPrivacyAlgorithms.Ehr && key.Length == 2)
                {
                    privacy = new NxdnPrivacyProcessor(new NxdnPrivacyOptions(CipherType, KeyId, key), codec);
                    // A late-entry VCALL completes with the fourth voice frame of its superframe.
                    if (fromSacch)
                    {
                        for (int i = 0; i < 12; i++)
                            privacy.ProcessParameters(new byte[7]);
                    }
                }
                return;
            }
            if (metadata.MessageType == NxdnVoicePacketCodec.VoiceCallIvMessageType &&
                (CipherType == NxdnPrivacyAlgorithms.Des || CipherType == NxdnPrivacyAlgorithms.Aes256) && key.Length > 0)
            {
                if (privacy != null && lastIv.AsSpan().SequenceEqual(metadata.MessageIndicator))
                    return;
                InvalidatePrivacy();
                privacy = new NxdnPrivacyProcessor(new NxdnPrivacyOptions(CipherType, KeyId, key, metadata.MessageIndicator), codec);
                lastIv = metadata.MessageIndicator.ToArray();
            }
        }

        private void InvalidatePrivacy()
        {
            privacy?.Dispose();
            privacy = null;
            CryptographicOperations.ZeroMemory(lastIv);
            lastIv = Array.Empty<byte>();
        }

        public void Dispose()
        {
            InvalidatePrivacy();
            CryptographicOperations.ZeroMemory(key);
            key = Array.Empty<byte>();
            sacch.Reset();
            IsReleased = true;
        }
    }
}
