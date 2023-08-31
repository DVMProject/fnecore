/**
* Digital Voice Modem - Fixed Network Equipment
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment
*
*/
/*
*   Copyright (C) 2022 by Bryan Biedenkapp N2PLL
*
*   This program is free software: you can redistribute it and/or modify
*   it under the terms of the GNU Affero General Public License as published by
*   the Free Software Foundation, either version 3 of the License, or
*   (at your option) any later version.
*
*   This program is distributed in the hope that it will be useful,
*   but WITHOUT ANY WARRANTY; without even the implied warranty of
*   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
*   GNU Affero General Public License for more details.
*/

using System;
using System.Security.Cryptography;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;

namespace fnecore
{
    /// <summary>
    /// 
    /// </summary>
    public class FneUtils
    {
        private static readonly byte[] BIT_MASK_TABLE = new byte[8] { 0x80, 0x40, 0x20, 0x10, 0x08, 0x04, 0x02, 0x01 };
        private static Action<IntPtr, byte, int> memsetDelegate;

        /*
        ** Methods
        */

        /// <summary>
        /// Static initializer for the <see cref="FneUtils"/> class.
        /// </summary>
        static FneUtils()
        {
            DynamicMethod dynamicMethod = new DynamicMethod("Memset", MethodAttributes.Public | MethodAttributes.Static, CallingConventions.Standard,
                null, new[] { typeof(IntPtr), typeof(byte), typeof(int) }, typeof(FneUtils), true);

            ILGenerator generator = dynamicMethod.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Ldarg_2);
            generator.Emit(OpCodes.Initblk);
            generator.Emit(OpCodes.Ret);

            memsetDelegate = (Action<IntPtr, byte, int>)dynamicMethod.CreateDelegate(typeof(Action<IntPtr, byte, int>));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="array"></param>
        /// <param name="what"></param>
        /// <param name="length"></param>
        public static void Memset(byte[] array, byte what, int length)
        {
            GCHandle gcHandle = GCHandle.Alloc(array, GCHandleType.Pinned);
            memsetDelegate(gcHandle.AddrOfPinnedObject(), what, length);
            gcHandle.Free();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="b"></param>
        /// <param name="bits"></param>
        /// <param name="offset"></param>
        public static void ByteToBitsBE(byte b, ref bool[] bits, int offset)
        {
            bits[0 + offset] = (b & 0x80U) == 0x80U;
            bits[1 + offset] = (b & 0x40U) == 0x40U;
            bits[2 + offset] = (b & 0x20U) == 0x20U;
            bits[3 + offset] = (b & 0x10U) == 0x10U;
            bits[4 + offset] = (b & 0x08U) == 0x08U;
            bits[5 + offset] = (b & 0x04U) == 0x04U;
            bits[6 + offset] = (b & 0x02U) == 0x02U;
            bits[7 + offset] = (b & 0x01U) == 0x01U;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="b"></param>
        /// <param name="bits"></param>
        /// <param name="offset"></param>
        public static void ByteToBitsLE(byte b, ref bool[] bits, int offset)
        {
            bits[0 + offset] = (b & 0x01U) == 0x01U;
            bits[1 + offset] = (b & 0x02U) == 0x02U;
            bits[2 + offset] = (b & 0x04U) == 0x04U;
            bits[3 + offset] = (b & 0x08U) == 0x08U;
            bits[4 + offset] = (b & 0x10U) == 0x10U;
            bits[5 + offset] = (b & 0x20U) == 0x20U;
            bits[6 + offset] = (b & 0x40U) == 0x40U;
            bits[7 + offset] = (b & 0x80U) == 0x80U;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bits"></param>
        /// <param name="offset"></param>
        /// <param name="b"></param>
        public static void BitsToByteBE(bool[] bits, int offset, ref byte b)
        {
            b = (byte)(bits[0 + offset] ? 0x80U : 0x00U);
            b |= (byte)(bits[1 + offset] ? 0x40U : 0x00U);
            b |= (byte)(bits[2 + offset] ? 0x20U : 0x00U);
            b |= (byte)(bits[3 + offset] ? 0x10U : 0x00U);
            b |= (byte)(bits[4 + offset] ? 0x08U : 0x00U);
            b |= (byte)(bits[5 + offset] ? 0x04U : 0x00U);
            b |= (byte)(bits[6 + offset] ? 0x02U : 0x00U);
            b |= (byte)(bits[7 + offset] ? 0x01U : 0x00U);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bits"></param>
        /// <param name="offset"></param>
        /// <param name="b"></param>
        public static void BitsToByteLE(bool[] bits, int offset, ref byte b)
        {
            b = (byte)(bits[0 + offset] ? 0x01U : 0x00U);
            b |= (byte)(bits[1 + offset] ? 0x02U : 0x00U);
            b |= (byte)(bits[2 + offset] ? 0x04U : 0x00U);
            b |= (byte)(bits[3 + offset] ? 0x08U : 0x00U);
            b |= (byte)(bits[4 + offset] ? 0x10U : 0x00U);
            b |= (byte)(bits[5 + offset] ? 0x20U : 0x00U);
            b |= (byte)(bits[6 + offset] ? 0x40U : 0x00U);
            b |= (byte)(bits[7 + offset] ? 0x80U : 0x00U);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="p"></param>
        /// <param name="i"></param>
        /// <param name="b"></param>
        public static void WriteBit(ref byte[] p, uint i, bool b)
        {
            p[(i) >> 3] = (byte)((b) ? (p[(i) >> 3] | BIT_MASK_TABLE[(i) & 7]) : (p[(i) >> 3] & ~BIT_MASK_TABLE[(i) & 7]));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="p"></param>
        /// <param name="i"></param>
        /// <param name="b"></param>
        public static void WriteBit(ref Span<byte> p, uint i, bool b)
        {
            p[(int)((i) >> 3)] = (byte)((b) ? (p[(int)((i) >> 3)] | BIT_MASK_TABLE[(i) & 7]) : (p[(int)((i) >> 3)] & ~BIT_MASK_TABLE[(i) & 7]));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="p"></param>
        /// <param name="i"></param>
        /// <returns></returns>
        public static bool ReadBit(byte[] p, uint i)
        {
            return (p[(i) >> 3] & BIT_MASK_TABLE[(i) & 7]) != 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="p"></param>
        /// <param name="i"></param>
        /// <returns></returns>
        public static bool ReadBit(Span<byte> p, uint i)
        {
            return (p[(int)((i) >> 3)] & BIT_MASK_TABLE[(i) & 7]) != 0;
        }

        /// <summary>
        /// Write the given bytes in the unsigned short into the given buffer (by most significant byte)
        /// </summary>
        /// <param name="val"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public static void WriteBytes(ushort val, ref byte[] buffer, int offset)
        {
            buffer[offset] = (byte)(val >> 8);
            buffer[offset + 1] = (byte)(val & 0xFF);
        }

        /// <summary>
        /// Write the given bytes in the unsigned short into the given buffer (by most significant byte)
        /// </summary>
        /// <param name="val"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public static void WriteBytes(ushort val, ref Span<byte> buffer, int offset)
        {
            buffer[offset] = (byte)(val >> 8);
            buffer[offset + 1] = (byte)(val & 0xFF);
        }

        /// <summary>
        /// Write the given bytes in the unsigned integer into the given buffer (by most significant byte)
        /// </summary>
        /// <param name="val"></param>
        public static void Write3Bytes(uint val, ref byte[] buffer, int offset)
        {
            buffer[offset + 0] = (byte)((val >> 16) & 0xFF);
            buffer[offset + 1] = (byte)((val >> 8) & 0xFF);
            buffer[offset + 2] = (byte)(val & 0xFF);
        }

        /// <summary>
        /// Write the given bytes in the unsigned integer into the given buffer (by most significant byte)
        /// </summary>
        /// <param name="val"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public static void WriteBytes(uint val, ref byte[] buffer, int offset)
        {
            buffer[offset] = (byte)((val >> 24) & 0xFF);
            buffer[offset + 1] = (byte)((val >> 16) & 0xFF);
            buffer[offset + 2] = (byte)((val >> 8) & 0xFF);
            buffer[offset + 3] = (byte)(val & 0xFF);
        }

        /// <summary>
        /// Write the given bytes in the unsigned integer into the given buffer (by most significant byte)
        /// </summary>
        /// <param name="val"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public static void WriteBytes(uint val, ref Span<byte> buffer, int offset)
        {
            buffer[offset] = (byte)((val >> 24) & 0xFF);
            buffer[offset + 1] = (byte)((val >> 16) & 0xFF);
            buffer[offset + 2] = (byte)((val >> 8) & 0xFF);
            buffer[offset + 3] = (byte)(val & 0xFF);
        }

        /// <summary>
        /// Write the given bytes in the unsigned long into the given buffer (by most significant byte)
        /// </summary>
        /// <param name="val"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public static void WriteBytes(ulong val, ref byte[] buffer, int offset)
        {
            buffer[offset] = (byte)((val >> 56) & 0xFF);
            buffer[offset + 1] = (byte)((val >> 48) & 0xFF);
            buffer[offset + 2] = (byte)((val >> 40) & 0xFF);
            buffer[offset + 3] = (byte)((val >> 32) & 0xFF);
            buffer[offset + 4] = (byte)((val >> 24) & 0xFF);
            buffer[offset + 5] = (byte)((val >> 16) & 0xFF);
            buffer[offset + 6] = (byte)((val >> 8) & 0xFF);
            buffer[offset + 7] = (byte)(val & 0xFF);
        }

        /// <summary>
        /// Write the given bytes in the unsigned long into the given buffer (by most significant byte)
        /// </summary>
        /// <param name="val"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public static void WriteBytes(ulong val, ref Span<byte> buffer, int offset)
        {
            buffer[offset] = (byte)((val >> 56) & 0xFF);
            buffer[offset + 1] = (byte)((val >> 48) & 0xFF);
            buffer[offset + 2] = (byte)((val >> 40) & 0xFF);
            buffer[offset + 3] = (byte)((val >> 32) & 0xFF);
            buffer[offset + 4] = (byte)((val >> 24) & 0xFF);
            buffer[offset + 5] = (byte)((val >> 16) & 0xFF);
            buffer[offset + 6] = (byte)((val >> 8) & 0xFF);
            buffer[offset + 7] = (byte)(val & 0xFF);
        }

        /// <summary>
        /// Write the given bytes in the short into the given buffer (by most significant byte)
        /// </summary>
        /// <param name="val"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public static void WriteBytes(short val, ref byte[] buffer, int offset)
        {
            WriteBytes((ushort)val, ref buffer, offset);
        }

        /// <summary>
        /// Write the given bytes in the short into the given buffer (by most significant byte)
        /// </summary>
        /// <param name="val"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public static void WriteBytes(short val, ref Span<byte> buffer, int offset)
        {
            WriteBytes((ushort)val, ref buffer, offset);
        }

        /// <summary>
        /// Write the given bytes in the integer into the given buffer (by most significant byte)
        /// </summary>
        /// <param name="val"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public static void WriteBytes(int val, ref byte[] buffer, int offset)
        {
            WriteBytes((uint)val, ref buffer, offset);
        }

        /// <summary>
        /// Write the given bytes in the integer into the given buffer (by most significant byte)
        /// </summary>
        /// <param name="val"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public static void WriteBytes(int val, ref Span<byte> buffer, int offset)
        {
            WriteBytes((uint)val, ref buffer, offset);
        }

        /// <summary>
        /// Write the given bytes in the long into the given buffer (by most significant byte)
        /// </summary>
        /// <param name="val"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public static void WriteBytes(long val, ref byte[] buffer, int offset)
        {
            WriteBytes((ulong)val, ref buffer, offset);
        }

        /// <summary>
        /// Write the given bytes in the long into the given buffer (by most significant byte)
        /// </summary>
        /// <param name="val"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public static void WriteBytes(long val, ref Span<byte> buffer, int offset)
        {
            WriteBytes((ulong)val, ref buffer, offset);
        }

        /// <summary>
        /// Get an unsigned short value from the given bytes. (by most significant byte)
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static ushort ToUInt16(byte[] buffer, int offset)
        {
            return (ushort)(((buffer[offset] << 8) & 0xFF00) | ((buffer[offset + 1] << 0) & 0x00FF));
        }

        /// <summary>
        /// Get an unsigned short value from the given bytes. (by most significant byte)
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static ushort ToUInt16(Span<byte> buffer, int offset)
        {
            return (ushort)(((buffer[offset] << 8) & 0xFF00) | ((buffer[offset + 1] << 0) & 0x00FF));
        }

        /// <summary>
        /// Get an unsigned integer value from the given bytes. (by most significant byte)
        /// </summary>
        /// <returns></returns>
        public static uint Bytes3ToUInt32(byte[] buffer, int offset)
        {
            uint val = (uint)(((buffer[offset] << 16) & 0x00FF0000U) | ((buffer[offset + 1] << 8) & 0x0000FF00U) |
                ((buffer[offset + 2] << 0) & 0x000000FFU));
            return val;
        }

        /// <summary>
        /// Get an unsigned integer value from the given bytes. (by most significant byte)
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static uint ToUInt32(byte[] buffer, int offset)
        {
            uint val = (uint)(((buffer[offset + 0] << 24) & 0xFF000000U) | ((buffer[offset + 1] << 16) & 0x00FF0000U)
                | ((buffer[offset + 2] << 8) & 0x0000FF00U) | ((buffer[offset + 3] << 0) & 0x000000FFU));
            return val;
        }

        /// <summary>
        /// Get an unsigned integer value from the given bytes. (by most significant byte)
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static uint ToUInt32(Span<byte> buffer, int offset)
        {
            uint val = (uint)(((buffer[offset + 0] << 24) & 0xFF000000U) | ((buffer[offset + 1] << 16) & 0x00FF0000U)
                | ((buffer[offset + 2] << 8) & 0x0000FF00U) | ((buffer[offset + 3] << 0) & 0x000000FFU));
            return val;
        }

        /// <summary>
        /// Get an unsigned long value from the given bytes. (by most significant byte)
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static ulong ToUInt64(byte[] buffer, int offset)
        {
            return (((ulong)ToUInt32(buffer, offset + 0)) << 32) | ToUInt32(buffer, offset + 4);
        }

        /// <summary>
        /// Get an unsigned long value from the given bytes. (by most significant byte)
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static ulong ToUInt64(Span<byte> buffer, int offset)
        {
            return (((ulong)ToUInt32(buffer, offset + 0)) << 32) | ToUInt32(buffer, offset + 4);
        }

        /// <summary>
        /// Get an signed short value from the given bytes. (by most significant byte)
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static short ToInt16(byte[] buffer, int offset)
        {
            return (short)ToUInt16(buffer, offset);
        }

        /// <summary>
        /// Get an signed short value from the given bytes. (by most significant byte)
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static short ToInt16(Span<byte> buffer, int offset)
        {
            return (short)ToUInt16(buffer, offset);
        }

        /// <summary>
        /// Get a signed integer value from the given bytes. (by most significant byte)
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static int ToInt32(byte[] buffer, int offset)
        {
            return (int)ToUInt32(buffer, offset);
        }

        /// <summary>
        /// Get a signed integer value from the given bytes. (by most significant byte)
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static int ToInt32(Span<byte> buffer, int offset)
        {
            return (int)ToUInt32(buffer, offset);
        }

        /// <summary>
        /// Get a signed long value from the given bytes. (by most significant byte)
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static long ToInt64(byte[] buffer, int offset)
        {
            return (long)ToUInt64(buffer, offset);
        }

        /// <summary>
        /// Get a signed long value from the given bytes. (by most significant byte)
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static long ToInt64(Span<byte> buffer, int offset)
        {
            return (long)ToUInt64(buffer, offset);
        }

        /// <summary>
        /// Primitive conversion from Unicode to ASCII that preserves special characters.
        /// </summary>
        /// <param name="value">The string to convert.</param>
        /// <param name="dest">The buffer to fill.</param>
        /// <param name="offset">The start of the string in the buffer.</param>
        /// <param name="count">The number of characters to convert.</param>
        /// <remarks>The built-in ASCIIEncoding converts characters of codepoint > 127 to ?,
        /// this preserves those code points by removing the top 16 bits of each character.</remarks>
        public static void StringToBytes(string value, byte[] dest, int offset, int count)
        {
            char[] chars = value.ToCharArray();

            int i = 0;
            while (i < chars.Length)
            {
                dest[i + offset] = (byte)chars[i];
                ++i;
            }

            while (i < count)
            {
                dest[i + offset] = 0;
                ++i;
            }
        }

        /// <summary>
        /// Primitive conversion from ASCII to Unicode that preserves special characters.
        /// </summary>
        /// <param name="data">The data to convert.</param>
        /// <param name="offset">The first byte to convert.</param>
        /// <param name="count">The number of bytes to convert.</param>
        /// <returns>The string.</returns>
        /// <remarks>The built-in ASCIIEncoding converts characters of codepoint > 127 to ?,
        /// this preserves those code points.</remarks>
        public static string BytesToString(byte[] data, int offset, int count)
        {
            char[] result = new char[count];

            // iterate through the individual bytes, and convert them to a character
            for (int i = 0; i < count; ++i)
                result[i] = (char)data[i + offset];

            return new string(result);
        }

        /// <summary>
        /// Helper to display the ASCII representation of a hex dump.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        private static string DisplayHexChars(Span<byte> buffer, int offset)
        {
            int bCount = 0;

            string _out = string.Empty;
            for (int i = offset; i < buffer.Length; i++)
            {
                // stop every 16 bytes...
                if (bCount == 16)
                    break;

                byte b = buffer[i];
                char c = Convert.ToChar(b);

                // make control and illegal characters spaces
                if (c >= 0x00 && c <= 0x1F)
                    c = ' ';
                if (c >= 0x7F)
                    c = ' ';

                _out += c;

                bCount++;
            }

            return _out;
        }

        /// <summary>
        /// Helper to display the ASCII representation of a hex dump.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        private static string DisplayHexChars(byte[] buffer, int offset)
        {
            int bCount = 0;

            string _out = string.Empty;
            for (int i = offset; i < buffer.Length; i++)
            {
                // stop every 16 bytes...
                if (bCount == 16)
                    break;

                byte b = buffer[i];
                char c = Convert.ToChar(b);

                // make control and illegal characters spaces
                if (c >= 0x00 && c <= 0x1F)
                    c = ' ';
                if (c >= 0x7F)
                    c = ' ';

                _out += c;

                bCount++;
            }

            return _out;
        }

        /// <summary>
        /// Perform a hex dump of a buffer.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public static string HexDump(byte[] buffer, int offset = 0)
        {
            int bCount = 0, j = 0;

            // iterate through buffer printing all the stored bytes
            string res = "\n\tDUMP " + j.ToString("X4") + ":  ";
            for (int i = offset; i < buffer.Length; i++)
            {
                byte b = buffer[i];

                // split the message every 16 bytes...
                if (bCount == 16)
                {
                    res += "    *" + DisplayHexChars(buffer, j) + "*\n";
                    bCount = 0;
                    j += 16;
                    res += "\tDUMP " + j.ToString("X4") + ":  ";
                }
                else
                    res += (bCount > 0) ? " " : "";

                res += b.ToString("X2");
                bCount++;
            }

            // if the byte count at this point is non-zero print the message
            if (bCount != 0)
            {
                if (bCount < 16)
                {
                    for (int i = bCount; i < 16; i++)
                        res += "   ";
                }

                res += "    *" + DisplayHexChars(buffer, j) + "*";
            }

            return res;
        }

        /// <summary>
        /// Perform a hex dump of a buffer.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public static string HexDump(short[] buffer, int offset = 0)
        {
            int bCount = 0, j = 0;

            // iterate through buffer printing all the stored bytes
            string res = "\n\tDUMP " + j.ToString("X4") + ":  ";
            for (int i = offset; i < buffer.Length; i++)
            {
                short b = buffer[i];

                // split the message every 16 bytes...
                if (bCount == 16)
                {
                    //res += "    *" + DisplayHexChars(buffer, j) + "*\n";
                    res += "\n";
                    bCount = 0;
                    j += 16;
                    res += "\tDUMP " + j.ToString("X4") + ":  ";
                }
                else
                    res += (bCount > 0) ? " " : "";

                res += b.ToString("X4");
                bCount++;
            }

            // if the byte count at this point is non-zero print the message
            if (bCount != 0)
            {
                if (bCount < 16)
                {
                    for (int i = bCount; i < 16; i++)
                        res += "   ";
                }

                //res += "    *" + DisplayHexChars(buffer, j) + "*";
            }

            return res;
        }

        /// <summary>
        /// Perform a hex dump of a buffer.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public static string HexDump(Memory<byte> buffer, int offset = 0)
        {
            int bCount = 0, j = 0;

            // iterate through buffer printing all the stored bytes
            string res = "\n\tDUMP " + j.ToString("X4") + ":  ";
            for (int i = offset; i < buffer.Length; i++)
            {
                byte b = buffer.Span[i];

                // split the message every 16 bytes...
                if (bCount == 16)
                {
                    res += "    *" + DisplayHexChars(buffer.Span, j) + "*\n";
                    bCount = 0;
                    j += 16;
                    res += "\tDUMP " + j.ToString("X4") + ":  ";
                }
                else
                    res += (bCount > 0) ? " " : "";

                res += b.ToString("X2");
                bCount++;
            }

            // if the byte count at this point is non-zero print the message
            if (bCount != 0)
            {
                if (bCount < 16)
                {
                    for (int i = bCount; i < 16; i++)
                        res += "   ";
                }

                res += "    *" + DisplayHexChars(buffer.Span, j) + "*";
            }

            return res;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] sha256_hash(string value)
        {
            return sha256_hash(Encoding.ASCII.GetBytes(value));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static byte[] sha256_hash(byte[] value)
        {
            using (SHA256 hash = SHA256Managed.Create())
                return hash.ComputeHash(value);
        }
    } // public class FneUtils
} // namespace fnecore
