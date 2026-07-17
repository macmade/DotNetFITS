/*******************************************************************************
 * The MIT License (MIT)
 *
 * Copyright (c) 2026, Jean-David Gadina - www.xs-labs.com
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the Software), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 ******************************************************************************/

using System;

namespace DotNetFITS;

/// <summary>
/// Internal <see cref="string"/> helpers shared across the FITS serialization
/// code.
/// </summary>
internal static class StringExtensions
{
    /// <summary>
    /// Returns a copy of <paramref name="value"/> adjusted to exactly
    /// <paramref name="length"/> characters: right-padded with
    /// <paramref name="padCharacter"/> when it is shorter, and truncated at the
    /// end when it is longer.
    /// </summary>
    /// <remarks>
    /// Mirrors Foundation's <c>String.padding(toLength:withPad:startingAt:)</c>,
    /// which both pads and truncates, for the single-character pad every FITS
    /// call site uses (Foundation's cyclic pad-string form is intentionally not
    /// reproduced). .NET's <see cref="string.PadRight(int, char)"/> only pads, so
    /// this helper adds the truncation needed wherever a FITS field must be
    /// brought to a fixed width - keyword fields, the <c>END</c> marker and card
    /// padding.
    /// </remarks>
    /// <param name="value">The string to pad or truncate.</param>
    /// <param name="length">The exact, non-negative length of the returned string.</param>
    /// <param name="padCharacter">The character used for right padding.</param>
    /// <returns>A string of exactly <paramref name="length"/> characters.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="length"/> is negative.
    /// </exception>
    internal static string PaddedOrTruncated( this string value, int length, char padCharacter = ' ' )
    {
        ArgumentOutOfRangeException.ThrowIfNegative( length );

        if( value.Length > length )
        {
            return value.Substring( 0, length );
        }

        return value.PadRight( length, padCharacter );
    }
}
