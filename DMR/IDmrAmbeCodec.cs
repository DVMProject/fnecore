// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 C. Lovell, Dev_Ranger
using System;

namespace fnecore.DMR
{
    /// <summary>Adapts a 72-bit DMR AMBE codeword to its 49 natural parameter bits.</summary>
    public interface IDmrAmbeCodec
    {
        int Decode(byte[] codeword, byte[] parameters);
        void Encode(byte[] parameters, byte[] codeword);
    }
}
