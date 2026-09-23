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

namespace fnecore.NXDN
{
    /// <summary>
    /// Handles NXDN voice encryption using each 49-bit word's original position,
    /// so missing or stolen words do not shift the remaining audio out of sync.
    /// </summary>
    public sealed class NXDNCrypto : IDisposable
    {
        public const byte Clear = 0;
        public const byte Ehr = 1;
        public const byte Des = 2;
        public const byte Aes = 3;

        private byte cipher;
        private ushort keyId;
        private byte[] key;
        private readonly byte[] stream = new byte[196];
        private bool prepared;
        private bool disposed;

        /// <summary>Returns the material size for a wire cipher identifier.</summary>
        public static int KeyBytes(byte algorithm)
        {
            switch (algorithm)
            {
                case Ehr: return 2;
                case Des: return 8;
                case Aes: return 32;
                default: throw new ArgumentOutOfRangeException(nameof(algorithm));
            }
        }

        /// <summary>Maps NXDN wire cipher IDs to the FNE key service.</summary>
        public static byte ToKeyRequestAlgorithm(byte algorithm)
        {
            switch (algorithm)
            {
                case Clear: return 0x80;
                case Ehr: return Ehr;
                case Des: return 0x81;
                case Aes: return 0x84;
                default: throw new ArgumentOutOfRangeException(nameof(algorithm));
            }
        }

        public static byte FromKeyRequestAlgorithm(byte algorithm)
        {
            switch (algorithm)
            {
                case Ehr: return Ehr;
                case 0x81: return Des;
                case 0x84: return Aes;
                default: return Clear;
            }
        }

        /// <summary>Copies a key; the caller retains ownership of its input.</summary>
        public void SetKey(ushort id, byte algorithm, byte[] material)
        {
            ThrowIfDisposed();
            if (id > 63 || material == null || material.Length != KeyBytes(algorithm))
                throw new ArgumentException("Invalid NXDN key ID or key length.");
            if (algorithm == Ehr && ((material[0] & 0x80) != 0 || (material[0] | material[1]) == 0))
                throw new ArgumentException("NXDN EHR requires a nonzero 15-bit key.");
            Reset();
            key = (byte[])material.Clone();
            cipher = algorithm;
            keyId = id;
        }

        public bool HasKey() => !disposed && key != null;

        /// <summary>
        /// Builds the keystream for one encryption session.
        /// No keystream state is advanced by receiving a duplicate packet.
        /// </summary>
        public bool Prepare(byte algorithm, ushort id, byte[] messageIndicator)
        {
            ThrowIfDisposed();
            prepared = false;
            Array.Clear(stream, 0, stream.Length);
            if (key == null || algorithm != cipher || id != keyId)
                return false;
            if (cipher == Ehr)
            {
                int register = (key[0] << 8) | key[1];
                for (int bit = 0; bit < 784; bit++)
                {
                    stream[bit >> 3] |= (byte)((register & 1) << (7 - (bit & 7)));
                    register = (register >> 1) | (((register ^ (register >> 1)) & 1) << 14);
                }
            }
            else
            {
                if (messageIndicator == null || messageIndicator.Length != 8 || ReadIndicator(messageIndicator) == 0)
                    return false;
                using (SymmetricAlgorithm algorithmImpl = cipher == Des ? (SymmetricAlgorithm)DES.Create() : AES())
                {
                    algorithmImpl.Mode = CipherMode.ECB;
                    algorithmImpl.Padding = PaddingMode.None;
                    algorithmImpl.Key = key;
                    using (ICryptoTransform encrypt = algorithmImpl.CreateEncryptor())
                    {
                        byte[] feedback = new byte[cipher == Des ? 8 : 16];
                        try
                        {
                            messageIndicator.CopyTo(feedback, 0);
                            if (cipher == Aes)
                                WriteIndicator(AdvanceIndicator(ReadIndicator(messageIndicator)), feedback.AsSpan(8));
                            // Skip the first OFB block before generating the voice keystream.
                            encrypt.TransformBlock(feedback, 0, feedback.Length, feedback, 0);
                            for (int offset = 0; offset < stream.Length; offset += feedback.Length)
                            {
                                encrypt.TransformBlock(feedback, 0, feedback.Length, feedback, 0);
                                Array.Copy(feedback, 0, stream, offset, Math.Min(feedback.Length, stream.Length - offset));
                            }
                        }
                        finally { CryptographicOperations.ZeroMemory(feedback); }
                    }
                }
            }
            prepared = true;
            return true;
        }

        private static SymmetricAlgorithm AES() => System.Security.Cryptography.Aes.Create();

        /// <summary>XORs 49 unpacked AMBE bits at an explicit session position.</summary>
        public bool Process(byte[] voiceBits, int wordIndex)
        {
            ThrowIfDisposed();
            if (voiceBits == null || voiceBits.Length != 49)
                throw new ArgumentException("NXDN EHR voice requires 49 bits.", nameof(voiceBits));
            if (wordIndex < 0 || wordIndex >= (cipher == Ehr ? 16 : 32))
                throw new ArgumentOutOfRangeException(nameof(wordIndex));
            if (!prepared)
                return false;
            for (int bit = 0; bit < 49; bit++)
            {
                int index = wordIndex * 49 + bit;
                voiceBits[bit] ^= (byte)((stream[index >> 3] >> (7 - (index & 7))) & 1);
            }
            return true;
        }

        /// <summary>Advances the 64-bit IV generator to the next session's value.</summary>
        public static ulong AdvanceIndicator(ulong value)
        {
            for (int i = 0; i < 64; i++)
            {
                ulong feedback = ((value >> 63) ^ (value >> 61) ^ (value >> 45) ^
                    (value >> 37) ^ (value >> 26) ^ (value >> 14)) & 1;
                value = (value << 1) | feedback;
            }
            return value;
        }

        internal static ulong ReadIndicator(ReadOnlySpan<byte> data)
        {
            ulong value = 0;
            for (int i = 0; i < 8; i++)
                value = (value << 8) | data[i];
            return value;
        }

        internal static void WriteIndicator(ulong value, Span<byte> data)
        {
            for (int i = 7; i >= 0; i--)
            {
                data[i] = (byte)value;
                value >>= 8;
            }
        }

        /// <summary>Creates a nonzero, unpredictable 64-bit message indicator.</summary>
        public static byte[] CreateMessageIndicator()
        {
            byte[] value = new byte[8];
            do { RandomNumberGenerator.Fill(value); } while (ReadIndicator(value) == 0);
            return value;
        }

        public void Reset()
        {
            if (key != null)
                CryptographicOperations.ZeroMemory(key);
            key = null;
            CryptographicOperations.ZeroMemory(stream);
            cipher = Clear;
            keyId = 0;
            prepared = false;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(NXDNCrypto));
        }

        public void Dispose()
        {
            Reset();
            disposed = true;
        }
    }
}
