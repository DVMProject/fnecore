// SPDX-FileCopyrightText: 2026 RdWing
// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
* Adapted from src/DvmConsole.Media/DmrLateEntryMetadata.cs
* in DVM Console NEO (https://github.com/RdWing/dvmconsole), AGPL-3.0-only.
*/
using System;
using fnecore.EDAC;

namespace fnecore.DMR
{
    /// <summary>Encodes and collects the protected late-entry message indicator in DMR AMBE C3 bits.</summary>
    internal sealed class DmrLateEntryMessageIndicator
    {
        private const int VoiceBurstsPerSuperframe = 6;
        private const int CodewordsPerBurst = DmrVoicePacketCodec.CodewordsPerPacket;
        private const int FragmentBits = 4;
        private static readonly int[] C3PrefixWireBitPositions = { 71, 67, 63, 59 };
        private readonly byte[,] fragments = new byte[VoiceBurstsPerSuperframe, CodewordsPerBurst];
        private int nextVoiceBurst;

        public DmrLateEntryMessageIndicator() { }

        public DmrLateEntryMessageIndicator(ReadOnlySpan<byte> messageIndicator)
        {
            if (messageIndicator.Length != DmrPrivacyAlgorithms.MessageIndicatorBytes)
                throw new ArgumentException("DMR late-entry metadata requires a four-byte message indicator.", nameof(messageIndicator));

            ulong protectedMi = ((ulong)ReadUInt32BigEndian(messageIndicator) << FragmentBits) | CalculateCrc4(messageIndicator);
            for (int codeword = 0; codeword < CodewordsPerBurst; codeword++)
            {
                uint data = (uint)((protectedMi >> (24 - codeword * 12)) & 0xFFF);
                uint encoded = Golay24128.encode24128(data);
                uint parity = encoded & 0xFFF;
                for (int burst = 0; burst < 3; burst++)
                {
                    int shift = 8 - burst * FragmentBits;
                    fragments[burst, codeword] = (byte)((data >> shift) & 0x0F);
                    fragments[burst + 3, codeword] = (byte)((parity >> shift) & 0x0F);
                }
            }
        }

        public void ApplyFragment(Span<byte> codeword, int voiceBurst, int codewordIndex)
        {
            ValidateCoordinates(codeword, voiceBurst, codewordIndex);
            WriteC3Fragment(codeword, fragments[voiceBurst, codewordIndex]);
        }

        public bool AddVoiceBurst(byte voiceBurst, ReadOnlySpan<byte> ambe, out byte[] messageIndicator)
        {
            messageIndicator = Array.Empty<byte>();
            if (voiceBurst >= VoiceBurstsPerSuperframe || ambe.Length < DmrVoicePacketCodec.AmbeBytes)
            {
                Reset();
                return false;
            }
            if (voiceBurst == 0)
                nextVoiceBurst = 0;
            if (voiceBurst != nextVoiceBurst)
            {
                Reset();
                return false;
            }

            for (int codeword = 0; codeword < CodewordsPerBurst; codeword++)
            {
                fragments[voiceBurst, codeword] = ReadC3Fragment(
                    ambe.Slice(codeword * DmrVoicePacketCodec.CodewordBytes, DmrVoicePacketCodec.CodewordBytes));
            }
            nextVoiceBurst++;
            if (nextVoiceBurst < VoiceBurstsPerSuperframe)
                return false;
            nextVoiceBurst = 0;
            return TryDecode(out messageIndicator);
        }

        public void Reset()
        {
            Array.Clear(fragments, 0, fragments.Length);
            nextVoiceBurst = 0;
        }

        private bool TryDecode(out byte[] messageIndicator)
        {
            ulong protectedMi = 0;
            for (int codeword = 0; codeword < CodewordsPerBurst; codeword++)
            {
                uint data = 0;
                uint parity = 0;
                for (int burst = 0; burst < 3; burst++)
                {
                    data = (data << FragmentBits) | fragments[burst, codeword];
                    parity = (parity << FragmentBits) | fragments[burst + 3, codeword];
                }
                uint decoded = 0;
                if (!Golay24128.decode24128((data << 12) | parity, ref decoded))
                {
                    messageIndicator = Array.Empty<byte>();
                    return false;
                }
                protectedMi = (protectedMi << 12) | decoded;
            }
            messageIndicator = new[]
            {
                (byte)(protectedMi >> 28), (byte)(protectedMi >> 20),
                (byte)(protectedMi >> 12), (byte)(protectedMi >> 4)
            };
            return (protectedMi & 0x0F) == CalculateCrc4(messageIndicator);
        }

        private static byte CalculateCrc4(ReadOnlySpan<byte> messageIndicator)
        {
            const int polynomial = 0x13;
            ulong dividend = (ulong)ReadUInt32BigEndian(messageIndicator) << FragmentBits;
            for (int bit = 35; bit >= FragmentBits; bit--)
                if ((dividend & (1UL << bit)) != 0)
                    dividend ^= (ulong)polynomial << (bit - FragmentBits);
            return (byte)((dividend & 0x0F) ^ 0x0F);
        }

        private static byte ReadC3Fragment(ReadOnlySpan<byte> codeword)
        {
            byte fragment = 0;
            foreach (int bitPosition in C3PrefixWireBitPositions)
                fragment = (byte)((fragment << 1) | ReadBit(codeword, bitPosition));
            return fragment;
        }

        private static void WriteC3Fragment(Span<byte> codeword, byte fragment)
        {
            for (int index = 0; index < C3PrefixWireBitPositions.Length; index++)
                WriteBit(codeword, C3PrefixWireBitPositions[index], fragment >> (3 - index));
        }

        private static int ReadBit(ReadOnlySpan<byte> bytes, int bit)
        {
            return (bytes[bit / 8] >> (7 - bit % 8)) & 1;
        }

        private static void WriteBit(Span<byte> bytes, int bit, int value)
        {
            byte mask = (byte)(1 << (7 - bit % 8));
            if ((value & 1) != 0)
                bytes[bit / 8] |= mask;
            else
                bytes[bit / 8] &= (byte)~mask;
        }

        private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> value)
        {
            return (uint)(value[0] << 24 | value[1] << 16 | value[2] << 8 | value[3]);
        }

        private static void ValidateCoordinates(ReadOnlySpan<byte> codeword, int voiceBurst, int codewordIndex)
        {
            if (codeword.Length != DmrVoicePacketCodec.CodewordBytes)
                throw new ArgumentException("DMR late-entry metadata requires one AMBE codeword.", nameof(codeword));
            if ((uint)voiceBurst >= VoiceBurstsPerSuperframe)
                throw new ArgumentOutOfRangeException(nameof(voiceBurst));
            if ((uint)codewordIndex >= CodewordsPerBurst)
                throw new ArgumentOutOfRangeException(nameof(codewordIndex));
        }
    }

    /// <summary>DMR burst-F single-burst or reverse-channel signaling.</summary>
    public struct DmrBurstFSignaling
    {
        private const int InformationBits = 11;
        private const int EncodedBits = 32;
        private static readonly int[] Deinterleave =
        {
            0, 17, 2, 19, 4, 21, 6, 23, 8, 25, 10, 27, 12, 29, 14, 31,
            16, 1, 18, 3, 20, 5, 22, 7, 24, 9, 26, 11, 28, 13, 30, 15
        };
        private static readonly int[] Placement =
        {
            0, 16, 1, 17, 2, 18, 3, 19, 4, 20, 5, 21, 6, 22, 7, 23,
            8, 24, 9, 25, 10, 26, 11, 27, 12, 28, 13, 29, 14, 30, 15, 31
        };

        public DmrBurstFSignaling(bool isReverseChannel, ushort payload)
        {
            IsReverseChannel = isReverseChannel;
            Payload = payload;
        }

        public bool IsReverseChannel { get; }
        public ushort Payload { get; }
        public byte AlgorithmId => (byte)(Payload & 0x07);
        public byte KeyId => (byte)(Payload >> 3);

        public static DmrBurstFSignaling EncryptionIdentifiers(byte algorithmId, byte keyId)
        {
            if (algorithmId > 0x07)
                throw new ArgumentOutOfRangeException(nameof(algorithmId));
            if (keyId == 0)
                throw new ArgumentOutOfRangeException(nameof(keyId));
            return new DmrBurstFSignaling(false, (ushort)((keyId << 3) | algorithmId));
        }

        public void Encode(Span<byte> frame)
        {
            if (frame.Length < DmrVoicePacketCodec.FrameBytes)
                throw new ArgumentException("DMR burst-F signaling requires a complete voice burst.", nameof(frame));
            if (Payload >= 1 << InformationBits)
                throw new InvalidOperationException("DMR burst-F signaling payload exceeds 11 bits.");

            bool[] matrix = new bool[EncodedBits];
            for (int bit = 0; bit < InformationBits; bit++)
                matrix[bit] = ((Payload >> (InformationBits - 1 - bit)) & 1) != 0;
            Hamming.encode16114(ref matrix);
            for (int bit = 0; bit < 16; bit++)
                matrix[bit + 16] = IsReverseChannel ? !matrix[bit] : matrix[bit];

            bool[] interleaved = new bool[EncodedBits];
            for (int bit = 0; bit < EncodedBits; bit++)
                interleaved[bit] = matrix[Placement[Deinterleave[bit]]];
            WriteMiddleBits(frame, interleaved);
        }

        public static bool TryDecode(ReadOnlySpan<byte> frame, bool isReverseChannel, out DmrBurstFSignaling signaling)
        {
            signaling = default(DmrBurstFSignaling);
            if (frame.Length < DmrVoicePacketCodec.FrameBytes)
                return false;
            bool[] interleaved = new bool[EncodedBits];
            ReadMiddleBits(frame, interleaved);
            bool[] matrix = new bool[EncodedBits];
            for (int bit = 0; bit < EncodedBits; bit++)
                matrix[Placement[Deinterleave[bit]]] = interleaved[bit];
            if (!Hamming.decode16114(matrix))
                return false;
            for (int bit = 0; bit < 16; bit++)
            {
                bool expectedParity = isReverseChannel ? !matrix[bit] : matrix[bit];
                if (matrix[bit + 16] != expectedParity)
                    return false;
            }
            ushort payload = 0;
            for (int bit = 0; bit < InformationBits; bit++)
                payload = (ushort)((payload << 1) | (matrix[bit] ? 1 : 0));
            signaling = new DmrBurstFSignaling(isReverseChannel, payload);
            return true;
        }

        private static void ReadMiddleBits(ReadOnlySpan<byte> frame, bool[] bits)
        {
            for (int bit = 0; bit < EncodedBits; bit++)
            {
                int frameBit = 116 + bit;
                bits[bit] = (frame[frameBit / 8] & (1 << (7 - frameBit % 8))) != 0;
            }
        }

        private static void WriteMiddleBits(Span<byte> frame, bool[] bits)
        {
            for (int bit = 0; bit < EncodedBits; bit++)
            {
                int frameBit = 116 + bit;
                byte mask = (byte)(1 << (7 - frameBit % 8));
                if (bits[bit]) frame[frameBit / 8] |= mask;
                else frame[frameBit / 8] &= (byte)~mask;
            }
        }
    }
}
