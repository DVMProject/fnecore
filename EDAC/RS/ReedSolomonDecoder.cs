/**
* Digital Voice Modem - Fixed Network Equipment
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment
*
*/
//
// Based on code from the MMDVMHost project. (https://github.com/g4klx/MMDVMHost)
// Licensed under the GPLv2 License (https://opensource.org/licenses/GPL-2.0)
//
/*
*   Copyright (C) 2016 by Jonathan Naylor G4KLX
*   Copyright (C) 2017-2023 by Bryan Biedenkapp N2PLL
*
*   This program is free software; you can redistribute it and/or modify
*   it under the terms of the GNU General Public License as published by
*   the Free Software Foundation; either version 2 of the License, or
*   (at your option) any later version.
*
*   This program is distributed in the hope that it will be useful,
*   but WITHOUT ANY WARRANTY; without even the implied warranty of
*   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
*   GNU General Public License for more details.
*
*   You should have received a copy of the GNU General Public License
*   along with this program; if not, write to the Free Software
*   Foundation, Inc., 675 Mass Ave, Cambridge, MA 02139, USA.
*/

using System;
using System.Linq;

namespace fnecore.EDAC.RS
{
    /// <summary>
    /// Implements Reed-Solomon decoding, as the name implies.
    /// </summary>
    /// <remarks>This does not do error correction, only detection.</remarks>
    internal sealed class ReedSolomonDecoder
    {
        private readonly int fieldSize;
        private readonly int messageSymbols;
        private readonly int paritySymbols;

        private readonly int fieldGenPoly;

        private readonly GaloisField gf;

        private readonly int[] syndroms;

        private readonly int[] lambda;
        private readonly int[] corrPoly;
        private readonly int[] lambdaStar;

        private readonly int[] lambdaPrime;

        private readonly int[] omega;

        private readonly int[] errorIndexes;

        private readonly int[] chienCache;

        /*
        ** Properties
        */

        /// <summary>
        /// The number of symbols that make up an entire encoded message. An encoded message is composed of the
        /// original data symbols plus parity symbols.
        /// </summary>
        public int BlockSize { get; private set; }

        /// <summary>
        /// The number of symbols per block that store original message symbols.
        /// </summary>
        public int MessageSize
        {
            get { return this.messageSymbols; }
        }

        /*
        ** Methods
        */

        /// <summary>
        /// Initializes a new instance of the <see cref="ReedSolomonDecoder"/> class.
        /// </summary>
        /// <param name="fieldSize">The size of the Galois field to create. Must be a value that is 
        /// a power of two. The length of the output block is set to `fieldSize - 1`.</param>
        /// <param name="messageSymbols">The number of original message symbols per block.</param>
        /// <param name="paritySymbols">The number of parity symbols per block.</param>
        /// <param name="fieldGenPoly">A value representing the field generator polynomial, 
        /// which must be order N for a field GF(2^N).</param>
        /// <remarks>BlockSize is equal to `fieldSize - 1`. messageSymbols plus paritySymbols must equal BlockSize.</remarks>
        public ReedSolomonDecoder(int fieldSize, int messageSymbols, int paritySymbols, int fieldGenPoly)
        {
            if (fieldSize - 1 != messageSymbols + paritySymbols)
                throw new ArgumentOutOfRangeException("Invalid reed-solomon block parameters were provided - the number of message symbols plus the number of parity symbols " +
                    "does not add up to the size of a block");

            this.fieldSize = fieldSize;
            this.messageSymbols = messageSymbols;
            this.paritySymbols = paritySymbols;
            this.BlockSize = fieldSize - 1;

            this.fieldGenPoly = fieldGenPoly;

            this.gf = new GaloisField(fieldSize, fieldGenPoly);

            // Syndrom calculation buffers
            this.syndroms = new int[paritySymbols];

            // Lamda calculation buffers
            this.lambda = new int[paritySymbols - 1];
            this.corrPoly = new int[paritySymbols - 1];
            this.lambdaStar = new int[paritySymbols - 1];

            // LambdaPrime calculation buffers
            this.lambdaPrime = new int[paritySymbols - 2];

            // Omega calculation buffers
            this.omega = new int[paritySymbols - 2];

            // Error position calculation
            this.errorIndexes = new int[fieldSize - 1];

            // Cache of the lookup used in the ChienSearch process.
            this.chienCache = new int[fieldSize - 1];

            for (int i = 0; i < this.chienCache.Length; i++)
                this.chienCache[i] = gf.Inverses[gf.Field[i + 1]];
        }

        /// <summary>
        /// Discovers and corrects any errors in the block provided.
        /// </summary>
        /// <param name="message">A block containing a reed-solomon encoded message.</param>
        public byte[] DecodeEx(byte[] message)
        {
            int[] received = message.Select(x => (int)x).ToArray();
            Decode(ref received);

            return received.Take(message.Length).Select(x => (byte)x).ToArray();
        }

        /// <summary>
        /// Discovers and corrects any errors in the block provided.
        /// </summary>
        /// <param name="message">A block containing a reed-solomon encoded message.</param>
        public void Decode(ref int[] message)
        {
            if (message.Length != this.BlockSize)
                throw new ArgumentException("The provided message's size was not the size of a block");

            CalcSyndromPoly(message);
            CalcLambda();
            CalcLambdaPrime();
            CalcOmega();

            ChienSearch();

            RepairErrors(ref message, errorIndexes, omega, lambdaPrime);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="errorIndexes"></param>
        /// <param name="omega"></param>
        /// <param name="lp"></param>
        private void RepairErrors(ref int[] message, int[] errorIndexes, int[] omega, int[] lp)
        {
            int top, bottom, x, xInverse;
            int messageLen = message.Length;

            for (int i = 0; i < messageLen; i++)
            {
                // If i = 2, then use a^2 in the evaluation.
                // remember that field[i + 1] = a^i, since field[0] = 0 and field[1] = a^0.

                // i = 2
                // a^2 is the element. a^2 = 4
                // 
                // 13 = 4 * [6*(4^-1)^2 + 15] / 14

                // 8 ^ 15 = 7
                // 6 * a^-2 = 8
                // 8 + 15 = 7

                if (errorIndexes[i] == 0)
                {
                    // This spot has an error.
                    // Equation:
                    //      value = X_J * [ Omega(1/X_J) / L'(1/X_J) ];
                    // Break it down to:
                    //      top = eval(omega, 1/X_J);
                    //      top = X_J * top;
                    //      bottom = eval( lambaPrime, 1/X_J );
                    //      errorMagnitude = top / bottom;
                    //     
                    // To repair the message, we add the error magnitude to the received value.
                    //      message[i] = message[i] ^ errorMagnitude

                    x = gf.Field[i + 1];

                    xInverse = gf.Inverses[x];

                    top = gf.PolyEval(omega, xInverse);
                    top = gf.Multiply(top, x);
                    bottom = gf.PolyEval(lp, xInverse);

                    message[i] ^= gf.Divide(top, bottom);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private void CalcLambda()
        {
            // Explanation of terms:
            // S  = S(x)     - syndrom polynomial
            // C  = C(x)     - correction polynomial
            // D  = 'lamba'(x) - error locator estimate polynomial.
            // D* = 'lambda-star'(x) - a new error locator estimate polynomial.
            // S_x = the element at index 'x' in S, eg, if S = {5,6,7,8}, then S_0 = 5, S_1 = 6, etc.
            // 2T  = the number of error correction symbols, eg, numCheckBytes.
            //       T must be >= 1, so 2T is guarenteed to be at least 2. 
            //
            // Start with 
            //   K = 1;
            //   L = 0; 
            //   C = 0x^n + ... + 0x^2 + x + 0 aka {0,1,0, ...};
            //   D = 0x^n + ... + 0x^2 + 0x + 1 aka {1,0,0,...};
            //     Both C and D are guarenteed to be at least 2 elements, which is why they can have
            //     hardcoded initial values.

            // Step 1: Calculate e.
            // --------------
            //  e = S_(K-1) + sum(from i=1 to L: D_i * S_(K-1-i)
            //
            // Example
            //                           0   1   2         0  1  2   3
            // K = 4, L = 2; D = {1, 11, 15}; S = {15, 3, 4, 12}
            //         
            //  e = S_3 + sum(from i = 1 to 2: D_i * S_(3- i)
            //    = 12 + D_1 * S_2 + D_2 * S_1
            //    = 12 +  11 *  4  +  15 *  3
            //    = 12 +  10       +   2
            //    = 12 XOR 10 XOR 2 
            //    = 4

            // Step 2: Update estimate if e != 0
            //
            // If e != 0 { 
            //      D*(x) = D(x) + e * C(X)  -- Note that this just assigns D(x) to D*(x) if e is zero.
            //      If 2L < k {
            //          L = K - L
            //          C(x) = D(x) * e^(-1) -- Multiply D(x) times the multiplicative inverse of e.
            //      }
            // }

            // Step 3: Advance C(x):
            //   C(x) = C(x) * x  
            //     This just shifts the coeffs down; eg, x + 1 {1, 1, 0} turns into x^2 + x {0, 1, 1}
            //   
            //   D(x) = D*(x) (only if a new D*(x) was calulated)
            //  
            //   K = K + 1

            // Step 4: Compute end conditions
            //   If K <= 2T goto 1
            //   Else, D(x) is the error locator polynomial.

            int k, l, e;
            int eInv; // temp to store calculation of 1 / e aka e^(-1)

            // --- Initial conditions ----
            // Need to clear lambda and corrPoly, but not lambdaStar. lambda and corrPoly 
            // are used and initialized iteratively in the algorithm, whereas lambdaStar isn't.
            Array.Clear(corrPoly, 0, corrPoly.Length);
            Array.Clear(lambda, 0, lambda.Length);
            k = 1;
            l = 0;
            corrPoly[1] = 1;
            lambda[0] = 1;

            while (k <= paritySymbols)
            {
                // --- Calculate e ---
                e = syndroms[k - 1];

                for (int i = 1; i <= l; i++)
                    e ^= gf.Multiply(lambda[i], syndroms[k - 1 - i]);

                // --- Update estimate if e != 0 ---
                if (e != 0)
                {
                    // D*(x) = D(x) + e * C(x);
                    for (int i = 0; i < lambdaStar.Length; i++)
                        lambdaStar[i] = lambda[i] ^ gf.Multiply(e, corrPoly[i]);

                    if (2 * l < k)
                    {
                        // L = K - L;
                        l = k - l;

                        // C(x) = D(x) * e^(-1);
                        eInv = gf.Inverses[e];
                        for (int i = 0; i < corrPoly.Length; i++)
                            corrPoly[i] = gf.Multiply(lambda[i], eInv);
                    }
                }

                // --- Advance C(x) ---

                // C(x) = C(x) * x
                for (int i = corrPoly.Length - 1; i >= 1; i--)
                    corrPoly[i] = corrPoly[i - 1];
                corrPoly[0] = 0;

                if (e != 0)
                {
                    // D(x) = D*(x);
                    Array.Copy(lambdaStar, lambda, lambda.Length);
                }

                k += 1;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private void CalcLambdaPrime()
        {
            // Forney's says that we can just set even powers to 0 and then take the rest and 
            // divide it by x (shift it down one). 

            // No need to clear this.lambdaPrime between calls; full assignment is done every call.
            for (int i = 0; i < lambdaPrime.Length; i++)
                if ((i & 0x1) == 0)
                    lambdaPrime[i] = lambda[i + 1];
                else
                    lambdaPrime[i] = 0;
        }

        /// <summary>
        /// 
        /// </summary>
        private void CalcOmega()
        {
            // O(x) is shorthand for Omega(x).
            // L(x) is shorthand for Lambda(x).
            // 
            // O_i is the coefficient of the term in omega with degree i. Ditto for L_i.
            // Eg, O(x) = 6x + 15;  O_0 = 15, O_1 = 6
            // 
            // From the paper:
            // O_0 = S_b
            //   ---> b in our implementation is 0.
            // O_1 = S_{b+1} + S_b * L_1
            //
            // O_{v-1} = S_{b+v-1} + S_{b+v-2} * L_1 + ... + S_b * L_{v-1}.
            // O_i = S_{b+i} + S_{b+ i-1} * L_1  + ... + S_{b+0} * L_i

            // Lets say :
            //   L(x) = 14x^2 + 14x + 1         aka {1, 14, 14}.
            //   S(x) = 12x^3 + 4x^2 + 3x + 15  aka {15, 3, 4, 12}
            //   b = 0;
            //   v = 2 because the power of the highest monomial in L(x), 14x^2, is 2.
            // 
            // O_0 = S_{b} = S_0 = 15
            // O_1 = S_{b+1} + S_b * L_1 = S_1 + S_0 * L_1 = 3 + 15 * 14 = 6.
            // 
            // O(x) = 6x + 15.

            // Lets make up another example (these are completely made up so they may not work):
            //   L(x) = 10x^3 + 9x^2 + 8x + 7       aka { 7, 8, 9, 10}
            //   S(x) = 2^4 + 3x^3 + 4x^2 + 5x + 6  aka { 6, 5, 4, 3, 2}
            //   b = 0 (design parameter)
            //   v = 3

            // O_i for i = 0 .. v - 1 = 2. Thus, O has form ax^2 + bx^1 + cx^0
            // Compute O_0, O_1, O_2

            // O_0 = S_{b+0}
            //     = S_0
            //
            // O_1 = S_{b+1} + S_{b+0} * L_1
            //     = S_1 + S_0 * L_1
            //
            // O_2 = S_{b+2} + S_{b+1} * L_1 + S_{b+0} * L_2
            //     + S_2 + S_1 * L_1 + S_0 * L_2

            // Don't need to zero this.omega first - it's assigned to before we use it.

            for (int i = 0; i < omega.Length; i++)
            {
                omega[i] = syndroms[i];
                for (int lIter = 1; lIter <= i; lIter++)
                    omega[i] ^= gf.Multiply(syndroms[i - lIter], lambda[lIter]);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private void ChienSearch()
        {
            // The cheap chien search evaluates the lamba polynomial for the multiplicate inverse 
            // each element in the field other than 0.

            // Don't need to zero this.errorIndexes first - it's not used before its assigned to.

            /*
             * This loop uses an optimization where I precalculate one lookup. 
             * The code before the optimization was:
             * for( int i = 0; i < errorIndexes.Length; i++ )
             *  {
             *      errorIndexes[i] = gf.PolyEval(
             *          lambda,
             *          gf.Inverses[ gf.Field[ i + 1] ]
             *      );
             *  }
             * We precompute the lookup gf.Inverses[ gf.Field[ i + 1] ] when creating the decoder.
             */

            for (int i = 0; i < errorIndexes.Length; i++)
                errorIndexes[i] = gf.PolyEval(lambda, chienCache[i]);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        private void CalcSyndromPoly(int[] message)
        {
            int syndrome, root;

            // Don't need to zero this.syndromes first - it's not used before its assigned to.
            for (int synIndex = 0; synIndex < syndroms.Length; synIndex++)
            {
                // EG, if g(x) = (x+a^0)(x+a^1)(x+a^2)(x+a^3) 
                //             = (x+1)(x+2)(x+4)(x+8),
                // Then for the first syndrom S_0, we would provide root = a^0 = 1.
                // S_1 --> root = a^1 = 2 etc.

                root = gf.Field[synIndex + 1];
                syndrome = 0;

                for (int i = message.Length - 1; i > 0; i--)
                    syndrome = gf.Multiply((syndrome ^ message[i]), root);

                syndroms[synIndex] = syndrome ^ message[0];
            }
        }
    } // internal sealed class ReedSolomonDecoder
} // namespace fnecore.EDAC.RS
