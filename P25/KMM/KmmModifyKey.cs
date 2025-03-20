// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Fixed Network Equipment Core Library
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment Core Library
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Caleb, K4PHP
*
*/

using System;
using System.Linq;

namespace fnecore.P25.KMM
{
    /// <summary>
    /// KMM Modify Key
    /// </summary>
    public class KmmModifyKey : KmmFrame
    {
        private byte[] _mi;

        public const int KMM_MODIFY_KEY_LENGTH = 21;

        public byte DecryptInfoFmt { get; set; } = P25Defines.KMM_DECRYPT_INSTRUCTION_NONE;
        public byte AlgId { get; set; }
        public ushort KeyId { get; set; }
        public KeysetItem KeysetItem { get; set; } = new KeysetItem();

        /// <summary>
        /// Creates an instance <see cref="KmmModifyKey"/>
        /// </summary>
        public KmmModifyKey()
        {
            MessageId = (byte)KmmMessageType.MODIFY_KEY_CMD;
            _mi = new byte[P25Defines.P25_MI_LENGTH];
        }

        /// <summary>
        /// KMM Frame Length
        /// </summary>
        public override ushort Length => 21;

        /// <summary>
        /// Encode a KMM Modify Key
        /// </summary>
        /// <param name="data"></param>
        public void Encode(byte[] data)
        {
            EncodeHeader(data);

            data[10] = DecryptInfoFmt;
            data[11] = AlgId;
            FneUtils.WriteBytes(KeyId, ref data, 12);

            int offset = 14;
            if (DecryptInfoFmt == P25Defines.KMM_DECRYPT_INSTRUCTION_MI)
            {
                Array.Copy(_mi, 0, data, offset, P25Defines.P25_MI_LENGTH);
                offset += P25Defines.P25_MI_LENGTH;
            }

            data[offset] = KeysetItem.KeysetId;
            data[offset + 1] = KeysetItem.AlgId;
            data[offset + 2] = KeysetItem.KeyLength;
            data[offset + 3] = (byte)KeysetItem.Keys.Count;
            offset += 4;

            foreach (var key in KeysetItem.Keys)
            {
                int keyNameLen = key.KeyFormat & 0x1F;
                data[offset] = (byte)(key.KeyFormat | keyNameLen);
                FneUtils.WriteBytes(key.Sln, ref data, offset + 1);
                FneUtils.WriteBytes(key.KeyId, ref data, offset + 3);
                key.GetKey().CopyTo(data, offset + 5 + keyNameLen);
                offset += 5 + keyNameLen + KeysetItem.KeyLength;
            }
        }

        /// <summary>
        /// Decode a KMM Modify Key
        /// </summary>
        /// <param name="data"></param>
        public void Decode(byte[] data)
        {
            DecodeHeader(data);

            DecryptInfoFmt = data[10];
            AlgId = data[11];
            KeyId = FneUtils.ToUInt16(data, 12);

            int offset = 14;
            if (DecryptInfoFmt == P25Defines.KMM_DECRYPT_INSTRUCTION_MI)
            {
                Array.Copy(data, offset, _mi, 0, P25Defines.P25_MI_LENGTH);
                offset += P25Defines.P25_MI_LENGTH;
            }

            KeysetItem.KeysetId = data[offset];
            KeysetItem.AlgId = data[offset + 1];
            KeysetItem.KeyLength = data[offset + 2];
            int keyCount = data[offset + 3];
            offset += 4;

            KeysetItem.Keys.Clear();
            for (int i = 0; i < keyCount; i++)
            {
                KeyItem key = new KeyItem();
                int keyNameLen = data[offset] & 0x1F;
                key.KeyFormat = data[offset];
                key.Sln = FneUtils.ToUInt16(data, offset + 1);
                key.KeyId = FneUtils.ToUInt16(data, offset + 3);
                key.SetKey(
                    data.Skip(offset + 5 + keyNameLen).Take((int)KeysetItem.KeyLength).ToArray(),
                    KeysetItem.KeyLength
                );

                offset += 5 + keyNameLen + KeysetItem.KeyLength;
                KeysetItem.Keys.Add(key);
            }
        }
    } // public class KmmModifyKey
} // namespace fnecore.P25.kmm