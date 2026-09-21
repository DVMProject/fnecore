// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
* Portions of this DMR implementation draw on DvmConsole.Media
* (https://github.com/RdWing/dvmconsole), AGPL-3.0-only.
*/
using System;

namespace fnecore.DMR
{
    /// <summary>Encodes and decodes the DMRD/FNE voice packet layout.</summary>
    public static class DmrVoicePacketCodec
    {
        public const int HeaderBytes = 20;
        public const int FrameBytes = 33;
        public const int PacketBytes = 55;
        public const int CodewordBytes = 9;
        public const int CodewordsPerPacket = 3;
        public const int AmbeBytes = CodewordBytes * CodewordsPerPacket;
        public const ushort RtpCallEndSequence = ushort.MaxValue;

        public struct DmrEncryptionMetadata
        {
            public DmrEncryptionMetadata(byte algorithmId, byte keyId, byte featureId, uint destinationId,
                bool group, byte[] messageIndicator)
            {
                AlgorithmId = algorithmId;
                KeyId = keyId;
                FeatureId = featureId;
                DestinationId = destinationId;
                Group = group;
                MessageIndicator = messageIndicator;
            }
            public byte AlgorithmId { get; }
            public byte KeyId { get; }
            public byte FeatureId { get; }
            public uint DestinationId { get; }
            public bool Group { get; }
            public byte[] MessageIndicator { get; }
        }

        public static bool TryExtractAmbe(ReadOnlySpan<byte> packet, Span<byte> ambe)
        {
            if (packet.Length < PacketBytes || ambe.Length < AmbeBytes)
                return false;
            ReadOnlySpan<byte> frame = packet.Slice(HeaderBytes, FrameBytes);
            frame.Slice(0, 13).CopyTo(ambe);
            ambe[13] = (byte)((frame[13] & 0xF0) | (frame[19] & 0x0F));
            frame.Slice(20, 13).CopyTo(ambe.Slice(14));
            return true;
        }

        public static byte[] ExtractAmbe(ReadOnlySpan<byte> packet)
        {
            byte[] ambe = new byte[AmbeBytes];
            if (!TryExtractAmbe(packet, ambe))
                throw new ArgumentException("The DMR packet does not contain a complete voice frame.", nameof(packet));
            return ambe;
        }

        public static bool TryExtractEncryptionMetadata(ReadOnlySpan<byte> packet, out DmrEncryptionMetadata metadata)
        {
            metadata = default(DmrEncryptionMetadata);
            if (packet.Length < PacketBytes)
                return false;
            try
            {
                byte[] frame = packet.Slice(HeaderBytes, FrameBytes).ToArray();
                SlotType slotType = new SlotType(frame);
                if (slotType.DataType != (byte)DMRDataType.VOICE_PI_HEADER)
                    return false;
                PrivacyLC privacy = FullLC.DecodePI(frame);
                if (privacy == null || privacy.FID != DmrPrivacyAlgorithms.FeatureId || privacy.KId == 0 ||
                    !IsSupportedAlgorithm(privacy.AlgId))
                    return false;
                byte[] raw = privacy.GetBytes();
                byte[] mi = new byte[DmrPrivacyAlgorithms.MessageIndicatorBytes];
                Buffer.BlockCopy(raw, 3, mi, 0, mi.Length);
                metadata = new DmrEncryptionMetadata(privacy.AlgId, (byte)privacy.KId, privacy.FID,
                    privacy.DstId, privacy.Group, mi);
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (IndexOutOfRangeException) { return false; }
        }

        public static bool IsPrivacyIndicator(ReadOnlySpan<byte> packet)
        {
            if (packet.Length < PacketBytes || (packet[15] & 0x3F) != 0x20)
                return false;
            try
            {
                SlotType slotType = new SlotType(packet.Slice(HeaderBytes, FrameBytes).ToArray());
                return slotType.DataType == (byte)DMRDataType.VOICE_PI_HEADER;
            }
            catch (ArgumentException) { return false; }
            catch (IndexOutOfRangeException) { return false; }
        }

        public static bool TryExtractVoiceEncryptionState(ReadOnlySpan<byte> packet, out bool encrypted)
        {
            encrypted = false;
            if (packet.Length < PacketBytes || (packet[15] & 0x3F) != 0x21)
                return false;
            try
            {
                byte[] frame = packet.Slice(HeaderBytes, FrameBytes).ToArray();
                SlotType slotType = new SlotType(frame);
                if (slotType.DataType != (byte)DMRDataType.VOICE_LC_HEADER)
                    return false;
                LC linkControl = FullLC.Decode(frame, DMRDataType.VOICE_LC_HEADER);
                if (linkControl == null)
                    return false;
                encrypted = linkControl.Encrypted;
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (IndexOutOfRangeException) { return false; }
        }

        public static byte[] CreateVoicePacket(uint sourceId, uint destinationId, byte slot,
            bool voiceSync, byte embeddedSequence, byte frameSequence, ReadOnlySpan<byte> ambe,
            EmbeddedData embeddedData = null, DmrBurstFSignaling? burstFSignaling = null)
        {
            ValidateAddressing(sourceId, destinationId, slot);
            if (embeddedSequence > 5)
                throw new ArgumentOutOfRangeException(nameof(embeddedSequence));
            if (ambe.Length < AmbeBytes)
                throw new ArgumentException($"AMBE data must contain {AmbeBytes} bytes.", nameof(ambe));

            byte[] packet = CreatePacketHeader(sourceId, destinationId, slot, frameSequence);
            packet[15] |= voiceSync ? (byte)0x10 : embeddedSequence;
            byte[] frame = new byte[FrameBytes];
            ambe.Slice(0, 13).CopyTo(frame);
            frame[13] = (byte)(ambe[13] & 0xF0);
            frame[19] = (byte)(ambe[13] & 0x0F);
            ambe.Slice(14).CopyTo(frame.AsSpan(20, 13));
            if (!voiceSync)
            {
                byte lcss = 0;
                if (embeddedSequence >= 1 && embeddedSequence <= 4 && embeddedData != null)
                    lcss = embeddedData.GetData(ref frame, embeddedSequence);
                else if (embeddedSequence == 5 && burstFSignaling.HasValue)
                    burstFSignaling.Value.Encode(frame);
                EMB emb = new EMB
                {
                    ColorCode = 0,
                    PI = embeddedSequence == 5 && burstFSignaling.HasValue && burstFSignaling.Value.IsReverseChannel,
                    LCSS = lcss
                };
                emb.Encode(ref frame);
            }
            frame.CopyTo(packet.AsSpan(HeaderBytes));
            return packet;
        }

        public static bool TryExtractBurstFSignaling(ReadOnlySpan<byte> packet, out DmrBurstFSignaling signaling)
        {
            signaling = default(DmrBurstFSignaling);
            if (packet.Length < PacketBytes || (packet[15] & 0x0F) != 5)
                return false;
            byte[] frame = packet.Slice(HeaderBytes, FrameBytes).ToArray();
            EMB embedded = new EMB();
            embedded.Decode(frame);
            if (embedded.LCSS != 0)
                return false;
            return DmrBurstFSignaling.TryDecode(frame, embedded.PI, out signaling);
        }

        public static byte[] CreateVoiceLcHeaderPacket(uint sourceId, uint destinationId, byte slot,
            byte frameSequence, bool encrypted = false)
        {
            return CreateControlPacket(sourceId, destinationId, slot, frameSequence,
                DMRDataType.VOICE_LC_HEADER, encrypted);
        }

        public static byte[] CreatePrivacyIndicatorPacket(uint sourceId, uint destinationId, byte slot,
            byte frameSequence, DmrPrivacyOptions privacy)
        {
            ValidateAddressing(sourceId, destinationId, slot);
            if (privacy == null)
                throw new ArgumentNullException(nameof(privacy));
            byte[] packet = CreatePacketHeader(sourceId, destinationId, slot, frameSequence);
            packet[15] |= (byte)(0x20 | (byte)DMRDataType.VOICE_PI_HEADER);
            byte[] frame = new byte[FrameBytes];
            byte[] raw = new byte[10];
            raw[0] = (byte)(0x20 | privacy.AlgorithmId);
            raw[1] = DmrPrivacyAlgorithms.FeatureId;
            raw[2] = privacy.KeyId;
            privacy.MessageIndicator.Span.CopyTo(raw.AsSpan(3, DmrPrivacyAlgorithms.MessageIndicatorBytes));
            WriteThreeBytes(raw, 7, destinationId);
            FullLC.EncodePI(new PrivacyLC(raw), ref frame);
            new SlotType { ColorCode = 0, DataType = (byte)DMRDataType.VOICE_PI_HEADER }.GetData(ref frame);
            frame.CopyTo(packet.AsSpan(HeaderBytes));
            return packet;
        }

        public static byte[] CreateTerminatorPacket(uint sourceId, uint destinationId, byte slot,
            byte frameSequence, bool encrypted = false)
        {
            return CreateControlPacket(sourceId, destinationId, slot, frameSequence,
                DMRDataType.TERMINATOR_WITH_LC, encrypted);
        }

        private static byte[] CreateControlPacket(uint sourceId, uint destinationId, byte slot,
            byte frameSequence, DMRDataType dataType, bool encrypted)
        {
            ValidateAddressing(sourceId, destinationId, slot);
            byte[] packet = CreatePacketHeader(sourceId, destinationId, slot, frameSequence);
            packet[15] |= (byte)(0x20 | (byte)dataType);
            byte[] frame = new byte[FrameBytes];
            LC lc = new LC
            {
                FLCO = (byte)DMRFLCO.FLCO_GROUP,
                FID = encrypted ? DmrPrivacyAlgorithms.FeatureId : (byte)0,
                Encrypted = encrypted,
                SrcId = sourceId,
                DstId = destinationId
            };
            FullLC.Encode(lc, ref frame, dataType);
            new SlotType { ColorCode = 0, DataType = (byte)dataType }.GetData(ref frame);
            frame.CopyTo(packet.AsSpan(HeaderBytes));
            return packet;
        }

        private static byte[] CreatePacketHeader(uint sourceId, uint destinationId, byte slot, byte frameSequence)
        {
            byte[] packet = new byte[PacketBytes];
            packet[0] = (byte)'D'; packet[1] = (byte)'M'; packet[2] = (byte)'R'; packet[3] = (byte)'D';
            packet[4] = frameSequence;
            WriteThreeBytes(packet, 5, sourceId);
            WriteThreeBytes(packet, 8, destinationId);
            packet[15] = slot == 2 ? (byte)0x80 : (byte)0x00;
            return packet;
        }

        private static void ValidateAddressing(uint sourceId, uint destinationId, byte slot)
        {
            if (sourceId == 0 || sourceId > 0xFFFFFF)
                throw new ArgumentOutOfRangeException(nameof(sourceId));
            if (destinationId == 0 || destinationId > 0xFFFFFF)
                throw new ArgumentOutOfRangeException(nameof(destinationId));
            if (slot < 1 || slot > 2)
                throw new ArgumentOutOfRangeException(nameof(slot));
        }

        private static void WriteThreeBytes(byte[] target, int offset, uint value)
        {
            target[offset] = (byte)(value >> 16);
            target[offset + 1] = (byte)(value >> 8);
            target[offset + 2] = (byte)value;
        }

        private static bool IsSupportedAlgorithm(byte algorithmId)
        {
            return algorithmId == DmrPrivacyAlgorithms.Arc4 || algorithmId == DmrPrivacyAlgorithms.DesOfb ||
                algorithmId == DmrPrivacyAlgorithms.Aes256;
        }
    }
}
