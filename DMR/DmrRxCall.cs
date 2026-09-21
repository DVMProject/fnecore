// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*/
using System;
using System.Security.Cryptography;

namespace fnecore.DMR
{
    /// <summary>Tracks one DMR receive call and returns clear AMBE voice frames.</summary>
    public sealed class DmrRxCall : IDisposable
    {
        private enum UnknownPrivacyResolution { AwaitingMetadata, Clear, Encrypted }

        private readonly Func<byte, byte, byte[]> resolveKey;
        private readonly IDmrAmbeCodec codec;
        private readonly DmrLateEntryMessageIndicator lateEntryCollector = new DmrLateEntryMessageIndicator();
        private DmrPrivacyProcessor privacy;
        private bool privacyRequired;
        private bool privacyStateKnown;
        private bool disposed;

        public DmrRxCall(uint sourceId, uint destinationId, uint streamId,
            Func<byte, byte, byte[]> resolveKey, IDmrAmbeCodec codec)
        {
            if (sourceId == 0 || sourceId > 0xFFFFFF)
                throw new ArgumentOutOfRangeException(nameof(sourceId));
            if (destinationId == 0 || destinationId > 0xFFFFFF)
                throw new ArgumentOutOfRangeException(nameof(destinationId));
            if (streamId == 0)
                throw new ArgumentOutOfRangeException(nameof(streamId));
            SourceId = sourceId;
            DestinationId = destinationId;
            StreamId = streamId;
            this.resolveKey = resolveKey ?? throw new ArgumentNullException(nameof(resolveKey));
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
        }

        public uint SourceId { get; }
        public uint DestinationId { get; }
        public uint StreamId { get; }
        public bool IsEncrypted => privacyRequired;
        public bool HasKey => !privacyRequired || privacy != null;
        public bool IsReleased { get; private set; }
        public byte AlgorithmId { get; private set; }
        public byte KeyId { get; private set; }

        public byte[] Process(byte[] packet, FrameType frameType, DMRDataType dataType, byte voiceBurst)
        {
            ThrowIfDisposed();
            if (packet == null || packet.Length < DmrVoicePacketCodec.PacketBytes)
                return Array.Empty<byte>();

            if (frameType == FrameType.DATA_SYNC && dataType == DMRDataType.TERMINATOR_WITH_LC)
            {
                IsReleased = true;
                return Array.Empty<byte>();
            }
            if (frameType == FrameType.DATA_SYNC && dataType == DMRDataType.VOICE_LC_HEADER)
            {
                if (DmrVoicePacketCodec.TryExtractVoiceEncryptionState(packet, out bool encrypted))
                {
                    privacyStateKnown = true;
                    privacyRequired = encrypted;
                    if (!encrypted)
                        ClearPrivacy();
                }
                return Array.Empty<byte>();
            }
            if (frameType == FrameType.DATA_SYNC && dataType == DMRDataType.VOICE_PI_HEADER)
            {
                privacyStateKnown = true;
                privacyRequired = true;
                if (DmrVoicePacketCodec.TryExtractEncryptionMetadata(packet, out DmrVoicePacketCodec.DmrEncryptionMetadata metadata))
                    PreparePrivacy(metadata);
                return Array.Empty<byte>();
            }
            if (frameType != FrameType.VOICE_SYNC && frameType != FrameType.VOICE)
                return Array.Empty<byte>();

            byte[] ambe = new byte[DmrVoicePacketCodec.AmbeBytes];
            if (!DmrVoicePacketCodec.TryExtractAmbe(packet, ambe))
            {
                privacy?.SkipCodewords(DmrVoicePacketCodec.CodewordsPerPacket);
                return Array.Empty<byte>();
            }

            bool hasLateEntryMi = lateEntryCollector.AddVoiceBurst(voiceBurst, ambe, out byte[] lateEntryMi);
            DmrBurstFSignaling burstFSignaling = default(DmrBurstFSignaling);
            bool hasBurstFSignaling = voiceBurst == 5 &&
                DmrVoicePacketCodec.TryExtractBurstFSignaling(packet, out burstFSignaling);

            if (!privacyStateKnown)
            {
                UnknownPrivacyResolution resolution = ResolveUnknownPrivacyState(voiceBurst,
                    hasLateEntryMi, lateEntryMi, hasBurstFSignaling, burstFSignaling);
                if (resolution != UnknownPrivacyResolution.Clear)
                    return Array.Empty<byte>();
            }
            if (privacyRequired && privacy == null)
            {
                if (hasLateEntryMi && hasBurstFSignaling)
                    TryPrepareLateEntry(lateEntryMi, burstFSignaling);
                return Array.Empty<byte>();
            }

            byte[] clear = new byte[ambe.Length];
            if (privacy == null)
                Buffer.BlockCopy(ambe, 0, clear, 0, ambe.Length);
            else
            {
                for (int index = 0; index < DmrVoicePacketCodec.CodewordsPerPacket; index++)
                {
                    privacy.ProcessCodeword(
                        ambe.AsSpan(index * DmrVoicePacketCodec.CodewordBytes, DmrVoicePacketCodec.CodewordBytes),
                        clear.AsSpan(index * DmrVoicePacketCodec.CodewordBytes, DmrVoicePacketCodec.CodewordBytes));
                }
            }

            // Late-entry signaling advertises the MI for the next superframe.
            if (privacy != null && hasLateEntryMi && hasBurstFSignaling)
                TryPrepareLateEntry(lateEntryMi, burstFSignaling);
            return clear;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            ClearPrivacy();
            lateEntryCollector.Reset();
            disposed = true;
        }

        private bool PreparePrivacy(DmrVoicePacketCodec.DmrEncryptionMetadata metadata)
        {
            privacyRequired = true;
            AlgorithmId = metadata.AlgorithmId;
            KeyId = metadata.KeyId;
            byte[] key = resolveKey(metadata.AlgorithmId, metadata.KeyId);
            if (key == null)
            {
                privacy?.Dispose();
                privacy = null;
                return false;
            }
            try
            {
                privacy?.Dispose();
                privacy = new DmrPrivacyProcessor(new DmrPrivacyOptions(metadata.AlgorithmId,
                    metadata.KeyId, key, metadata.MessageIndicator), codec);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        private bool TryPrepareLateEntry(byte[] messageIndicator, DmrBurstFSignaling signaling)
        {
            if (!IsPrivacyAssociation(signaling))
                return false;
            return PreparePrivacy(new DmrVoicePacketCodec.DmrEncryptionMetadata(signaling.AlgorithmId,
                signaling.KeyId, DmrPrivacyAlgorithms.FeatureId, DestinationId, true, messageIndicator));
        }

        private UnknownPrivacyResolution ResolveUnknownPrivacyState(byte voiceBurst,
            bool hasMessageIndicator, byte[] messageIndicator, bool hasBurstFSignaling,
            DmrBurstFSignaling signaling)
        {
            if (hasMessageIndicator && hasBurstFSignaling && TryPrepareLateEntry(messageIndicator, signaling))
            {
                privacyStateKnown = true;
                privacyRequired = true;
                return UnknownPrivacyResolution.Encrypted;
            }
            if (voiceBurst != 5 || (hasBurstFSignaling && IsPrivacyAssociation(signaling)))
                return UnknownPrivacyResolution.AwaitingMetadata;
            privacyStateKnown = true;
            privacyRequired = false;
            return UnknownPrivacyResolution.Clear;
        }

        private static bool IsPrivacyAssociation(DmrBurstFSignaling signaling)
        {
            bool supported = signaling.AlgorithmId == DmrPrivacyAlgorithms.Arc4 ||
                signaling.AlgorithmId == DmrPrivacyAlgorithms.DesOfb ||
                signaling.AlgorithmId == DmrPrivacyAlgorithms.Aes256;
            return !signaling.IsReverseChannel && signaling.Payload != 0 && supported && signaling.KeyId != 0;
        }

        private void ClearPrivacy()
        {
            privacy?.Dispose();
            privacy = null;
            AlgorithmId = 0;
            KeyId = 0;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(DmrRxCall));
        }
    }
}
