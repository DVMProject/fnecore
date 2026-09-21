// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 C. Lovell, Dev_Ranger
namespace fnecore.NXDN
{
    /// <summary>
    /// Supplies AMBE FEC/interleave without coupling FNECore to a native vocoder.
    /// Codewords contain nine bytes; parameters contain 49 one-byte bits.
    /// </summary>
    public interface INxdnAmbeCodec
    {
        int Decode(byte[] codeword, byte[] parameters);
        void Encode(byte[] parameters, byte[] codeword);
    }
}
