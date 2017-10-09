﻿using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;

namespace FastUtf8Tester
{
    internal static partial class Utf8Util
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static bool IntPtrIsLessThan(IntPtr a, IntPtr b) => (a.ToPointer() < b.ToPointer());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int IntPtrToInt32NoOverflowCheck(IntPtr value)
        {
            if (IntPtr.Size == 4)
            {
                return (int)value;
            }
            else
            {
                return (int)(long)value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidTrailingByte(uint value)
        {
            return ((value & 0xC0U) == 0x80U);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWithinRangeInclusive(uint value, uint lowerBound, uint upperBound) => ((value - lowerBound) <= (value - upperBound));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint GenerateUtf16CodeUnitFromUtf8CodeUnits(uint firstByte, uint secondByte)
        {
            return ((firstByte & 0x1FU) << 6)
                | (secondByte & 0x3FU);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint GenerateUtf16CodeUnitFromUtf8CodeUnits(uint firstByte, uint secondByte, uint thirdByte)
        {
            return ((firstByte & 0x0FU) << 12)
                   | ((secondByte & 0x3FU) << 6)
                   | (secondByte & 0x3FU);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint GenerateUtf16CodeUnitsFromUtf8CodeUnits(uint firstByte, uint secondByte, uint thirdByte, uint fourthByte)
        {
            // This method needs to generate a surrogate pair.
            // RETURN VALUE IS BIG ENDIAN

            uint retVal = ((firstByte & 0x3U) << 24)
                  | ((secondByte & 0x3FU) << 18)
                  | ((thirdByte & 0x30U) << 12)
                  | ((thirdByte & 0x0FU) << 6)
                  | (fourthByte & 0x3FU);
            retVal -= 0x400000U; // convert uuuuu to wwww per Table 3-5
            retVal += 0xD800DC00U; // add surrogate markers back in
            return retVal;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint GenerateUtf16CodeUnitsFromFourUtf8CodeUnits(uint utf8)
        {
            // input and output are in machine order
            if (BitConverter.IsLittleEndian)
            {
                // UTF8 [ 10xxxxxx 10yyyyyy 10uuzzzz 11110uuu ] = scalar 000uuuuu zzzzyyyy yyxxxxxx
                // UTF16 scalar 000uuuuuzzzzyyyyyyxxxxxx = [ 110111yy yyxxxxxx 110110ww wwzzzzyy ]
                // where wwww = uuuuu - 1
                uint retVal = (utf8 & 0x0F0000U) << 6; // retVal = [ 000000yy yy000000 00000000 00000000 ]
                retVal |= (utf8 & 0x3F000000U) >> 8; // retVal = [ 000000yy yyxxxxxx 00000000 00000000 ]
                retVal |= (utf8 & 0xFFU) << 8; // retVal = [ 000000yy yyxxxxxx 11110uuu 00000000 ]
                retVal |= (utf8 & 0x3F00U) >> 6; // retVal = [ 000000yy yyxxxxxx 11110uuu uuzzzz00 ]
                retVal |= (utf8 & 0x030000U) >> 16; // retVal = [ 000000yy yyxxxxxx 11110uuu uuzzzzyy ]
                retVal -= 0x40U;// retVal = [ 000000yy yyxxxxxx 111100ww wwzzzzyy ]
                retVal -= 0x2000U; // retVal = [ 000000yy yyxxxxxx 110100ww wwzzzzyy ]
                retVal += 0x0800U; // retVal = [ 000000yy yyxxxxxx 110110ww wwzzzzyy ]
                retVal += 0xDC000000U; // retVal = [ 110111yy yyxxxxxx 110110ww wwzzzzyy ]
                return retVal;
            }
            else
            {
                // UTF8 [ 11110uuu 10uuzzzz 10yyyyyy 10xxxxxx ] = scalar 000uuuuu zzzzyyyy yyxxxxxx
                // UTF16 scalar 000uuuuuxxxxxxxxxxxxxxxx = [ 110110wwwwxxxxxx 110111xxxxxxxxx ]
                // where wwww = uuuuu - 1
                if (Bmi2.IsSupported)
                {
                    uint retVal = Bmi2.ParallelBitDeposit(Bmi2.ParallelBitExtract(utf8, 0x0F3F3F00U), 0x03FF03FFU); // retVal = [ 00000uuuuuzzzzyy 000000yyyyxxxxxx ]
                    retVal -= 0x4000U; // retVal = [ 000000wwwwzzzzyy 000000yyyyxxxxxx ]
                    retVal += 0xD800DC00U; // retVal = [ 110110wwwwzzzzyy 110111yyyyxxxxxx ]
                    return retVal;
                }
                else
                {
                    uint retVal = utf8 & 0xFF000000U; // retVal = [ 11110uuu 00000000 00000000 00000000 ]
                    retVal |= (utf8 & 0x3F0000U) << 2; // retVal = [ 11110uuu uuzzzz00 00000000 00000000 ]
                    retVal |= (utf8 & 0x3000U) << 4; // retVal = [ 11110uuu uuzzzzyy 00000000 00000000 ]
                    retVal |= (utf8 & 0x0F00U) >> 2; // retVal = [ 11110uuu uuzzzzyy 000000yy yy000000 ]
                    retVal |= (utf8 & 0x3FU); // retVal = [ 11110uuu uuzzzzyy 000000yy yyxxxxxx ]
                    retVal -= 0x20000000U; // retVal = [ 11010uuu uuzzzzyy 000000yy yyxxxxxx ]
                    retVal -= 0x400000U; // retVal = [ 110100ww wwzzzzyy 000000yy yyxxxxxx ]
                    retVal += 0xDC00U; // retVal = [ 110100ww wwzzzzyy 110111yy yyxxxxxx ]
                    retVal += 0x08000000U; // retVal = [ 110110ww wwzzzzyy 110111yy yyxxxxxx ]
                    return retVal;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Utf8DWordBeginsWithTwoByteMask(uint value)
        {
            // The code in this method is equivalent to the code
            // below, but the JITter is able to inline + optimize it
            // better in release builds.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     const uint mask = 0x0000C0E0U;
            //     const uint comparand = 0x000080C0U;
            //     return ((value & mask) == comparand);
            // }
            // else
            // {
            //     const uint mask = 0xE0C00000U;
            //     const uint comparand = 0xC0800000U;
            //     return ((value & mask) == comparand);
            // }

            return (BitConverter.IsLittleEndian && ((value & 0x0000C0E0U) == 0x000080C0U))
                || (!BitConverter.IsLittleEndian && ((value & 0xE0C00000U) == 0xC0800000U));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Utf8DWordEndsWithTwoByteMask(uint value)
        {
            // The code in this method is equivalent to the code
            // below, but the JITter is able to inline + optimize it
            // better in release builds.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     const uint mask = 0xC0E00000U;
            //     const uint comparand = 0x80C00000U;
            //     return ((value & mask) == comparand);
            // }
            // else
            // {
            //     const uint mask = 0x0000E0C0U;
            //     const uint comparand = 0x0000C080U;
            //     return ((value & mask) == comparand);
            // }

            return (BitConverter.IsLittleEndian && ((value & 0xC0E00000U) == 0x80C00000U))
                  || (!BitConverter.IsLittleEndian && ((value & 0x0000E0C0U) == 0x0000C080U));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Utf8DWordBeginsAndEndsWithTwoByteMask(uint value)
        {
            // The code in this method is equivalent to the code
            // below, but the JITter is able to inline + optimize it
            // better in release builds.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     const uint mask = 0xC0E0C0E0U;
            //     const uint comparand = 0x80C080C0U;
            //     return ((value & mask) == comparand);
            // }
            // else
            // {
            //     const uint mask = 0xE0C0E0C0U;
            //     const uint comparand = 0xC080C080U;
            //     return ((value & mask) == comparand);
            // }

            return (BitConverter.IsLittleEndian && ((value & 0xC0E0C0E0U) == 0x80C080C0U))
                || (!BitConverter.IsLittleEndian && ((value & 0xE0C0E0C0U) == 0xC080C080U));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Utf8DWordBeginsWithThreeByteMask(uint value)
        {
            // The code in this method is equivalent to the code
            // below, but the JITter is able to inline + optimize it
            // better in release builds.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     const uint mask = 0x00C0C0F0U;
            //     const uint comparand = 0x008080E0U;
            //     return ((value & mask) == comparand);
            // }
            // else
            // {
            //     const uint mask = 0xF0C0C000U;
            //     const uint comparand = 0xE0808000U;
            //     return ((value & mask) == comparand);
            // }

            return (BitConverter.IsLittleEndian && ((value & 0x00C0C0F0U) == 0x008080E0U))
                   || (!BitConverter.IsLittleEndian && ((value & 0xF0C0C000U) == 0xE0808000U));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Utf8DWordBeginsWithFourByteMask(uint value)
        {
            // The code in this method is equivalent to the code
            // below, but the JITter is able to inline + optimize it
            // better in release builds.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     const uint mask = 0xC0C0C0F8U;
            //     const uint comparand = 0x808080F0U;
            //     return ((value & mask) == comparand);
            // }
            // else
            // {
            //     const uint mask = 0xF8C0C0C0U;
            //     const uint comparand = 0xF0808000U;
            //     return ((value & mask) == comparand);
            // }

            return (BitConverter.IsLittleEndian && ((value & 0xC0C0C0F8U) == 0x808080F0U))
                   || (!BitConverter.IsLittleEndian && ((value & 0xF8C0C0C0U) == 0xF0808000U));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Utf8DWordEndsWithThreeByteSequenceMarker(uint value)
        {
            // The code in this method is equivalent to the code
            // below, but the JITter is able to inline + optimize it
            // better in release builds.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     // search input word for [ B1 A3 A2 A1 ]
            //     return ((value & 0xF0000000U) == 0xE0000000U);
            // }
            // else
            // {
            //     // search input word for [ A1 A2 A3 B1 ]
            //     return ((value & 0xF0U) == 0xE0U);
            // }

            return (BitConverter.IsLittleEndian && ((value & 0xF0000000U) == 0xE0000000U))
                   || (!BitConverter.IsLittleEndian && ((value & 0xF0U) == 0xE0U));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Utf8DWordAllBytesAreAscii(uint value)
        {
            return ((value & 0x80808080U) == 0U);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Utf8DWordFirstByteIsAscii(uint value)
        {
            // The code in this method is equivalent to the code
            // below, but the JITter is able to inline + optimize it
            // better in release builds.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     return ((value & 0x80U) == 0U);
            // }
            // else
            // {
            //     return ((int)value >= 0);
            // }

            return (BitConverter.IsLittleEndian && ((value & 0x80U) == 0U))
                || (!BitConverter.IsLittleEndian && ((int)value >= 0));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Utf8DWordSecondByteIsAscii(uint value)
        {
            // The code in this method is equivalent to the code
            // below, but the JITter is able to inline + optimize it
            // better in release builds.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     return ((value & 0x8000U) == 0U);
            // }
            // else
            // {
            //     return ((value & 0x800000U) == 0U);
            // }

            return (BitConverter.IsLittleEndian && ((value & 0x8000U) == 0U))
                || (!BitConverter.IsLittleEndian && ((value & 0x800000U) == 0U));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Utf8DWordThirdByteIsAscii(uint value)
        {
            // The code in this method is equivalent to the code
            // below, but the JITter is able to inline + optimize it
            // better in release builds.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     return ((value & 0x800000U) == 0U);
            // }
            // else
            // {
            //     return ((value & 0x8000U) == 0U);
            // }

            return (BitConverter.IsLittleEndian && ((value & 0x800000U) == 0U))
                || (!BitConverter.IsLittleEndian && ((value & 0x8000U) == 0U));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Utf8DWordFourthByteIsAscii(uint value)
        {
            // The code in this method is equivalent to the code
            // below, but the JITter is able to inline + optimize it
            // better in release builds.
            //
            // if (BitConverter.IsLittleEndian)
            // {
            //     return ((int)value >= 0);
            // }
            // else
            // {
            //     return ((value & 0x80U) == 0U);
            // }

            return (BitConverter.IsLittleEndian && ((int)value >= 0))
                || (!BitConverter.IsLittleEndian && ((value & 0x80U) == 0U));
        }

        // Widens a 32-bit DWORD to a 64-bit QWORD by placing bytes into alternating slots.
        // [ AA BB CC DD ] -> [ 00 AA 00 BB 00 CC 00 DD ]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static ulong Widen(uint value)
        {
            if (Bmi2.IsSupported)
            {
                return Bmi2.ParallelBitDeposit((ulong)value, 0x00FF00FF00FF00FFUL);
            }
            else
            {
                ulong qWord = value;
                return ((qWord & 0xFF000000UL) << 24)
                    | ((qWord & 0xFF0000UL) << 16)
                    | (((value & 0xFF00U) << 8) | (byte)value);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWellFormedCharPackFromDoubleTwoByteSequences(uint value)
        {
            // Given a value [ AAAA BBBB ], ensures that both AAAA and BBBB are
            // at least U+0080. It's assumed that both AAAA and BBBB are < U+0800
            // since such a scalar can't be formed from a two-byte UTF8 sequence.

            // This method uses only arithmetic operations and bit manipulation
            // in order to avoid storing + loading flags between calls.

            uint a = value - 0x00800000U; // high bit will be set (underflow) if AAAA < 0x0080
            uint b = (value & 0xFFFFU) - 0x0080U; // high bit will be set (underflow) if BBBB < 0x0080
            return ((int)(a | b) >= 0); // if any high bit is set, underflow occurred
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWellFormedCharPackFromQuadTwoByteSequences(ulong value)
        {
            // Like IsWellFormedCharPackFromDoubleTwoByteSequences, but works with
            // 64-bit values of the form [ AAAA BBBB CCCC DDDD ].

            ulong a = value - 0x0080000000000000UL; // high bit will be set (underflow) if AAAA < 0x0080
            ulong b = (value & 0x0000FFFF00000000UL) - 0x0000008000000000U; // high bit will be set (underflow) if BBBB < 0x0080
            ulong c = (value & 0x00000000FFFF0000UL) - 0x0000000000800000U; // high bit will be set (underflow) if CCCC < 0x0080
            ulong d = (value & 0x000000000000FFFFUL) - 0x0000000000000080U; // high bit will be set (underflow) if DDDD < 0x0080
            return ((long)(a | b | c | d) >= 0L); // if any high bit is set, underflow occurred
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWellFormedCharPackFromDualThreeByteSequences(uint packedChars, ulong secondDWord)
        {
            if (!BitConverter.IsLittleEndian)
            {
                // TODO: SUPPORT BIG ENDIAN
                throw new NotImplementedException();
            }

            return (packedChars >= 0x08000000U) /* char 'B' is >= U+0800 */
                && ((packedChars & 0xF8000000U) != 0xD8000000U) /* char 'B' isn't a surrogate */
                && ((packedChars & 0x0000F800U) != 0U) /* char 'A' is >= U+0800 */
                && ((packedChars & 0x0000F800U) != 0x0000D800U) /* char 'A' isn't a surrogate */
                && ((secondDWord & 0x0000C0C0U) == 0x00008080U); /* secondDWord has correct masking */
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSurrogateFast(uint @char) => ((@char & 0xF800U) == 0xD800U);
    }
}
