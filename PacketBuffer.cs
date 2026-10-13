// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment Core Library
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 Bryan Biedenkapp, N2PLL
*
*/

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;

namespace fnecore
{
    /// <summary>
    /// Represents a fragmented packet buffer.
    ///
    /// 256-block fragment wire layout:
    /// Byte 0               1               2               3
    /// Bit  7 6 5 4 3 2 1 0 7 6 5 4 3 2 1 0 7 6 5 4 3 2 1 0 7 6 5 4 3 2 1 0
    ///     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///     | Uncompressed Length                                           |
    ///     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///     | Compressed Length                                             |
    ///     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///     | Block Number  | Total Blocks  | Payload ..................... |
    ///     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///
    /// Extended fragment wire layout:
    /// Byte 0               1               2               3
    /// Bit  7 6 5 4 3 2 1 0 7 6 5 4 3 2 1 0 7 6 5 4 3 2 1 0 7 6 5 4 3 2 1 0
    ///     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///     | Uncompressed Length                                           |
    ///     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///     | Compressed Length                                             |
    ///     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///     | Block Number                  | Last Block                    |
    ///     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///     | Payload ..................................................... |
    ///     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// Extended repeats both lengths in every fragment, retains the 544-byte total fragment size,
    /// and supports up to 65,535 blocks (IDs 0 through 65,534).
    /// </summary>
    public sealed class PacketBuffer
    {
        public const int FRAG_HEADER_SIZE = 10;
        public const int FRAG_BLOCK_SIZE = 534;
        public const int FRAG_SIZE = FRAG_HEADER_SIZE + FRAG_BLOCK_SIZE;
        public const int EXTENDED_FRAG_HEADER_SIZE = 12;
        public const int EXTENDED_FRAG_BLOCK_SIZE = FRAG_SIZE - EXTENDED_FRAG_HEADER_SIZE;

        private const int MAX_FRAGMENT_SIZE = 8192 * 1024; // 8 MB
        private const uint MAX_BLOCK_COUNT = 256;
        private const uint MAX_EX_BLOCK_COUNT = 65535;
        private const uint MAX_256_COMPRESSED_SIZE = MAX_BLOCK_COUNT * FRAG_BLOCK_SIZE;

        private readonly Dictionary<ushort, Fragment> fragments = new Dictionary<ushort, Fragment>();
        private readonly object sync = new object();
        private readonly bool compression;

        /// <summary>
        /// Represents a packet buffer fragment.
        /// </summary>
        public sealed class Fragment
        {
            /// <summary>
            /// Compressed size of the packet.
            /// </summary>
            public uint CompressedSize { get; set; }

            /// <summary>
            /// Uncompressed size of the packet.
            /// </summary>
            public uint Size { get; set; }

            /// <summary>
            /// Size of the packet fragment block.
            /// </summary>
            public uint BlockSize { get; set; }

            /// <summary>
            /// Block ID of the fragment.
            /// </summary>
            public ushort BlockId { get; set; }

            /// <summary>
            /// Total number of blocks in the packet.
            /// </summary>
            public ushort BlockCount { get; set; }

            /// <summary>
            /// Flag indicating that this is an extended (V2) fragment.
            /// </summary>
            public bool Extended { get; set; }

            /// <summary>
            /// Fragment data.
            /// </summary>
            public byte[] Data { get; set; } = Array.Empty<byte>();
        }

        /// <summary>
        /// 
        /// </summary>
        public IReadOnlyDictionary<ushort, Fragment> Fragments
        {
            get
            {
                lock (sync)
                {
                    return new Dictionary<ushort, Fragment>(fragments);
                }
            }
        }

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="PacketBuffer"/> class.
        /// </summary>
        /// <param name="compression">Flag indicating whether packet data should be compressed automatically.</param>
        /// <param name="name">Name of buffer.</param>
        public PacketBuffer(bool compression, string name)
        {
            this.compression = compression;
        }

        /// <summary>
        /// Decode a network packet fragment.
        /// </summary>
        /// <param name="data">Buffer containing packet fragment to decode.</param>
        /// <param name="message">Buffer containing assembled message.</param>
        /// <param name="outLength">Length of assembled message.</param>
        /// <returns><c>true</c> if a complete packet has been assembled; otherwise <c>false</c>.</returns>
        public bool Decode(ReadOnlySpan<byte> data, out byte[] message, out uint outLength)
        {
            message = null;
            outLength = 0;

            if (data.Length < FRAG_HEADER_SIZE)
                return false;

            uint size = ReadUInt32BE(data, 0);
            uint compressedSize = ReadUInt32BE(data, 4);
            bool extended = compressedSize > MAX_256_COMPRESSED_SIZE;
            int headerSize = extended ? EXTENDED_FRAG_HEADER_SIZE : FRAG_HEADER_SIZE;
            int blockSize = extended ? EXTENDED_FRAG_BLOCK_SIZE : FRAG_BLOCK_SIZE;

            if (data.Length < headerSize)
                return false;

            ushort curBlock = extended ? ReadUInt16BE(data, 8) : data[8];
            ushort lastBlock = extended ? ReadUInt16BE(data, 10) : data[9];
            uint blockCount = (uint)lastBlock + 1U;
            uint maxBlockCount = extended ? MAX_EX_BLOCK_COUNT : MAX_BLOCK_COUNT;

            if (curBlock >= blockCount || blockCount > maxBlockCount)
            {
                Clear();
                return false;
            }

            if (extended)
            {
                if (size == 0 || size > MAX_FRAGMENT_SIZE || compressedSize > MAX_FRAGMENT_SIZE ||
                    DivideRoundUp(compressedSize, (uint)blockSize) != blockCount)
                {
                    Clear();
                    return false;
                }
            }
            else if (curBlock != 0 && (size != 0 || compressedSize != 0))
            {
                Clear();
                return false;
            }

            var frag = new Fragment
            {
                BlockId = curBlock,
                BlockCount = (ushort)blockCount,
                Extended = extended,
                BlockSize = (uint)blockSize
            };

            // if this is the first block, store sizes from the fragment header
            if (curBlock == 0)
            {
                // prevent potential DOS via oversized fragment metadata
                if (size == 0 || compressedSize == 0 || size > MAX_FRAGMENT_SIZE ||
                    compressedSize > MAX_FRAGMENT_SIZE || (!extended && compressedSize > MAX_256_COMPRESSED_SIZE) ||
                    DivideRoundUp(compressedSize, (uint)blockSize) != blockCount)
                    return false;

                frag.Size = size;
                frag.CompressedSize = compressedSize;
            }
            else if (extended)
            {
                frag.Size = size;
                frag.CompressedSize = compressedSize;
            }

            frag.Data = new byte[blockSize];

            int payloadBytes = Math.Min(blockSize, Math.Max(0, data.Length - headerSize));
            if (payloadBytes > 0)
                data.Slice(headerSize, payloadBytes).CopyTo(frag.Data);

            lock (sync)
            {
                foreach (Fragment existing in fragments.Values)
                {
                    if (existing == null || existing.Extended != extended || existing.BlockCount != blockCount ||
                        (extended && (existing.Size != size || existing.CompressedSize != compressedSize)))
                    {
                        ClearLocked();
                        return false;
                    }

                    break;
                }

                // Match the native implementation: ignore duplicate blocks without replacing data.
                if (fragments.ContainsKey(curBlock))
                    return false;

                fragments.Add(curBlock, frag);

                // wait until all blocks are present before attempting reassembly
                if ((uint)fragments.Count != blockCount)
                    return false;

                if (!fragments.TryGetValue(0, out Fragment first) || first is null)
                {
                    ClearLocked();
                    return false;
                }

                if (first.Size == 0 || first.CompressedSize == 0)
                {
                    ClearLocked();
                    return false;
                }

                uint len = first.Size;
                uint compressedLen = first.CompressedSize;

                if (len > MAX_FRAGMENT_SIZE || compressedLen > MAX_FRAGMENT_SIZE)
                {
                    ClearLocked();
                    return false;
                }

                var compressed = new byte[compressedLen];
                int written = 0;

                for (uint i = 0; i < blockCount; i++)
                {
                    if (!fragments.TryGetValue((ushort)i, out Fragment block) || block is null)
                    {
                        ClearLocked();
                        return false;
                    }

                    int toCopy = (int)Math.Min((uint)blockSize, compressedLen - (uint)written);
                    if (toCopy <= 0)
                        break;

                    Buffer.BlockCopy(block.Data, 0, compressed, written, toCopy);
                    written += toCopy;
                }

                if (written != compressed.Length)
                {
                    ClearLocked();
                    return false;
                }

                if (compression)
                {
                    byte[] decompressed = Decompress(compressed, len);

                    // Match C++ behavior: only succeed when decompressed size equals header size.
                    if (decompressed.Length == len)
                    {
                        message = decompressed;
                        outLength = len;
                        ClearLocked();
                        return true;
                    }

                    ClearLocked();
                    return false;
                }

                if (compressed.Length < len)
                {
                    ClearLocked();
                    return false;
                }

                message = new byte[len];
                Buffer.BlockCopy(compressed, 0, message, 0, (int)len);
                outLength = len;
                ClearLocked();
                return true;
            }
        }

        /// <summary>
        /// Encode a network packet into fragments.
        /// </summary>
        /// <param name="data">Message to encode.</param>
        public void Encode(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
                throw new ArgumentException("Data cannot be empty.", nameof(data));

            lock (sync)
            {
                // Erase any buffered fragments.
                ClearLocked();

                // Create temporary payload buffer (compressed or raw).
                byte[] buffer;
                uint compressedLen;

                if (compression)
                {
                    buffer = Compress(data);
                    compressedLen = (uint)buffer.Length;
                }
                else
                {
                    buffer = data.ToArray();
                    compressedLen = (uint)buffer.Length;
                }

                uint length = (uint)data.Length;
                if (length > MAX_FRAGMENT_SIZE || compressedLen == 0 || compressedLen > MAX_FRAGMENT_SIZE)
                    throw new InvalidDataException("Packet size exceeds the maximum supported size.");

                bool extended = compressedLen > MAX_256_COMPRESSED_SIZE;
                int headerSize = extended ? EXTENDED_FRAG_HEADER_SIZE : FRAG_HEADER_SIZE;
                uint blockSize = (uint)(extended ? EXTENDED_FRAG_BLOCK_SIZE : FRAG_BLOCK_SIZE);
                uint maxBlockCount = extended ? MAX_EX_BLOCK_COUNT : MAX_BLOCK_COUNT;
                uint blockCount = DivideRoundUp(compressedLen, blockSize);
                if (blockCount == 0 || blockCount > maxBlockCount)
                    throw new InvalidDataException("Packet requires too many fragments.");

                // Create packet fragments.
                uint offs = 0;
                for (uint i = 0; i < blockCount; i++)
                {
                    byte[] packet = new byte[FRAG_SIZE];

                    var frag = new Fragment
                    {
                        BlockId = (ushort)i,
                        BlockCount = (ushort)blockCount,
                        Extended = extended,
                        Data = packet
                    };

                    if (i == 0 || extended)
                    {
                        WriteUInt32BE(length, packet, 0);
                        frag.Size = length;

                        WriteUInt32BE(compressedLen, packet, 4);
                        frag.CompressedSize = compressedLen;
                    }

                    if (extended)
                    {
                        WriteUInt16BE((ushort)i, packet, 8);
                        WriteUInt16BE((ushort)(blockCount - 1U), packet, 10);
                    }
                    else
                    {
                        packet[8] = (byte)i;
                        packet[9] = (byte)(blockCount - 1U);
                    }

                    uint copySize = blockSize;
                    if (offs + blockSize > compressedLen)
                        copySize = blockSize - ((offs + blockSize) - compressedLen);

                    frag.BlockSize = copySize;
                    if (copySize > 0)
                        Buffer.BlockCopy(buffer, (int)offs, packet, headerSize, (int)copySize);

                    offs += blockSize;
                    fragments[(ushort)i] = frag;
                }
            }
        }

        /// <summary>
        /// Helper to clear currently buffered fragments.
        /// </summary>
        public void Clear()
        {
            lock (sync)
            {
                ClearLocked();
            }
        }

        /// <summary>
        /// Clears all currently buffered fragments.
        /// </summary>
        private void ClearLocked()
        {
            fragments.Clear();
        }

        /// <summary>
        /// Compresses a byte array using zlib with the best compression setting.
        /// </summary>
        /// <param name="input">The input data to compress.</param>
        /// <returns>The compressed byte array.</returns>
        private static byte[] Compress(ReadOnlySpan<byte> input)
        {
            // SharpZipLib with noHeader=false preserves zlib framing compatible with C++ zlib.
            using var output = new MemoryStream();
            using (var z = new DeflaterOutputStream(output, new Deflater(Deflater.BEST_COMPRESSION, noZlibHeaderOrFooter: false)))
            {
                byte[] src = input.ToArray();
                z.Write(src, 0, src.Length);
                z.Finish();
            }

            return output.ToArray();
        }

        /// <summary>
        /// Decompresses a byte array using zlib and ensures the output matches the expected length.
        /// </summary>
        /// <param name="input">The compressed input data.</param>
        /// <param name="expectedLength">The expected length of the decompressed data.</param>
        /// <returns>The decompressed byte array.</returns>
        private static byte[] Decompress(ReadOnlySpan<byte> input, uint expectedLength)
        {
            using var source = new MemoryStream(input.ToArray());
            using var z = new InflaterInputStream(source, new Inflater(noHeader: false));
            using var output = new MemoryStream();
            byte[] chunk = new byte[8192];
            int count;
            while ((count = z.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (output.Length + count > expectedLength)
                    throw new InvalidDataException("Decompressed packet exceeds its declared size.");
                output.Write(chunk, 0, count);
            }
            return output.ToArray();
        }

        /// <summary>
        /// Reads a 32-bit unsigned integer from a buffer in big-endian format.
        /// </summary>
        /// <param name="buffer">The buffer containing the 32-bit unsigned integer in big-endian format.</param>
        /// <param name="offset">The offset within the buffer where the 32-bit unsigned integer starts.</param>
        /// <returns>The 32-bit unsigned integer read from the buffer.</returns>
        private static uint ReadUInt32BE(ReadOnlySpan<byte> buffer, int offset)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(offset, 4));
        }

        /// <summary>
        /// Reads a 16-bit unsigned integer from a buffer in big-endian format.
        /// </summary>
        /// <param name="buffer">The buffer containing the 16-bit unsigned integer in big-endian format.</param>
        /// <param name="offset">The offset within the buffer where the 16-bit unsigned integer starts.</param>
        /// <returns>The 16-bit unsigned integer read from the buffer.</returns>
        private static ushort ReadUInt16BE(ReadOnlySpan<byte> buffer, int offset)
        {
            return BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset, 2));
        }

        /// <summary>
        /// Writes a 32-bit unsigned integer to a buffer in big-endian format.
        /// </summary>
        /// <param name="value">The 32-bit unsigned integer to write.</param>
        /// <param name="buffer">The buffer to write the 32-bit unsigned integer to in big-endian format.</param>
        /// <param name="offset">The offset within the buffer where the 32-bit unsigned integer should be written.</param>
        private static void WriteUInt32BE(uint value, Span<byte> buffer, int offset)
        {
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(offset, 4), value);
        }

        /// <summary>
        /// Writes a 16-bit unsigned integer to a buffer in big-endian format.
        /// </summary>
        /// <param name="value">The 16-bit unsigned integer to write.</param>
        /// <param name="buffer">The buffer to write the 16-bit unsigned integer to in big-endian format.</param>
        /// <param name="offset">The offset within the buffer where the 16-bit unsigned integer should be written.</param>
        private static void WriteUInt16BE(ushort value, Span<byte> buffer, int offset)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(offset, 2), value);
        }

        /// <summary>
        /// Divides a value by a divisor and rounds up to the nearest whole number.
        /// </summary>
        /// <param name="value">The value to be divided.</param>
        /// <param name="divisor">The divisor.</param>
        /// <returns>The result of the division rounded up.</returns>
        private static uint DivideRoundUp(uint value, uint divisor)
        {
            return value / divisor + (value % divisor == 0 ? 0U : 1U);
        }
    }
}
