// SPDX-FileCopyrightText: 2026 RdWing
// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
* Adapted from src/DvmConsole.Media/NxdnPrivacy.cs
* in DVM Console NEO (https://github.com/RdWing/dvmconsole), AGPL-3.0-only.
*/
#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;

namespace fnecore.NXDN
{
    public static class NxdnPrivacyAlgorithms
    {
        public const byte Ehr = 0x01;
        public const byte Des = 0x02;
        public const byte Aes256 = 0x03;
        public const int MessageIndicatorBytes = 8;

        /// <summary>Maps an NXDN wire cipher to its FNE KMM key-service algorithm.</summary>
        public static byte ToKeyRequestAlgorithm(byte cipherType) => cipherType switch
        {
            Ehr => Ehr,
            Des => P25.P25Defines.P25_ALGO_DES,
            Aes256 => P25.P25Defines.P25_ALGO_AES,
            _ => 0
        };

        /// <summary>Returns zero for a key-service algorithm not used by NXDN.</summary>
        public static byte FromKeyRequestAlgorithm(byte algorithmId) => algorithmId switch
        {
            Ehr => Ehr,
            P25.P25Defines.P25_ALGO_DES => Des,
            P25.P25Defines.P25_ALGO_AES => Aes256,
            _ => 0
        };

        public static int KeyBytes(byte algorithmId) => algorithmId switch
        {
            Ehr => 2,
            Des => 8,
            Aes256 => 32,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithmId), "Unsupported NXDN privacy algorithm.")
        };
    }

    public sealed class NxdnPrivacyOptions
    {
        public NxdnPrivacyOptions(
            byte algorithmId,
            byte keyId,
            ReadOnlyMemory<byte> key,
            ReadOnlyMemory<byte> messageIndicator = default)
        {
            int expected = NxdnPrivacyAlgorithms.KeyBytes(algorithmId);
            if (keyId == 0 || keyId > 63)
                throw new ArgumentOutOfRangeException(nameof(keyId), "NXDN key IDs are between 1 and 63.");
            if (key.Length != expected)
                throw new ArgumentException($"NXDN cipher type {algorithmId} requires exactly {expected} key bytes.", nameof(key));
            if (algorithmId == NxdnPrivacyAlgorithms.Ehr)
            {
                ushort seed = (ushort)((key.Span[0] << 8) | key.Span[1]);
                if (seed == 0 || seed > 0x7FFF)
                    throw new ArgumentException("NXDN EHR requires a non-zero 15-bit seed.", nameof(key));
            }
            else if (messageIndicator.Length != NxdnPrivacyAlgorithms.MessageIndicatorBytes)
            {
                throw new ArgumentException("NXDN DES/AES privacy requires an 8-byte message indicator.", nameof(messageIndicator));
            }
            AlgorithmId = algorithmId;
            KeyId = keyId;
            Key = key.ToArray();
            MessageIndicator = algorithmId == NxdnPrivacyAlgorithms.Ehr ? Array.Empty<byte>() : messageIndicator.ToArray();
        }

        public byte AlgorithmId { get; }
        public byte KeyId { get; }
        public ReadOnlyMemory<byte> Key { get; }
        public ReadOnlyMemory<byte> MessageIndicator { get; }

        public static NxdnPrivacyOptions CreateRandom(byte algorithmId, byte keyId, ReadOnlyMemory<byte> key)
        {
            byte[] mi = algorithmId == NxdnPrivacyAlgorithms.Ehr
                ? Array.Empty<byte>()
                : new byte[NxdnPrivacyAlgorithms.MessageIndicatorBytes];
            if (mi.Length > 0)
                RandomNumberGenerator.Fill(mi);
            return new NxdnPrivacyOptions(algorithmId, keyId, key, mi);
        }
    }

    // Symmetric transform over the 49 natural AMBE parameter bits. The native
    // adapter handles NXDN FEC/interleave after this operation.
    public sealed class NxdnPrivacyProcessor : IDisposable
    {
        private readonly INxdnAmbeCodec? interleaver;
        private readonly byte algorithmId;
        private readonly byte[] key;
        private byte[] messageIndicator;
        private byte[] stream = Array.Empty<byte>();
        private ICryptoTransform? encryptor;
        private SymmetricAlgorithm? cipher;
        private byte[] register = Array.Empty<byte>();
        private ushort ehrSeed;
        private ushort ehrState;
        private int codewordIndex;
        private int streamBit;
        private bool disposed;

        public NxdnPrivacyProcessor(NxdnPrivacyOptions options, INxdnAmbeCodec? codec = null)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            interleaver = codec;
            algorithmId = options.AlgorithmId;
            key = options.Key.ToArray();
            messageIndicator = options.MessageIndicator.ToArray();
            Prepare();
        }

        public ReadOnlyMemory<byte> MessageIndicator => messageIndicator;

        public void ResetMessageIndicator(ReadOnlySpan<byte> nextMessageIndicator)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(NxdnPrivacyProcessor));
            if (algorithmId == NxdnPrivacyAlgorithms.Ehr)
                throw new InvalidOperationException("NXDN EHR does not use a message indicator.");
            if (nextMessageIndicator.Length != NxdnPrivacyAlgorithms.MessageIndicatorBytes)
                throw new ArgumentException("NXDN DES/AES privacy requires an 8-byte message indicator.", nameof(nextMessageIndicator));
            encryptor?.Dispose();
            encryptor = null;
            cipher?.Dispose();
            cipher = null;
            if (messageIndicator.Length > 0)
                CryptographicOperations.ZeroMemory(messageIndicator);
            if (register.Length > 0)
                CryptographicOperations.ZeroMemory(register);
            if (stream.Length > 0)
                CryptographicOperations.ZeroMemory(stream);
            messageIndicator = nextMessageIndicator.ToArray();
            register = Array.Empty<byte>();
            stream = Array.Empty<byte>();
            streamBit = 0;
            codewordIndex = 0;
            Prepare();
        }

        public int ProcessCodeword(ReadOnlySpan<byte> input, Span<byte> output)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(NxdnPrivacyProcessor));
            if (input.Length != 9 || output.Length < 9)
                throw new ArgumentException("NXDN privacy requires one 9-byte AMBE codeword.");

            // DMR and NXDN use the same individual 72-bit AMBE codeword layout.
            // Apply privacy only to its 49 natural parameter bits, before rebuilding FEC.
            if (interleaver == null)
                throw new InvalidOperationException("An AMBE codec adapter is required to process NXDN codewords.");
            byte[] bits = new byte[49];
            interleaver.Decode(input.ToArray(), bits);
            Span<byte> parameters = stackalloc byte[7];
            parameters.Clear();
            for (int bit = 0; bit < 49; bit++)
                if (bits[bit] != 0)
                    parameters[bit / 8] |= (byte)(0x80 >> (bit % 8));
            ProcessParameters(parameters);
            for (int bit = 0; bit < 49; bit++)
                bits[bit] = (byte)((parameters[bit / 8] >> (7 - bit % 8)) & 1);
            byte[] result = new byte[9];
            interleaver.Encode(bits, result);
            result.CopyTo(output);
            return 9;
        }

        public void ProcessParameters(Span<byte> parameters)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(NxdnPrivacyProcessor));
            if (parameters.Length != 7)
                throw new ArgumentException("NXDN privacy requires seven bytes of AMBE parameters.", nameof(parameters));
            if (algorithmId == NxdnPrivacyAlgorithms.Ehr && codewordIndex % 16 == 0)
                ehrState = ehrSeed;

            if (algorithmId == NxdnPrivacyAlgorithms.Ehr)
            {
                for (int bit = 0; bit < 49; bit++)
                {
                    bool keyBit = (ehrState & 1) != 0;
                    bool feedback = (((ehrState >> 1) ^ ehrState) & 1) != 0;
                    ehrState = (ushort)(((ehrState >> 1) | (feedback ? 0x4000 : 0)) & 0x7FFF);
                    if (keyBit)
                        parameters[bit / 8] ^= (byte)(0x80 >> (bit % 8));
                }
            }
            else
            {
                for (int bit = 0; bit < 49; bit++)
                {
                    if (streamBit >= stream.Length * 8)
                        FillStreamBlock();
                    if ((stream[streamBit / 8] & (0x80 >> (streamBit % 8))) != 0)
                        parameters[bit / 8] ^= (byte)(0x80 >> (bit % 8));
                    streamBit++;
                }
            }
            parameters[^1] &= 0x80;
            codewordIndex++;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            encryptor?.Dispose();
            cipher?.Dispose();
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(messageIndicator);
            CryptographicOperations.ZeroMemory(register);
            CryptographicOperations.ZeroMemory(stream);
            disposed = true;
        }

        private void Prepare()
        {
            if (algorithmId == NxdnPrivacyAlgorithms.Ehr)
            {
                ehrSeed = (ushort)(((key[0] << 8) | key[1]) & 0x7FFF);
                ehrState = ehrSeed;
                return;
            }

            if (algorithmId == NxdnPrivacyAlgorithms.Des)
            {
                var des = DES.Create();
                des.Mode = CipherMode.ECB;
                des.Padding = PaddingMode.None;
                try { des.Key = key.ToArray(); }
                catch (CryptographicException exception)
                {
                    des.Dispose();
                    throw new ArgumentException("The configured NXDN DES key is weak or invalid for DES.", nameof(key), exception);
                }
                cipher = des;
                register = messageIndicator.ToArray();
            }
            else
            {
                var aes = Aes.Create();
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.KeySize = 256;
                aes.Key = key.ToArray();
                cipher = aes;
                register = NxdnInitializationVectorGenerator.CreateAesInitializationVector(
                    messageIndicator);
            }
            encryptor = cipher.CreateEncryptor();
            // NXDN discards the first DES/AES OFB block.
            AdvanceRegister();
            FillStreamBlock();
        }

        private void FillStreamBlock()
        {
            AdvanceRegister();
            if (stream.Length > 0)
                CryptographicOperations.ZeroMemory(stream);
            stream = register.ToArray();
            streamBit = 0;
        }

        private void AdvanceRegister()
        {
            byte[] next = new byte[register.Length];
            encryptor!.TransformBlock(register, 0, register.Length, next, 0);
            CryptographicOperations.ZeroMemory(register);
            register = next;
        }

    }
}
