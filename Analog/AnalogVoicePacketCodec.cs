// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment Core Library
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Project contributors: DVMProject authors
*
*/

using System;

namespace fnecore.Analog
{
    /// <summary>Encodes and decodes 20 ms G.711 mu-law group voice frames.</summary>
    public static class AnalogVoicePacketCodec
    {
        public const int SampleCount = 160;
        public const int PacketLength = 352;
        private const int AudioOffset = 20;
        private static readonly int[] SegmentEnds = { 63, 127, 255, 511, 1023, 2047, 4095, 8191 };

        public static byte[] Encode(uint sourceId, uint destinationId, byte sequence,
            AudioFrameType frameType, ReadOnlySpan<short> samples)
        {
            if (sourceId == 0 || sourceId > 0xFFFFFF || destinationId == 0 || destinationId > 0xFFFFFF)
                throw new ArgumentOutOfRangeException(nameof(sourceId), "Analog IDs must be between 1 and 16777215.");
            if (frameType != AudioFrameType.VOICE_START && frameType != AudioFrameType.VOICE &&
                frameType != AudioFrameType.TERMINATOR)
                throw new ArgumentOutOfRangeException(nameof(frameType));
            if (frameType != AudioFrameType.TERMINATOR && samples.Length != SampleCount)
                throw new ArgumentException("Analog voice requires 160 PCM samples.", nameof(samples));

            byte[] packet = new byte[PacketLength];
            packet[0] = (byte)'A'; packet[1] = (byte)'N'; packet[2] = (byte)'O'; packet[3] = (byte)'D';
            packet[4] = sequence;
            packet[5] = (byte)(sourceId >> 16); packet[6] = (byte)(sourceId >> 8); packet[7] = (byte)sourceId;
            packet[8] = (byte)(destinationId >> 16); packet[9] = (byte)(destinationId >> 8); packet[10] = (byte)destinationId;
            packet[14] = frameType == AudioFrameType.VOICE_START ? (byte)0x80 : (byte)0;
            packet[15] = (byte)frameType;
            if (frameType != AudioFrameType.TERMINATOR)
                for (int i = 0; i < SampleCount; i++)
                    packet[AudioOffset + i] = EncodeSample(samples[i]);
            return packet;
        }

        public static bool TryDecode(ReadOnlySpan<byte> packet, out short[] samples)
        {
            samples = null;
            if (packet.Length < 344 || packet[0] != 'A' || packet[1] != 'N' ||
                packet[2] != 'O' || packet[3] != 'D' || (packet[15] & 0x0F) > (byte)AudioFrameType.VOICE)
                return false;
            samples = new short[SampleCount];
            for (int i = 0; i < SampleCount; i++)
                samples[i] = DecodeSample(packet[AudioOffset + i]);
            return true;
        }

        public static byte EncodeSample(short sample)
        {
            int magnitude = sample >> 2;
            int mask = 0xFF;
            if (magnitude < 0)
            {
                magnitude = -magnitude;
                mask = 0x7F;
            }
            magnitude = Math.Min(magnitude, 8159) + 33;
            int segment = 0;
            while (segment < SegmentEnds.Length && magnitude > SegmentEnds[segment])
                segment++;
            return segment >= 8 ? (byte)(0x7F ^ mask) :
                (byte)(((segment << 4) | ((magnitude >> (segment + 1)) & 0x0F)) ^ mask);
        }

        public static short DecodeSample(byte value)
        {
            int code = (~value) & 0xFF;
            int magnitude = (((code & 0x0F) << 3) + 0x84) << ((code >> 4) & 7);
            return (short)((code & 0x80) != 0 ? 0x84 - magnitude : magnitude - 0x84);
        }
    }
}
