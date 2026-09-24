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
    /// <summary>Clear DMR voice and signaling inside the FNE network envelope.</summary>
    public static class DMRFrame
    {
        public const int HeaderBytes = 20;
        public const int FrameBytes = 33;
        public const int PacketBytes = 55 + 8;
        public const int CodewordBytes = 9;
        public const int VoiceBytes = 27;

        private static readonly byte[] VoiceSync = { 0x75, 0x5F, 0xD7, 0xDF, 0x75, 0xF7 };
        private static readonly byte[] DataSync = { 0xDF, 0xF5, 0x7D, 0x75, 0xDF, 0x5D };
        private static readonly byte[] Silence = { 0xB9, 0xE8, 0x81, 0x52, 0x61, 0x73, 0x00, 0x2A, 0x6B };

        /// <summary>Starts a clear call. Encryption requests are rejected, never downgraded.</summary>
        public static byte[] EncodeHeader(DMRCallData call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            call.CheckActive();
            if (call.Transmitting || call.Receiving)
                throw new InvalidOperationException("Use a new DMR call instance for each stream.");
            if (call.TxStreamID == 0 || call.SrcId == 0 || call.SrcId > 0xFFFFFF ||
                call.DstId == 0 || call.DstId > 0xFFFFFF || call.Slot < 1 || call.Slot > 2 || call.ColorCode > 15)
                throw new ArgumentException("DMR requires a stream, 24-bit addresses, slot 1/2, and color code 0-15.", nameof(call));
            if (call.AlgorithmId != 0 || call.KeyId != 0 || (call.ServiceOptions & 0x40) != 0 || call.IsEncrypted)
                throw new NotSupportedException("Encrypted DMR is not available in this implementation.");
            if (call.MFId != 0 || (call.ServiceOptions & 0x30) != 0)
                throw new NotSupportedException("Only standard DMR voice service options are supported.");

            call.Control = new LC
            {
                SrcId = call.SrcId, DstId = call.DstId,
                FLCO = (byte)(call.Group ? DMRFLCO.FLCO_GROUP : DMRFLCO.FLCO_PRIVATE),
                Emergency = (call.ServiceOptions & 0x80) != 0,
                Broadcast = (call.ServiceOptions & 0x08) != 0,
                OVCM = (call.ServiceOptions & 0x04) != 0,
                Priority = (byte)(call.ServiceOptions & 3)
            };
            call.Source = call.SrcId;
            call.Destination = call.DstId;
            call.Stream = call.TxStreamID;
            call.TimeSlot = call.Slot;
            call.AccessCode = call.ColorCode;
            call.Options = call.ServiceOptions;
            call.GroupCall = call.Group;
            call.Embedded.SetLC(call.Control);
            call.Transmitting = call.HasCallInfo = true;
            return EncodeControl(call, DMRDataType.VOICE_LC_HEADER);
        }

        /// <summary>Encodes three consecutive 20 ms AMBE codewords into one voice burst.</summary>
        public static byte[] EncodeVoice(DMRCallData call, ReadOnlySpan<byte> voice)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            call.CheckTransmit();
            if (voice.Length != VoiceBytes) throw new ArgumentException("DMR voice requires three nine-byte codewords.", nameof(voice));
            byte[] frame = call.Frame;
            Array.Clear(frame, 0, frame.Length);
            voice.Slice(0, 13).CopyTo(frame);
            frame[13] = (byte)(voice[13] & 0xF0);
            frame[19] = (byte)(voice[13] & 0x0F);
            voice.Slice(14, 13).CopyTo(frame.AsSpan(20));

            byte burst = call.VoiceBurst;
            if (burst == 0)
                WriteSync(frame, VoiceSync);
            else
            {
                byte lcss = call.Embedded.GetData(ref frame, burst);
                new EMB { ColorCode = call.ColorCode, LCSS = lcss }.Encode(ref frame);
            }
            call.FrameType = burst == 0 ? FrameType.VOICE_SYNC : FrameType.VOICE;
            byte[] packet = Pack(call, frame, (byte)(burst == 0 ? 0x10 : burst));
            call.VoiceBurst = (byte)((burst + 1) % 6);
            return packet;
        }

        /// <summary>Creates a silence burst for bounded superframe completion.</summary>
        public static byte[] EncodeSilence(DMRCallData call)
        {
            Span<byte> voice = stackalloc byte[VoiceBytes];
            for (int i = 0; i < 3; i++) Silence.AsSpan().CopyTo(voice.Slice(i * CodewordBytes));
            return EncodeVoice(call, voice);
        }

        /// <summary>Ends a call once any remaining voice bursts have been completed.</summary>
        public static byte[] EncodeRelease(DMRCallData call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            call.CheckTransmit();
            if (call.PendingVoiceBursts != 0)
                throw new InvalidOperationException("Complete the DMR superframe before release.");
            byte[] packet = EncodeControl(call, DMRDataType.TERMINATOR_WITH_LC);
            call.IsReleased = true;
            return packet;
        }

        private static byte[] EncodeControl(DMRCallData call, DMRDataType type)
        {
            byte[] frame = call.Frame;
            Array.Clear(frame, 0, frame.Length);
            FullLC.Encode(call.Control, ref frame, type);
            new SlotType { ColorCode = call.ColorCode, DataType = (byte)type }.GetData(ref frame);
            WriteSync(frame, DataSync);
            call.FrameType = FrameType.DATA_SYNC;
            call.DataType = type;
            return Pack(call, frame, (byte)(0x20 | (byte)type));
        }

        private static byte[] Pack(DMRCallData call, byte[] frame, byte type)
        {
            byte[] packet = new byte[PacketBytes];
            packet[0] = (byte)'D'; packet[1] = (byte)'M'; packet[2] = (byte)'R'; packet[3] = (byte)'D';
            packet[4] = call.Sequence++;
            for (int i = 0; i < 3; i++)
            {
                packet[5 + i] = (byte)(call.SrcId >> (16 - i * 8));
                packet[8 + i] = (byte)(call.DstId >> (16 - i * 8));
            }
            packet[15] = (byte)(type | (call.Slot == 2 ? 0x80 : 0) | (call.Group ? 0 : 0x40));
            frame.CopyTo(packet, HeaderBytes);
            return packet;
        }

        private static void WriteSync(byte[] frame, byte[] sync)
        {
            frame[13] &= 0xF0;
            Array.Clear(frame, 14, 5);
            frame[19] &= 0x0F;
            for (int i = 0; i < sync.Length; i++)
            {
                frame[13 + i] |= (byte)(sync[i] >> 4);
                frame[14 + i] |= (byte)(sync[i] << 4);
            }
        }

        public static bool IsVoicePacket(ReadOnlySpan<byte> packet)
        {
            // Older peers omit the trailing transport padding.
            if (packet.Length < HeaderBytes + FrameBytes + 2 || packet[0] != 'D' || packet[1] != 'M' || packet[2] != 'R' || packet[3] != 'D')
                return false;
            int type = packet[15] & 0x3F;
            return (type >= 1 && type <= 5) || type == 0x10 || type == 0x20 || type == 0x21 || type == 0x22;
        }

        /// <summary>Accepts signaling or returns 27 bytes of confirmed-clear voice; invalid packets leave audio empty.</summary>
        public static int Decode(DMRCallData call, ReadOnlySpan<byte> packet, Span<byte> voice)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            call.LastPacketAccepted = false;
            call.CheckActive();
            if (call.Transmitting) throw new InvalidOperationException("Use a separate DMR state for receive.");
            if (voice.Length < VoiceBytes) throw new ArgumentException("DMR voice buffer is too short.", nameof(voice));
            voice.Clear();
            if (!IsVoicePacket(packet)) return 0;
            uint source = (uint)((packet[5] << 16) | (packet[6] << 8) | packet[7]);
            uint destination = (uint)((packet[8] << 16) | (packet[9] << 8) | packet[10]);
            if (source == 0 || source != call.SrcId || destination == 0 || destination != call.DstId ||
                call.TxStreamID == 0 || call.Slot != ((packet[15] & 0x80) != 0 ? 2 : 1) ||
                call.Group != ((packet[15] & 0x40) == 0)) return 0;
            int sequence = packet[4];
            int delta = call.LastSequence < 0 ? 1 : (sequence - call.LastSequence + 256) % 256;
            if (delta == 0 || delta >= 128) return 0;
            if (delta != 1)
            {
                call.Embedded.Reset();
                call.LastVoiceBurst = -1;
            }

            byte[] frame = call.Frame;
            packet.Slice(HeaderBytes, FrameBytes).CopyTo(frame);
            byte type = (byte)(packet[15] & 0x3F);
            if ((type & 0x20) != 0)
            {
                DMRDataType dataType = (DMRDataType)(type & 0x0F);
                SlotType slotType = new SlotType(frame);
                if (slotType.DataType != (byte)dataType) return 0;
                if (dataType == DMRDataType.VOICE_PI_HEADER)
                {
                    // A PI burst must never cause encrypted codewords to be played as clear voice.
                    call.IsEncrypted = true;
                    PrivacyLC privacy = FullLC.DecodePI(frame);
                    if (privacy != null && privacy.DstId == call.DstId)
                    {
                        call.AlgorithmId = privacy.AlgId;
                        call.KeyId = (ushort)privacy.KId;
                    }
                }
                else
                {
                    LC control = FullLC.Decode(frame, dataType);
                    if (!AcceptControl(call, control)) return 0;
                    if (dataType == DMRDataType.TERMINATOR_WITH_LC) call.IsReleased = true;
                }
                call.ColorCode = slotType.ColorCode;
                call.DataType = dataType;
                call.FrameType = FrameType.DATA_SYNC;
                call.Embedded.Reset();
                call.LastVoiceBurst = -1;
            }
            else
            {
                int burst = type == 0x10 ? 0 : type;
                if (burst == 0 || call.LastVoiceBurst + 1 != burst) call.Embedded.Reset();
                if (burst != 0)
                {
                    EMB emb = new EMB();
                    emb.Decode(frame);
                    call.ColorCode = emb.ColorCode;
                    // Reverse-channel signaling does not identify this voice call.
                    if (emb.PI)
                        call.Embedded.Reset();
                    else if (burst >= 1 && burst <= 4 && call.Embedded.AddData(ref frame, emb.LCSS))
                    {
                        if (!AcceptControl(call, call.Embedded.GetLC()))
                        {
                            call.HasCallInfo = false;
                            call.Embedded.Reset();
                        }
                    }
                }
                call.LastVoiceBurst = burst;
                call.FrameType = burst == 0 ? FrameType.VOICE_SYNC : FrameType.VOICE;
            }
            call.LastSequence = sequence;
            call.Receiving = call.LastPacketAccepted = true;
            if ((type & 0x20) != 0 || !call.HasCallInfo || call.IsEncrypted) return 0;
            frame.AsSpan(0, 13).CopyTo(voice);
            voice[13] = (byte)((frame[13] & 0xF0) | (frame[19] & 0x0F));
            frame.AsSpan(20, 13).CopyTo(voice.Slice(14));
            return VoiceBytes;
        }

        private static bool AcceptControl(DMRCallData call, LC control)
        {
            if (control == null || control.SrcId != call.SrcId || control.DstId != call.DstId ||
                control.FLCO != (byte)(call.Group ? DMRFLCO.FLCO_GROUP : DMRFLCO.FLCO_PRIVATE)) return false;
            call.Control = control;
            call.HasCallInfo = true;
            call.MFId = control.FID;
            call.ServiceOptions = control.GetBytes()[2];
            // Unknown manufacturer/protected signaling is not evidence of a clear call.
            call.IsEncrypted |= control.Encrypted || control.PF || control.FID != 0;
            return true;
        }
    }
}
