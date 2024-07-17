﻿// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment Core Library
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2022,2024 Bryan Biedenkapp, N2PLL
*
*/

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace fnecore
{
    /// <summary>
    /// Structure representing a raw UDP packet frame.
    /// </summary>
    /// <remarks>"Frame" is used loosely here...</remarks>
    public struct UdpFrame
    {
        /// <summary>
        /// 
        /// </summary>
        public IPEndPoint Endpoint;
        /// <summary>
        /// 
        /// </summary>
        public byte[] Message;
    } // public struct UDPFrame

    /// <summary>
    /// Base class from which all UDP classes are derived.
    /// </summary>
    public abstract class UdpBase
    {
        protected const ushort AES_WRAPPED_PCKT_MAGIC = 0xC0FE;
        protected const int AES_BLOCK_SIZE = 128;

        protected UdpClient client;

        protected bool isCryptoWrapped;
        protected byte[] presharedKey;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpBase"/> class.
        /// </summary>
        protected UdpBase()
        {
            client = new UdpClient();

            isCryptoWrapped = false;
            presharedKey = null;
        }

        /// <summary>
        /// Sets the preshared encryption key.
        /// </summary>
        /// <param name="presharedKey"></param>
        public void SetPresharedKey(byte[] presharedKey)
        {
            if (presharedKey != null) {
                this.presharedKey = presharedKey;
                isCryptoWrapped = true;
            } else {
                this.presharedKey = null;
                isCryptoWrapped = false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<UdpFrame> Receive()
        {
            UdpReceiveResult res = await client.ReceiveAsync();

            byte[] buffer = res.Buffer;

            // are we crypto wrapped?
            if (isCryptoWrapped)
            {
                if (presharedKey == null)
                    throw new InvalidOperationException("tried to read datagram encrypted with no key? this shouldn't happen BUGBUG");

                // does the network packet contain the appropriate magic leader?
                ushort magic = FneUtils.ToUInt16(res.Buffer, 0);
                if (magic == AES_WRAPPED_PCKT_MAGIC)
                {
                    int cryptedLen = (res.Buffer.Length - 2) * sizeof(byte);
                    byte[] cryptoBuffer = new byte[res.Buffer.Length - 2];
                    Buffer.BlockCopy(res.Buffer, 0, cryptoBuffer, 0, res.Buffer.Length - 2);

                    // do we need to pad the original buffer to be block aligned?
                    if (cryptedLen % AES_BLOCK_SIZE != 0)
                    {
                        int alignment = AES_BLOCK_SIZE - (cryptedLen % AES_BLOCK_SIZE);
                        cryptedLen += alignment;

                        // reallocate buffer and copy
                        cryptoBuffer = new byte[cryptedLen];
                        Buffer.BlockCopy(res.Buffer, 0, cryptoBuffer, 0, res.Buffer.Length - 2);
                    }

                    // decrypt
                    byte[] decrypted = Decrypt(cryptoBuffer, presharedKey);

                    // finalize, cleanup buffers and replace with new
                    if (decrypted != null)
                        buffer = decrypted;
                    else
                        buffer = new byte[0];
                }
                else
                    buffer = new byte[0]; // this will effectively discard packets without the packet magic
            }

            return new UdpFrame()
            {
                Message = buffer,
                Endpoint = res.RemoteEndPoint
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        protected static byte[] Encrypt(byte[] buffer, byte[] key)
        {
            byte[] encrypted = null;
            using (AesManaged aes = new AesManaged()
            {
                KeySize = 256,
                Key = key,
                BlockSize = AES_BLOCK_SIZE,
                Mode = CipherMode.ECB,
                Padding = PaddingMode.Zeros,
                IV = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
            })
            {
                ICryptoTransform encryptor = aes.CreateEncryptor();
                using (MemoryStream ms = new MemoryStream())
                using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    cs.Write(buffer, 0, buffer.Length);
                    encrypted = ms.ToArray();
                }
            }
    
            return encrypted;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        protected static byte[] Decrypt(byte[] buffer, byte[] key)
        {
            byte[] decrypted = null;
            using (AesManaged aes = new AesManaged()
            {
                KeySize = 256,
                Key = key,
                BlockSize = 128,
                Mode = CipherMode.ECB,
                Padding = PaddingMode.Zeros,
                IV = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
            })
            {
                ICryptoTransform decryptor = aes.CreateDecryptor();
                using (MemoryStream ms = new MemoryStream())
                using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                {
                    byte[] outBuf = new byte[16384];
                    int len = cs.Read(outBuf, 0, outBuf.Length);
                    if (len > 0)
                    {
                        decrypted = new byte[len];
                        Buffer.BlockCopy(outBuf, 0, decrypted, 0, len);
                    }
                }
            }

            return decrypted;
        }
    } // public abstract class UDPBase

    /// <summary>
    /// 
    /// </summary>
    public class UdpReceiver : UdpBase
    {
        private IPEndPoint endpoint;

        /*
        ** Properties
        */

        /// <summary>
        /// Gets the <see cref="IPEndPoint"/> for this <see cref="UdpReceiver"/>.
        /// </summary>
        public IPEndPoint EndPoint => endpoint;

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpListener"/> class.
        /// </summary>
        public UdpReceiver()
        {
            /* stub */
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hostName"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public void Connect(string hostName, int port)
        {
            try
            {
                try
                {
                    endpoint = new IPEndPoint(IPAddress.Parse(hostName), port);
                }
                catch
                {
                    IPHostEntry entry = Dns.GetHostEntry(hostName);
                    if (entry.AddressList.Length > 0)
                    {
                        IPAddress address = entry.AddressList[0];
                        endpoint = new IPEndPoint(address, port);
                    }
                }
            }
            catch
            {
                return;
            }

            client.Connect(endpoint.Address.ToString(), endpoint.Port); 
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        public void Connect(IPEndPoint endpoint)
        {
            UdpReceiver recv = new UdpReceiver();
            this.endpoint = endpoint;
            client.Connect(endpoint.Address.ToString(), endpoint.Port);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="frame"></param>
        public void Send(UdpFrame frame)
        {
            byte[] buffer = frame.Message;

            // are we crypto wrapped?
            if (isCryptoWrapped)
            {
                if (presharedKey == null)
                    throw new InvalidOperationException("tried to read datagram encrypted with no key? this shouldn't happen BUGBUG");

                int cryptedLen = buffer.Length * sizeof(byte);
                byte[] cryptoBuffer = new byte[buffer.Length];
                Buffer.BlockCopy(buffer, 0, cryptoBuffer, 0, buffer.Length);

                // do we need to pad the original buffer to be block aligned?
                if (cryptedLen % AES_BLOCK_SIZE != 0)
                {
                    int alignment = AES_BLOCK_SIZE - (cryptedLen % AES_BLOCK_SIZE);
                    cryptedLen += alignment;

                    // reallocate buffer and copy
                    cryptoBuffer = new byte[cryptedLen];
                    Buffer.BlockCopy(buffer, 0, cryptoBuffer, 0, buffer.Length);
                }

                // encrypt
                byte[] crypted = Encrypt(cryptoBuffer, presharedKey);

                // finalize, cleanup buffers and replace with new
                buffer = new byte[cryptedLen + 2];
                if (crypted != null)
                {
                    Buffer.BlockCopy(crypted, 0, buffer, 2, cryptedLen);
                    FneUtils.WriteBytes(AES_WRAPPED_PCKT_MAGIC, ref buffer, 0);
                }
                else
                    return;
            }

            client.Send(buffer, frame.Message.Length);
        }
    } // public class UdpReceiver : UdpBase
} // namespace fnecore
