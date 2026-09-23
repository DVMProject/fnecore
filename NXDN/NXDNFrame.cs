// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* Copyright (C) 2026 C. Lovell, Dev_Ranger
*/

using System;
using System.Security.Cryptography;
using fnecore.NXDN.LC;

namespace fnecore.NXDN
{
    /// <summary>
    /// Handles conventional NXDN voice frames and FNE transport. The application
    /// remains responsible for PCM audio, vocoder synthesis, buffering, and pacing.
    /// </summary>
    public static class NXDNFrame
    {
        public const int FrameBytes = 48;
        public const int CodewordBytes = 9;
        public const int VoiceBytes = 36;
        public const int HeaderBytes = 24;
        public const int FrameOffset = HeaderBytes + 2;
        public const int PacketBytes = FrameOffset + FrameBytes + 4;

        /// <summary>Writes the same transport envelope used by DVMHost.</summary>
        public static void CreateMessageHeader(NXDNMessageType type, NXDNCallData call, Span<byte> packet)
        {
            if (packet.Length < HeaderBytes) throw new ArgumentException("Short NXDN header.", nameof(packet));
            if (call.SrcId == 0 || call.SrcId > 65535 || call.DstId == 0 || call.DstId > 65535 ||
                call.Ran > 63 || call.AlgorithmId > 3 || call.KeyId > 63)
                throw new ArgumentException("Invalid NXDN call metadata.", nameof(call));
            packet.Slice(0, HeaderBytes).Clear();
            packet[0] = (byte)'N'; packet[1] = (byte)'X'; packet[2] = (byte)'D'; packet[3] = (byte)'D';
            packet[4] = (byte)type;
            packet[6] = (byte)(call.SrcId >> 8); packet[7] = (byte)call.SrcId;
            packet[9] = (byte)(call.DstId >> 8); packet[10] = (byte)call.DstId;
            packet[15] = (byte)(call.Group ? 0 : 0x40);
            packet[23] = FrameOffset + FrameBytes;
        }

        private static bool Sync(ReadOnlySpan<byte> frame) => frame.Length >= FrameBytes &&
            frame[0] == 0xCD && frame[1] == 0xF5 && (frame[2] & 0xF0) == 0x90;

        /// <summary>Accepts Host modem-tagged payloads and earlier untagged client frames.</summary>
        public static bool TryExtractFrame(ReadOnlySpan<byte> packet, Span<byte> frame)
        {
            if (frame.Length < FrameBytes || packet.Length < HeaderBytes + FrameBytes ||
                packet[0] != 'N' || packet[1] != 'X' || packet[2] != 'D' || packet[3] != 'D') return false;
            int declared = packet[23];
            int offset = HeaderBytes;
            if (Sync(packet.Slice(offset)) && (declared == 48 || declared == 72)) { }
            else if (packet.Length >= HeaderBytes + 2 + FrameBytes &&
                Sync(packet.Slice(HeaderBytes + 2)) && (declared == 50 || declared == 74)) offset += 2;
            else return false;
            packet.Slice(offset, FrameBytes).CopyTo(frame);
            Whiten(frame);
            return true;
        }

        private static void Whiten(Span<byte> frame)
        {
            int register = 0xE4;
            for (int bit = 20; bit < 384; bit += 2)
            {
                NXDNChannelCoding.Put(frame, bit, NXDNChannelCoding.Bit(frame, bit) ^ (register & 1));
                register = (register >> 1) | (((register ^ (register >> 4)) & 1) << 8);
            }
        }

        private static void EncodeLich(Span<byte> frame, bool voice, int option)
        {
            frame[0] = 0xCD; frame[1] = 0xF5; frame[2] = 0x90;
            int info = 0x80 | (voice ? 0x20 : 0) | (option << 2);
            int high = info >> 4;
            info |= (high ^ (high >> 1) ^ (high >> 2) ^ (high >> 3)) & 1;
            for (int i = 0; i < 8; i++)
            {
                NXDNChannelCoding.Put(frame, 20 + i * 2, info >> (7 - i));
                NXDNChannelCoding.Put(frame, 21 + i * 2, 1);
            }
        }

        private static bool DecodeLich(ReadOnlySpan<byte> frame, out bool voice, out int option)
        {
            int value = 0;
            for (int i = 0; i < 8; i++) value = value * 2 + NXDNChannelCoding.Bit(frame, 20 + i * 2);
            int high = value >> 4;
            voice = (value & 0x30) == 0x20;
            option = (value >> 2) & 3;
            return ((value >> 6) == 1 || (value >> 6) == 2) &&
                ((value & 0x30) == 0 || voice) &&
                ((high ^ (high >> 1) ^ (high >> 2) ^ (high >> 3) ^ value) & 1) == 0;
        }

        private static byte[] NewPacket(NXDNMessageType type, NXDNCallData call)
        {
            byte[] packet = new byte[PacketBytes];
            CreateMessageHeader(type, call, packet);
            // Host reads two modem bytes before the 48-byte RF frame.
            packet[HeaderBytes] = type == NXDNMessageType.MESSAGE_TYPE_TX_REL ? (byte)0x03 : (byte)0x01;
            return packet;
        }

        /// <summary>Creates VCALL and, for DES/AES, the initial VCALL_IV.</summary>
        public static byte[] EncodeHeader(NXDNCallData call)
        {
            call.CheckActive();
            if (call.Transmitting || call.HasCallInfo) throw new InvalidOperationException("NXDN header already sent.");
            byte[] packet = NewPacket(NXDNMessageType.MESSAGE_TYPE_VCALL, call);
            if (call.IsEncrypted)
            {
                if (call.AlgorithmId >= NXDNCrypto.Des)
                {
                    if (call.MessageIndicator == null || call.MessageIndicator.Length != 8 ||
                        NXDNCrypto.ReadIndicator(call.MessageIndicator) == 0)
                        call.MessageIndicator = NXDNCrypto.CreateMessageIndicator();
                    call.CurrentIndicator = NXDNCrypto.ReadIndicator(call.MessageIndicator);
                    call.IndicatorKnown = true;
                }
                if (!call.Crypto.Prepare(call.AlgorithmId, call.KeyId, call.MessageIndicator))
                    throw new InvalidOperationException("NXDN key unavailable; refusing clear fallback.");
            }
            Span<byte> frame = packet.AsSpan(FrameOffset, FrameBytes);
            EncodeLich(frame, false, 0);
            EncodeIdleSacch(frame, call.Ran);
            Span<byte> control = stackalloc byte[10];
            call.SetLinkControl(NXDNMessageType.MESSAGE_TYPE_VCALL);
            call.LinkControl.Encode(control);
            NXDNChannelCoding.Encode(control, frame, 96, false);
            if (call.AlgorithmId >= NXDNCrypto.Des)
            {
                call.LinkControl.MessageType = NXDNMessageType.MESSAGE_TYPE_VCALL_IV;
                call.LinkControl.MessageIndicator = call.CurrentIndicator;
                call.LinkControl.Encode(control);
            }
            NXDNChannelCoding.Encode(control, frame, 240, false);
            Whiten(frame);
            call.Transmitting = call.HasCallInfo = true;
            call.VoiceFrame = 0;
            call.PreparedSession = 0;
            call.TxSource = call.SrcId; call.TxDestination = call.DstId;
            call.TxAlgorithm = call.AlgorithmId; call.TxKey = call.KeyId;
            call.TxRan = call.Ran; call.TxGroup = call.Group;
            return packet;
        }

        private static void EncodeIdleSacch(Span<byte> frame, byte ran)
        {
            Span<byte> control = stackalloc byte[4];
            control.Clear();
            control[0] = (byte)(0xC0 | ran);
            control[1] = 0x10;
            NXDNChannelCoding.Encode(control, frame, 36, true);
        }

        /// <summary>Creates one 80 ms frame from four vocoder/FEC codewords.</summary>
        public static byte[] EncodeVoice(NXDNCallData call, ReadOnlySpan<byte> words, INxdnAmbeCodec codec)
        {
            call.CheckActive();
            if (!call.Transmitting || words.Length != VoiceBytes) throw new ArgumentException("NXDN requires an active call and four codewords.");
            call.CheckTransmitMetadata();
            byte[] packet = NewPacket(NXDNMessageType.MESSAGE_TYPE_VCALL, call);
            Span<byte> frame = packet.AsSpan(FrameOffset, FrameBytes);
            EncodeLich(frame, true, 3);
            if (!PrepareSession(call)) throw new InvalidOperationException("NXDN encryption is not synchronized.");
            int quarter = call.VoiceFrame % 4;
            call.SetLinkControl(NXDNMessageType.MESSAGE_TYPE_VCALL);
            if (call.AlgorithmId >= NXDNCrypto.Des && call.VoiceFrame % 8 >= 4)
            {
                call.LinkControl.MessageType = NXDNMessageType.MESSAGE_TYPE_VCALL_IV;
                call.LinkControl.MessageIndicator = NXDNCrypto.AdvanceIndicator(call.CurrentIndicator);
            }
            Span<byte> control = stackalloc byte[10];
            Span<byte> slow = stackalloc byte[4];
            slow.Clear();
            call.LinkControl.Encode(control);
            slow[0] = (byte)(((3 - quarter) << 6) | call.Ran);
            NXDNChannelCoding.Copy(control, quarter * 18, slow, 8, 18);
            NXDNChannelCoding.Encode(slow, frame, 36, true);
            for (int i = 0; i < 4; i++)
            {
                words.Slice(i * 9, 9).CopyTo(call.Word);
                if (call.IsEncrypted && !TransformVoice(call, codec, i))
                    throw new InvalidOperationException("NXDN encryption is not synchronized.");
                call.Word.AsSpan().CopyTo(frame.Slice(12 + i * 9, 9));
            }
            Whiten(frame);
            call.VoiceFrame++;
            return packet;
        }

        /// <summary>Creates an independently decodable release, without voice padding.</summary>
        public static byte[] EncodeRelease(NXDNCallData call)
        {
            call.CheckActive();
            if (!call.Transmitting) throw new InvalidOperationException("NXDN call was not started.");
            call.CheckTransmitMetadata();
            byte[] packet = NewPacket(NXDNMessageType.MESSAGE_TYPE_TX_REL, call);
            Span<byte> frame = packet.AsSpan(FrameOffset, FrameBytes);
            EncodeLich(frame, false, 0);
            EncodeIdleSacch(frame, call.Ran);
            Span<byte> control = stackalloc byte[10];
            call.SetLinkControl(NXDNMessageType.MESSAGE_TYPE_TX_REL);
            call.LinkControl.Encode(control);
            NXDNChannelCoding.Encode(control, frame, 96, false);
            NXDNChannelCoding.Encode(control, frame, 240, false);
            Whiten(frame);
            call.IsReleased = true;
            return packet;
        }

        private static bool TransformVoice(NXDNCallData call, INxdnAmbeCodec codec, int word)
        {
            if (codec == null) throw new ArgumentNullException(nameof(codec));
            codec.Decode(call.Word, call.VoiceBits);
            int position = (call.VoiceFrame % (call.AlgorithmId == NXDNCrypto.Ehr ? 4 : 8)) * 4 + word;
            if (!call.Crypto.Process(call.VoiceBits, position)) return false;
            codec.Encode(call.VoiceBits, call.Word);
            return true;
        }

        private static bool PrepareSession(NXDNCallData call)
        {
            if (!call.IsEncrypted) return true;
            if (call.AlgorithmId == NXDNCrypto.Ehr)
            {
                if (call.PreparedSession >= 0) return call.Crypto.HasKey();
                bool ready = call.Crypto.Prepare(call.AlgorithmId, call.KeyId, null);
                if (ready) call.PreparedSession = 0;
                return ready;
            }
            if (!call.IndicatorKnown) return false;
            int session = call.VoiceFrame / 8;
            while (call.PreparedSession >= 0 && call.PreparedSession < session)
            {
                call.CurrentIndicator = NXDNCrypto.AdvanceIndicator(call.CurrentIndicator);
                call.PreparedSession++;
            }
            if (call.PreparedSession == session && NXDNCrypto.ReadIndicator(call.MessageIndicator) == call.CurrentIndicator)
                return call.Crypto.HasKey();
            NXDNCrypto.WriteIndicator(call.CurrentIndicator, call.MessageIndicator);
            bool prepared = call.Crypto.Prepare(call.AlgorithmId, call.KeyId, call.MessageIndicator);
            if (prepared) call.PreparedSession = session;
            return prepared;
        }

        /// <summary>
        /// Decodes a packet to up to four clear vocoder codewords. Unknown crypto,
        /// incomplete late-entry signaling, duplicates, and stale packets are muted.
        /// </summary>
        public static int Decode(NXDNCallData call, ReadOnlySpan<byte> packet, ushort sequence,
            Span<byte> voice, INxdnAmbeCodec codec, Func<byte, ushort, byte[]> resolveKey)
        {
            if (call.Disposed) throw new ObjectDisposedException(nameof(NXDNCallData));
            if (call.Invalid || call.IsReleased) return 0;
            call.CheckActive();
            if (call.Transmitting) throw new InvalidOperationException("Use a separate receive call.");
            if (voice.Length < VoiceBytes) throw new ArgumentException("Voice buffer needs 36 bytes.", nameof(voice));
            Span<byte> frame = stackalloc byte[FrameBytes];
            if (!TryExtractFrame(packet, frame) || !DecodeLich(frame, out bool superframe, out int option)) return 0;
            if (packet[5] != 0 || packet[8] != 0 || (packet[6] * 256 + packet[7]) != call.SrcId ||
                (packet[9] * 256 + packet[10]) != call.DstId || ((packet[15] & 0x40) == 0) != call.Group) return 0;
            int gap = call.LastSequence < 0 ? 1 : (sequence - call.LastSequence + 65535) % 65535;
            if (sequence != ushort.MaxValue && (gap == 0 || gap > 32767)) return 0;
            if (sequence == ushort.MaxValue && superframe) return 0;
            if (gap != 1) call.FragmentQuarter = -1;
            if (gap > 64)
            {
                call.IndicatorKnown = false;
                call.PreparedSession = -1;
            }
            if (superframe && call.LastSequence >= 0) call.VoiceFrame += gap - 1;
            if (sequence != ushort.MaxValue) call.LastSequence = sequence;

            Span<byte> slow = stackalloc byte[4];
            bool validSlow = NXDNChannelCoding.Decode(frame, 36, slow, true);
            if (validSlow) call.Ran = (byte)(slow[0] & 63);
            Span<byte> control = stackalloc byte[10];
            bool nextIndicator = false;
            ulong nextValue = 0;
            for (int half = 0; half < 2; half++)
            {
                // Option 1 steals the first half; option 2 steals the second.
                bool stolen = option == 0 || option == half + 1;
                if (!stolen || !NXDNChannelCoding.Decode(frame, 96 + half * 144, control, false)) continue;
                if (!call.LinkControl.Decode(control)) continue;
                ApplyControl(call, call.LinkControl, !superframe, resolveKey);
            }
            if (call.IsReleased || call.Invalid) return 0;
            if (!superframe) return 0;

            if (!validSlow) call.FragmentQuarter = -1;
            else
            {
                int quarter = 3 - (slow[0] >> 6);
                if (!call.IndicatorKnown) call.VoiceFrame = (call.VoiceFrame / 4) * 4 + quarter;
                if (quarter == 0 || quarter == call.FragmentQuarter + 1)
                {
                    NXDNChannelCoding.Copy(slow, 8, call.Fragments, quarter * 18, 18);
                    call.FragmentQuarter = quarter;
                    if (quarter == 3)
                    {
                        if (call.LinkControl.Decode(call.Fragments))
                        {
                            if (call.LinkControl.MessageType == NXDNMessageType.MESSAGE_TYPE_VCALL_IV)
                            {
                                nextIndicator = call.HasCallInfo && call.AlgorithmId >= NXDNCrypto.Des && call.LinkControl.MessageIndicator != 0;
                                nextValue = call.LinkControl.MessageIndicator;
                            }
                            else ApplyControl(call, call.LinkControl, false, resolveKey);
                        }
                        call.FragmentQuarter = -1;
                    }
                }
                else call.FragmentQuarter = -1;
            }
            int count = 0;
            bool ready = call.HasCallInfo && !call.Invalid && PrepareSession(call);
            for (int word = 0; word < 4; word++)
            {
                bool stolen = option == 0 || option == word / 2 + 1;
                if (!ready || stolen) continue;
                frame.Slice(12 + word * 9, 9).CopyTo(call.Word);
                if (call.IsEncrypted && !TransformVoice(call, codec, word)) continue;
                call.Word.AsSpan().CopyTo(voice.Slice(count, 9));
                count += 9;
            }
            call.VoiceFrame++;
            if (nextIndicator)
            {
                call.CurrentIndicator = nextValue;
                call.IndicatorKnown = true;
                call.VoiceFrame = 0;
                call.PreparedSession = -1;
            }
            return count;
        }

        private static void ApplyControl(NXDNCallData call, RTCH control, bool header, Func<byte, ushort, byte[]> resolveKey)
        {
            byte type = (byte)control.MessageType;
            if (type == 3)
            {
                if (header && call.VoiceFrame == 0 && control.MessageIndicator != 0)
                {
                    call.CurrentIndicator = control.MessageIndicator;
                    call.IndicatorKnown = true;
                    call.PreparedSession = -1;
                }
                return;
            }
            if (type != 1 && type != 7 && type != 8) return;
            if (control.SrcId != call.SrcId || control.DstId != call.DstId || (control.CallType != 4) != call.Group) return;
            if (type == 7 || type == 8) { call.IsReleased = true; return; }
            if (control.TransmissionMode != 0) { call.Invalid = true; return; }
            if (call.HasCallInfo && (call.AlgorithmId != control.CipherType || call.KeyId != control.KeyId))
            {
                call.Invalid = true;
                call.Crypto.Reset();
                return;
            }
            call.HasCallInfo = true;
            call.AlgorithmId = control.CipherType;
            call.KeyId = control.KeyId;
            call.ServiceOptions = (byte)((control.Emergency ? 0x80 : 0) | (control.Priority ? 0x20 : 0));
            if (call.IsEncrypted && !call.Crypto.HasKey())
            {
                byte[] material = resolveKey?.Invoke(call.AlgorithmId, call.KeyId);
                if (material == null) return;
                try { call.Crypto.SetKey(call.KeyId, call.AlgorithmId, material); call.PreparedSession = -1; }
                finally { CryptographicOperations.ZeroMemory(material); }
            }
        }
    }
}
