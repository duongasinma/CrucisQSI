using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace BotTradingCrypto.Domain.Utilities
{
    public static class NumberHandler
    {
        public static int GetPrecisionFromMantissa(decimal d)
        {
            // 1. Extract internal bits
            var bits = decimal.GetBits(d);
            int scale = (bits[3] >> 16) & 0xFF;   // stored scale (trailing zeros included)

            // 2. Rebuild the 96-bit integer mantissa
            BigInteger mantissa =
                 ((BigInteger)(uint)bits[2] << 64) |
                 ((BigInteger)(uint)bits[1] << 32) |
                                  (uint)bits[0];

            // 3. Peel off trailing zeros
            while (scale > 0 && mantissa % 10 == 0)
            {
                mantissa /= 10;
                scale--;
            }

            // 4. Remaining scale is significant decimals
            return scale;
        }

    }
}
