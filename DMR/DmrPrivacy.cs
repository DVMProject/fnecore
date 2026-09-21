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
using System.Linq;
using System.Security.Cryptography;
using fnecore.P25;

namespace fnecore.DMR
{
    public static class DmrPrivacyAlgorithms
    {
        public const byte Arc4 = 0x01;
        public const byte DesOfb = 0x02;
        public const byte Aes256 = 0x05;
        public const byte FeatureId = 0x10;
        public const int MessageIndicatorBytes = 4;

        /// <summary>Maps a DMR wire algorithm to its FNE KMM key-service algorithm.</summary>
        public static byte ToKeyRequestAlgorithm(byte algorithmId)
        {
            switch (algorithmId)
            {
                case Arc4: return P25Defines.P25_ALGO_ARC4;
                case DesOfb: return P25Defines.P25_ALGO_DES;
                case Aes256: return P25Defines.P25_ALGO_AES;
                default: return 0;
            }
        }

        /// <summary>Returns zero for a key-service algorithm not used by DMR.</summary>
        public static byte FromKeyRequestAlgorithm(byte algorithmId)
        {
            switch (algorithmId)
            {
                case P25Defines.P25_ALGO_ARC4: return Arc4;
                case P25Defines.P25_ALGO_DES: return DesOfb;
                case P25Defines.P25_ALGO_AES: return Aes256;
                default: return 0;
            }
        }

        public static int KeyBytes(byte algorithmId)
        {
            switch (algorithmId)
            {
                case Arc4: return 5;
                case DesOfb: return 8;
                case Aes256: return 32;
                default: throw new ArgumentOutOfRangeException(nameof(algorithmId), "Unsupported DMR privacy algorithm.");
            }
        }
    }

    public sealed class DmrPrivacyOptions
    {
        public DmrPrivacyOptions(byte algorithmId, byte keyId, ReadOnlyMemory<byte> key,
            ReadOnlyMemory<byte> messageIndicator)
        {
            int expectedKeyBytes = DmrPrivacyAlgorithms.KeyBytes(algorithmId);
            if (keyId == 0)
                throw new ArgumentOutOfRangeException(nameof(keyId));
            if (key.Length != expectedKeyBytes)
                throw new ArgumentException($"DMR algorithm 0x{algorithmId:X2} requires exactly {expectedKeyBytes} key bytes.", nameof(key));
            if (messageIndicator.Length != DmrPrivacyAlgorithms.MessageIndicatorBytes)
                throw new ArgumentException("DMR privacy requires a 4-byte message indicator.", nameof(messageIndicator));

            AlgorithmId = algorithmId;
            KeyId = keyId;
            Key = key.ToArray();
            MessageIndicator = messageIndicator.ToArray();
        }

        public byte AlgorithmId { get; }
        public byte KeyId { get; }
        public ReadOnlyMemory<byte> Key { get; }
        public ReadOnlyMemory<byte> MessageIndicator { get; }

        public static DmrPrivacyOptions CreateRandom(byte algorithmId, byte keyId, ReadOnlyMemory<byte> key)
        {
            byte[] messageIndicator = new byte[DmrPrivacyAlgorithms.MessageIndicatorBytes];
            RandomNumberGenerator.Fill(messageIndicator);
            return new DmrPrivacyOptions(algorithmId, keyId, key, messageIndicator);
        }
    }

    /// <summary>Symmetric DMR Association privacy transform over the 49 significant AMBE bits.</summary>
    public sealed class DmrPrivacyProcessor : IDisposable
    {
        private const int CodewordsPerPrivacyCycle = 18;
        private const int ParameterBytes = 7;
        private const int Arc4DiscardBytes = 256;
        private readonly IDmrAmbeCodec codec;
        private readonly byte algorithmId;
        private readonly byte[] key;
        private readonly byte[] messageIndicator;
        private byte[] keystream = Array.Empty<byte>();
        private int codewordIndex;
        private bool disposed;

        public DmrPrivacyProcessor(DmrPrivacyOptions options, IDmrAmbeCodec codec)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            algorithmId = options.AlgorithmId;
            key = options.Key.ToArray();
            messageIndicator = options.MessageIndicator.ToArray();
            PrepareCycle();
        }

        public ReadOnlyMemory<byte> MessageIndicator => messageIndicator;

        public byte[] GetNextMessageIndicator()
        {
            ThrowIfDisposed();
            return CalculateNextMessageIndicator();
        }

        public int ProcessCodeword(ReadOnlySpan<byte> input, Span<byte> output)
        {
            ThrowIfDisposed();
            if (input.Length != DmrVoicePacketCodec.CodewordBytes || output.Length < DmrVoicePacketCodec.CodewordBytes)
                throw new ArgumentException("DMR privacy requires one 9-byte AMBE codeword.");

            byte[] bits = new byte[49];
            codec.Decode(input.ToArray(), bits);
            Span<byte> parameters = stackalloc byte[ParameterBytes];
            parameters.Clear();
            for (int bit = 0; bit < 49; bit++)
                if (bits[bit] != 0)
                    parameters[bit / 8] |= (byte)(0x80 >> (bit % 8));
            ProcessParameters(parameters);
            for (int bit = 0; bit < 49; bit++)
                bits[bit] = (byte)((parameters[bit / 8] >> (7 - bit % 8)) & 1);
            byte[] result = new byte[DmrVoicePacketCodec.CodewordBytes];
            codec.Encode(bits, result);
            result.CopyTo(output);
            return result.Length;
        }

        public void SkipCodewords(int count)
        {
            ThrowIfDisposed();
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            Span<byte> discarded = stackalloc byte[ParameterBytes];
            for (int index = 0; index < count; index++)
            {
                discarded.Clear();
                ProcessParameters(discarded);
            }
        }

        public void ProcessParameters(Span<byte> parameters)
        {
            ThrowIfDisposed();
            if (parameters.Length != ParameterBytes)
                throw new ArgumentException("DMR privacy requires seven bytes of AMBE parameters.", nameof(parameters));

            int offset = codewordIndex * ParameterBytes;
            for (int index = 0; index < ParameterBytes; index++)
                parameters[index] ^= keystream[offset + index];
            parameters[parameters.Length - 1] &= 0x80;

            codewordIndex++;
            if (codewordIndex == CodewordsPerPrivacyCycle)
            {
                AdvanceMessageIndicator();
                PrepareCycle();
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(keystream);
            CryptographicOperations.ZeroMemory(messageIndicator);
            disposed = true;
        }

        private void PrepareCycle()
        {
            codewordIndex = 0;
            if (keystream.Length > 0)
                CryptographicOperations.ZeroMemory(keystream);
            switch (algorithmId)
            {
                case DmrPrivacyAlgorithms.Arc4:
                    keystream = CreateArc4Keystream(key, messageIndicator);
                    break;
                case DmrPrivacyAlgorithms.DesOfb:
                    keystream = CreateDesKeystream(key, ExpandDesIv(messageIndicator));
                    break;
                case DmrPrivacyAlgorithms.Aes256:
                    keystream = CreateAesKeystream(key, ExpandAesIv(messageIndicator));
                    break;
                default:
                    throw new InvalidOperationException("Unsupported DMR privacy algorithm.");
            }
        }

        private void AdvanceMessageIndicator()
        {
            byte[] next = CalculateNextMessageIndicator();
            next.CopyTo(messageIndicator, 0);
        }

        private byte[] CalculateNextMessageIndicator()
        {
            switch (algorithmId)
            {
                case DmrPrivacyAlgorithms.Arc4: return CycleArc4Mi(messageIndicator);
                case DmrPrivacyAlgorithms.DesOfb: return ExpandDesIv(messageIndicator).Skip(4).Take(4).ToArray();
                case DmrPrivacyAlgorithms.Aes256: return ExpandAesIv(messageIndicator).Skip(4).Take(4).ToArray();
                default: throw new InvalidOperationException("Unsupported DMR privacy algorithm.");
            }
        }

        private static byte[] CreateArc4Keystream(ReadOnlySpan<byte> key, ReadOnlySpan<byte> mi)
        {
            byte[] combined = new byte[key.Length + mi.Length];
            key.CopyTo(combined);
            mi.CopyTo(combined.AsSpan(key.Length));
            byte[] state = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
            int j = 0;
            for (int i = 0; i < state.Length; i++)
            {
                j = (j + state[i] + combined[i % combined.Length]) & 0xFF;
                byte swap = state[i]; state[i] = state[j]; state[j] = swap;
            }

            byte[] output = new byte[CodewordsPerPrivacyCycle * ParameterBytes];
            int x = 0;
            j = 0;
            for (int index = 0; index < Arc4DiscardBytes + output.Length; index++)
            {
                x = (x + 1) & 0xFF;
                j = (j + state[x]) & 0xFF;
                byte swap = state[x]; state[x] = state[j]; state[j] = swap;
                if (index >= Arc4DiscardBytes)
                    output[index - Arc4DiscardBytes] = state[(state[x] + state[j]) & 0xFF];
            }
            CryptographicOperations.ZeroMemory(combined);
            CryptographicOperations.ZeroMemory(state);
            return output;
        }

        private static byte[] CreateDesKeystream(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv)
        {
            using (DES des = DES.Create())
            {
                des.Mode = CipherMode.ECB;
                des.Padding = PaddingMode.None;
                try { des.Key = key.ToArray(); }
                catch (CryptographicException exception)
                {
                    throw new ArgumentException("The configured DMR DES key is weak or invalid for DES.", nameof(key), exception);
                }
                return CreateOfbKeystream(des, iv, 8);
            }
        }

        private static byte[] CreateAesKeystream(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.KeySize = 256;
                aes.Key = key.ToArray();
                return CreateOfbKeystream(aes, iv, 16);
            }
        }

        private static byte[] CreateOfbKeystream(SymmetricAlgorithm algorithm, ReadOnlySpan<byte> iv, int discardBytes)
        {
            int blockBytes = algorithm.BlockSize / 8;
            int requiredBytes = CodewordsPerPrivacyCycle * ParameterBytes;
            int totalBytes = discardBytes + requiredBytes;
            int blocks = (totalBytes + blockBytes - 1) / blockBytes;
            byte[] register = iv.ToArray();
            byte[] generated = new byte[blocks * blockBytes];
            using (ICryptoTransform encryptor = algorithm.CreateEncryptor())
            {
                for (int block = 0; block < blocks; block++)
                {
                    encryptor.TransformBlock(register, 0, blockBytes, generated, block * blockBytes);
                    generated.AsSpan(block * blockBytes, blockBytes).CopyTo(register);
                }
            }
            byte[] output = generated.AsSpan(discardBytes, requiredBytes).ToArray();
            CryptographicOperations.ZeroMemory(generated);
            CryptographicOperations.ZeroMemory(register);
            return output;
        }

        private static byte[] ExpandDesIv(ReadOnlySpan<byte> mi)
        {
            ulong lfsr = ReadUInt32BigEndian(mi);
            for (int count = 0; count < 32; count++)
            {
                ulong bit = ((lfsr >> 31) ^ (lfsr >> 21) ^ (lfsr >> 1) ^ lfsr) & 1;
                lfsr = (lfsr << 1) | bit;
            }
            byte[] iv = new byte[8];
            for (int index = 0; index < iv.Length; index++)
                iv[index] = (byte)(lfsr >> (56 - index * 8));
            return iv;
        }

        private static byte[] ExpandAesIv(ReadOnlySpan<byte> mi)
        {
            byte[] iv = new byte[16];
            mi.CopyTo(iv);
            ulong lfsr = ReadUInt32BigEndian(mi);
            for (int bitIndex = 32; bitIndex < 128; bitIndex++)
            {
                ulong bit = ((lfsr >> 31) ^ (lfsr >> 21) ^ (lfsr >> 1) ^ lfsr) & 1;
                lfsr = (lfsr << 1) | bit;
                iv[bitIndex / 8] = (byte)((iv[bitIndex / 8] << 1) | (int)bit);
            }
            return iv;
        }

        private static byte[] CycleArc4Mi(ReadOnlySpan<byte> mi)
        {
            ulong lfsr = ReadUInt32BigEndian(mi);
            for (int count = 0; count < 32; count++)
            {
                ulong bit = ((lfsr >> 31) ^ (lfsr >> 3) ^ (lfsr >> 1)) & 1;
                lfsr = ((lfsr << 1) | bit) & 0xFFFFFFFF;
            }
            return new[] { (byte)(lfsr >> 24), (byte)(lfsr >> 16), (byte)(lfsr >> 8), (byte)lfsr };
        }

        private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> value)
        {
            return (uint)(value[0] << 24 | value[1] << 16 | value[2] << 8 | value[3]);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(DmrPrivacyProcessor));
        }
    }
}
