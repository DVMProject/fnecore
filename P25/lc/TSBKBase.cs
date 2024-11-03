using System;

using fnecore.EDAC;

namespace fnecore.P25.LC
{
    /// <summary>
    /// Base TSBK Encode/Decode class
    /// </summary>
    public abstract class TSBKBase
    {
        protected bool LastBlock;
        protected byte Lco;
        protected byte MfId;

        /// <summary>
        /// Creates an instance of <see cref="TSBKBase"/>
        /// </summary>
        protected TSBKBase()
        {
            MfId = P25Defines.P25_MFG_STANDARD;
        }

        /// <summary>
        /// Decode a TSBK
        /// </summary>
        /// <param name="data"></param>
        /// <param name="payload"></param>
        /// <param name="rawTSBK"></param>
        /// <returns></returns>
        public virtual bool Decode(byte[] data, ref byte[] payload, bool rawTSBK)
        {
            byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];
            FneUtils.Memset(tsbk, 0x00, tsbk.Length);

            if (rawTSBK)
            {
                Array.Copy(data, tsbk, P25Defines.P25_TSBK_LENGTH_BYTES);

                if (!CRC.CheckCCITT162(tsbk, P25Defines.P25_TSBK_LENGTH_BYTES))
                {
                    if ((tsbk[P25Defines.P25_TSBK_LENGTH_BYTES - 2U] != 0x00U) && (tsbk[P25Defines.P25_TSBK_LENGTH_BYTES - 1U] != 0x00U))
                    {
                        Console.WriteLine("TSBK failed CRC CCITT-162 check");
                        return false;
                    }
                }
            }
            else
            {
                byte[] raw = new byte[P25Defines.P25_TSBK_FEC_LENGTH_BYTES];
                P25Interleaver.Decode(data, ref raw, 114, 318);

                EDAC.Trellis trellis = new EDAC.Trellis();
                if (!trellis.Decode12(raw, ref tsbk))
                {
                    Console.WriteLine("TSBK Failed Trellis decode");
                    return false;
                }

                if (!CRC.CheckCCITT162(tsbk, P25Defines.P25_TSBK_LENGTH_BYTES))
                {
                    Console.WriteLine("TSBK Failed CRC check after Trellis");
                    return false;
                }
            }

            Lco = (byte)(tsbk[0] & 0x3F);                // LCO 
            LastBlock = (tsbk[0] & 0x80) == 0x80;        // Last Block Marker
            MfId = tsbk[1];                              // Manufacturer ID

            Array.Copy(tsbk, 1, payload, 0, P25Defines.P25_TSBK_LENGTH_BYTES - 4);

            return true;
        }

        /// <summary>
        /// Encode a TSBK
        /// </summary>
        /// <param name="data"></param>
        /// <param name="payload"></param>
        /// <param name="rawTSBK"></param>
        /// <param name="noTrellis"></param>
        public virtual void Encode(ref byte[] data, ref byte[] payload, bool rawTSBK, bool noTrellis)
        {
            byte[] tsbk = new byte[P25Defines.P25_TSBK_LENGTH_BYTES];

            FneUtils.Memset(tsbk, 0x00, tsbk.Length);

            Array.Copy(payload, 0, tsbk, 2, P25Defines.P25_TSBK_LENGTH_BYTES - 4);

            tsbk[0] = Lco;                                      // LCO
            tsbk[0] |= LastBlock ? (byte)0x80 : (byte)0x00;     // Last Block Marker
            tsbk[1] = MfId;                                     // Manufacturer ID

            CRC.AddCCITT162(ref tsbk, P25Defines.P25_TSBK_LENGTH_BYTES);

            byte[] raw = new byte[P25Defines.P25_TSBK_FEC_LENGTH_BYTES];
            FneUtils.Memset(raw, 0x00, raw.Length);

            EDAC.Trellis trellis = new EDAC.Trellis();
            trellis.Encode12(tsbk, ref raw);

            if (rawTSBK)
            {
                if (noTrellis)
                {
                    Array.Copy(tsbk, 0, data, 0, P25Defines.P25_TSBK_LENGTH_BYTES);
                }
                else
                {
                    Array.Copy(raw, 0, data, 0, P25Defines.P25_TSBK_FEC_LENGTH_BYTES);
                }
            }
            else
            {
                P25Interleaver.Encode(raw, ref data, 114, 318);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return "UNKNOWN TSBK";
        }
    } // public abstract class TSBKBase
} // namespace fnecore.P25.LC
