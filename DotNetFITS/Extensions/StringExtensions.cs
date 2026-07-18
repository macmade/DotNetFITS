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
/// <see cref="string"/> helpers shared across the FITS code: one-sided trimming
/// used when parsing space-padded records, and a pad-or-truncate used when
/// rendering fixed-width fields.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Returns a copy of <paramref name="value"/> with every leading character
    /// for which <paramref name="predicate"/> returns <c>true</c> removed.
    /// </summary>
    /// <remarks>
    /// The one-sided counterpart of <see cref="RightTrimming"/>, used when
    /// parsing space-padded FITS records. Ports the Swift
    /// <c>leftTrimmingCharacters(in:)</c>, taking a <see cref="char"/> predicate
    /// in place of a Foundation <c>CharacterSet</c>. Returns an empty string when
    /// every character satisfies <paramref name="predicate"/>.
    /// </remarks>
    /// <param name="value">The string to trim.</param>
    /// <param name="predicate">The predicate selecting characters to remove.</param>
    /// <returns>
    /// <paramref name="value"/> without its leading matching characters.
    /// </returns>
    public static string LeftTrimming( this string value, Func<char, bool> predicate )
    {
        int start = 0;

        while( start < value.Length && predicate( value[ start ] ) )
        {
            start += 1;
        }

        return value.Substring( start );
    }

    /// <summary>
    /// Returns a copy of <paramref name="value"/> with every trailing character
    /// for which <paramref name="predicate"/> returns <c>true</c> removed.
    /// </summary>
    /// <remarks>
    /// The one-sided counterpart of <see cref="LeftTrimming"/>, used when parsing
    /// space-padded FITS records. Ports the Swift
    /// <c>rightTrimmingCharacters(in:)</c>, taking a <see cref="char"/> predicate
    /// in place of a Foundation <c>CharacterSet</c>. Returns an empty string when
    /// every character satisfies <paramref name="predicate"/>.
    /// </remarks>
    /// <param name="value">The string to trim.</param>
    /// <param name="predicate">The predicate selecting characters to remove.</param>
    /// <returns>
    /// <paramref name="value"/> without its trailing matching characters.
    /// </returns>
    public static string RightTrimming( this string value, Func<char, bool> predicate )
    {
        int end = value.Length;

        while( end > 0 && predicate( value[ end - 1 ] ) )
        {
            end -= 1;
        }

        return value.Substring( 0, end );
    }

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
    public static string PaddedOrTruncated( this string value, int length, char padCharacter = ' ' )
    {
        ArgumentOutOfRangeException.ThrowIfNegative( length );

        if( value.Length > length )
        {
            return value.Substring( 0, length );
        }

        return value.PadRight( length, padCharacter );
    }
}
