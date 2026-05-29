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
    /// Fragment wire layout:
    /// Byte 0               1               2               3
    /// Bit  7 6 5 4 3 2 1 0 7 6 5 4 3 2 1 0 7 6 5 4 3 2 1 0 7 6 5 4 3 2 1 0
    ///     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///     | Uncompressed Length                                           |
    ///     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///     | Compressed Length                                             |
    ///     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///     | Block Number  | Total Blocks  | Payload ..................... |
    ///     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// </summary>
    public sealed class PacketBuffer
    {
        public const int FRAG_HEADER_SIZE = 10;
        public const int FRAG_BLOCK_SIZE = 534;
        public const int FRAG_SIZE = FRAG_HEADER_SIZE + FRAG_BLOCK_SIZE;

        private const int MAX_FRAGMENT_SIZE = 8192 * 1024; // 8 MB

        private readonly Dictionary<byte, Fragment> fragments = new Dictionary<byte, Fragment>();
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
            public byte BlockId { get; set; }

            /// <summary>
            /// Fragment data.
            /// </summary>
            public byte[] Data { get; set; } = Array.Empty<byte>();
        }

        /// <summary>
        /// 
        /// </summary>
        public IReadOnlyDictionary<byte, Fragment> Fragments
        {
            get
            {
                lock (sync)
                {
                    return new Dictionary<byte, Fragment>(fragments);
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
        public bool Decode(ReadOnlySpan<byte> data, out byte[]? message, out uint outLength)
        {
            message = null;
            outLength = 0;

            if (data.Length < FRAG_HEADER_SIZE)
                return false;

            byte curBlock = data[8];
            byte blockCnt = data[9];

            var frag = new Fragment
            {
                BlockId = curBlock
            };

            // if this is the first block, store sizes from the fragment header
            if (curBlock == 0)
            {
                uint size = ReadUInt32BE(data, 0);
                uint compressedSize = ReadUInt32BE(data, 4);

                // prevent potential DOS via oversized fragment metadata
                if (size > MAX_FRAGMENT_SIZE || compressedSize > MAX_FRAGMENT_SIZE)
                    return false;

                frag.Size = size;
                frag.CompressedSize = compressedSize;
            }

            frag.BlockSize = FRAG_BLOCK_SIZE;
            frag.Data = new byte[FRAG_BLOCK_SIZE];

            int payloadBytes = Math.Min(FRAG_BLOCK_SIZE, Math.Max(0, data.Length - FRAG_HEADER_SIZE));
            if (payloadBytes > 0)
                data.Slice(FRAG_HEADER_SIZE, payloadBytes).CopyTo(frag.Data);

            lock (sync)
            {
                fragments[curBlock] = frag;

                // wait until all blocks are present before attempting reassembly
                if (fragments.Count != (blockCnt + 1))
                    return false;

                if (!fragments.TryGetValue(0, out Fragment? first) || first is null)
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

                for (byte i = 0; i <= blockCnt; i++)
                {
                    if (!fragments.TryGetValue(i, out Fragment? block) || block is null)
                    {
                        ClearLocked();
                        return false;
                    }

                    int toCopy = (int)Math.Min(FRAG_BLOCK_SIZE, compressedLen - (uint)written);
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
                    byte[] decompressed = Decompress(compressed);

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
                byte blockCnt = (byte)(compressedLen / FRAG_BLOCK_SIZE + ((compressedLen % FRAG_BLOCK_SIZE) > 0 ? 1U : 0U));

                // Create packet fragments.
                uint offs = 0;
                for (byte i = 0; i < blockCnt; i++)
                {
                    byte[] packet = new byte[FRAG_SIZE];

                    var frag = new Fragment
                    {
                        BlockId = i,
                        Data = packet
                    };

                    if (i == 0)
                    {
                        WriteUInt32BE(length, packet, 0);
                        frag.Size = length;

                        WriteUInt32BE(compressedLen, packet, 4);
                        frag.CompressedSize = compressedLen;
                    }

                    packet[8] = i;
                    packet[9] = (byte)(blockCnt - 1);

                    uint blockSize = FRAG_BLOCK_SIZE;
                    if (offs + FRAG_BLOCK_SIZE > compressedLen)
                        blockSize = FRAG_BLOCK_SIZE - ((offs + FRAG_BLOCK_SIZE) - compressedLen);

                    frag.BlockSize = blockSize;
                    if (blockSize > 0)
                        Buffer.BlockCopy(buffer, (int)offs, packet, FRAG_HEADER_SIZE, (int)blockSize);

                    offs += FRAG_BLOCK_SIZE;
                    fragments[i] = frag;
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
        /// 
        /// </summary>
        private void ClearLocked()
        {
            fragments.Clear();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
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
        /// 
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private static byte[] Decompress(ReadOnlySpan<byte> input)
        {
            using var source = new MemoryStream(input.ToArray());
            using var z = new InflaterInputStream(source, new Inflater(noHeader: false));
            using var output = new MemoryStream();
            z.CopyTo(output);
            return output.ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        private static uint ReadUInt32BE(ReadOnlySpan<byte> buffer, int offset)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(offset, 4));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        private static void WriteUInt32BE(uint value, Span<byte> buffer, int offset)
        {
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(offset, 4), value);
        }
    }
}
