// SPDX-FileCopyrightText: 2026 RdWing
// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
* Adapted from src/DvmConsole.Media/NxdnVoiceSignalingCycle.cs
* in DVM Console NEO (https://github.com/RdWing/dvmconsole), AGPL-3.0-only.
*/
#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;

namespace fnecore.NXDN
{
    // Owns the eight-frame NXDN DES/AES voice signaling cycle. The current IV
    // encrypts both four-frame superframes. The second superframe advertises the
    // successor IV in SACCH, and that successor becomes active only after its last
    // fragment has accompanied a voice frame.
    internal sealed class NxdnVoiceSignalingCycle : IDisposable
    {
        private const int FramesPerMessage = 4;
        private const int FramesPerPrivacyCycle = FramesPerMessage * 2;

        private readonly ushort sourceId;
        private readonly ushort destinationId;
        private readonly bool group;
        private readonly byte cipherType;
        private readonly byte keyId;
        private byte[] currentMessageIndicator;
        private byte[]? successorMessageIndicator;
        private int frameIndex;
        private bool disposed;

        public NxdnVoiceSignalingCycle(
            uint sourceId,
            uint destinationId,
            bool group,
            byte cipherType,
            byte keyId,
            ReadOnlyMemory<byte> messageIndicator)
        {
            this.sourceId = checked((ushort)sourceId);
            this.destinationId = checked((ushort)destinationId);
            this.group = group;
            this.cipherType = cipherType;
            this.keyId = keyId;
            currentMessageIndicator = messageIndicator.ToArray();
        }

        public byte SuperframePart => (byte)(frameIndex % FramesPerMessage);

        public NxdnVoicePacketCodec.CallMetadata CurrentMetadata
        {
            get
            {
                if (disposed)
                throw new ObjectDisposedException(nameof(NxdnVoiceSignalingCycle));
                if (!UsesRotatingMessageIndicator || frameIndex < FramesPerMessage)
                {
                    return new NxdnVoicePacketCodec.CallMetadata(
                        NxdnVoicePacketCodec.VoiceCallMessageType,
                        sourceId,
                        destinationId,
                        group,
                        cipherType,
                        keyId,
                        Array.Empty<byte>());
                }

                successorMessageIndicator ??=
                    NxdnInitializationVectorGenerator.GetNextSeed(currentMessageIndicator);
                return new NxdnVoicePacketCodec.CallMetadata(
                    NxdnVoicePacketCodec.VoiceCallIvMessageType,
                    0,
                    0,
                    true,
                    0,
                    0,
                    successorMessageIndicator.ToArray());
            }
        }

        public void AdvanceAfterVoiceFrame(NxdnPrivacyProcessor? privacyProcessor)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(NxdnVoiceSignalingCycle));
            frameIndex++;
            int cycleLength = UsesRotatingMessageIndicator
                ? FramesPerPrivacyCycle
                : FramesPerMessage;
            if (frameIndex < cycleLength)
                return;

            if (UsesRotatingMessageIndicator)
            {
                if (successorMessageIndicator is null || privacyProcessor is null)
                    throw new InvalidOperationException("NXDN privacy signaling completed without a successor IV.");
                privacyProcessor.ResetMessageIndicator(successorMessageIndicator);
                if (currentMessageIndicator.Length > 0)
                    CryptographicOperations.ZeroMemory(currentMessageIndicator);
                currentMessageIndicator = successorMessageIndicator;
                successorMessageIndicator = null;
            }
            frameIndex = 0;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            if (currentMessageIndicator.Length > 0)
                CryptographicOperations.ZeroMemory(currentMessageIndicator);
            if (successorMessageIndicator != null)
                CryptographicOperations.ZeroMemory(successorMessageIndicator);
            currentMessageIndicator = Array.Empty<byte>();
            successorMessageIndicator = null;
            disposed = true;
        }

        private bool UsesRotatingMessageIndicator
            => (cipherType == NxdnPrivacyAlgorithms.Des || cipherType == NxdnPrivacyAlgorithms.Aes256);
    }
}
