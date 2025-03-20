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
using System.Collections.Generic;
using System.Linq;

namespace fnecore.P25.KMM
{
    /// <summary>
    /// Enc. Key Item
    /// </summary>
    public class KeyItem
    {
        private const int MAX_ENC_KEY_LENGTH = 32;
        private readonly byte[] _keyMaterial = new byte[MAX_ENC_KEY_LENGTH];

        public byte KeyFormat { get; set; } = 0x80;
        public ushort Sln { get; set; }
        public ushort KeyId { get; set; }
        public uint KeyLength { get; private set; }

        /// <summary>
        /// Creates an instance of <see cref="KeyItem"/>
        /// </summary>
        public KeyItem() { /* stub */ }

        /// <summary>
        /// Creates an instance of <see cref="KeyItem"/>
        /// </summary>
        /// <param name="other"></param>
        public KeyItem(KeyItem other)
        {
            KeyFormat = other.KeyFormat;
            Sln = other.Sln;
            KeyId = other.KeyId;
            SetKey(other._keyMaterial, other.KeyLength);
        }

        /// <summary>
        /// Set Enc. Key
        /// </summary>
        /// <param name="key"></param>
        /// <param name="keyLength"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void SetKey(byte[] key, uint keyLength)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (keyLength > MAX_ENC_KEY_LENGTH) throw new ArgumentOutOfRangeException(nameof(keyLength));

            KeyLength = keyLength;
            Array.Clear(_keyMaterial, 0, MAX_ENC_KEY_LENGTH);
            Array.Copy(key, _keyMaterial, keyLength);
        }

        /// <summary>
        /// Return the Enc. Key
        /// </summary>
        /// <returns>Enc. Key</returns>
        public byte[] GetKey()
        {
            return _keyMaterial.Take((int)KeyLength).ToArray();
        }
    } // public class KeyItem

    /// <summary>
    /// Keyset item
    /// </summary>
    public class KeysetItem
    {
        public byte KeysetId { get; set; }
        public byte AlgId { get; set; }
        public byte KeyLength { get; set; }
        public List<KeyItem> Keys { get; private set; } = new List<KeyItem>();

        /// <summary>
        /// Creates an instance of <see cref="KeysetItem"/>
        /// </summary>
        public KeysetItem() { /* stub */ }

        /// <summary>
        /// Creates an instance of <see cref="KeysetItem"/>
        /// </summary>
        /// <param name="other"></param>
        public KeysetItem(KeysetItem other)
        {
            KeysetId = other.KeysetId;
            AlgId = other.AlgId;
            KeyLength = other.KeyLength;
            Keys = other.Keys.Select(k => new KeyItem(k)).ToList();
        }

        /// <summary>
        /// Keyset length
        /// </summary>
        public uint Length => (uint)(4 + Keys.Count * (5 + KeyLength));

        /// <summary>
        /// Add <see cref="KeyItem"/> to the Keys list
        /// </summary>
        /// <param name="key"></param>
        public void AddKey(KeyItem key)
        {
            Keys.Add(key);
        }

        /// <summary>
        /// Overwrite Keys list
        /// </summary>
        /// <param name="keys"></param>
        public void SetKeys(IEnumerable<KeyItem> keys)
        {
            Keys = keys.ToList();
        }
    } // public class KeysetItem
} // namespace fnecore.P25.kmm
