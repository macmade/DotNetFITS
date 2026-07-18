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

namespace DotNetFITS;

/// <summary>
/// FITS-specific character-membership predicates used throughout parsing.
/// </summary>
/// <remarks>
/// The FITS padding, padding-with-NUL and keyword character sets, each expressed
/// as a <see cref="char"/> membership predicate (.NET has no character-set type).
/// The predicates are exposed as static methods so they convert to
/// <see cref="System.Func{T, TResult}"/> method groups for the trimming helpers
/// in <see cref="StringExtensions"/> and compose with <c>string</c> / <c>char</c>
/// sequence checks.
/// </remarks>
public static class FITSCharacterSet
{
    /// <summary>
    /// The characters permitted in a FITS keyword name: uppercase letters,
    /// digits, the underscore and the hyphen, per the FITS standard.
    /// </summary>
    private const string KeywordCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_-";

    /// <summary>
    /// Returns whether <paramref name="character"/> is the FITS padding
    /// character, the ASCII space (<c>0x20</c>).
    /// </summary>
    /// <remarks>
    /// FITS fixes every record at 80 bytes and pads unused space with the ASCII
    /// space. This predicate is used to trim that padding from keyword names,
    /// values and comments.
    /// </remarks>
    /// <param name="character">The character to test.</param>
    /// <returns><c>true</c> if <paramref name="character"/> is the ASCII space.</returns>
    public static bool IsPadding( char character ) => character == ' ';

    /// <summary>
    /// Returns whether <paramref name="character"/> is a padding character
    /// extended with the NUL byte: the ASCII space (<c>0x20</c>) or NUL
    /// (<c>0x00</c>).
    /// </summary>
    /// <remarks>
    /// Used when NUL padding is allowed to trim NUL-padded or NUL-terminated
    /// keywords and <c>END</c> markers.
    /// </remarks>
    /// <param name="character">The character to test.</param>
    /// <returns>
    /// <c>true</c> if <paramref name="character"/> is the ASCII space or NUL.
    /// </returns>
    public static bool IsPaddingWithNul( char character ) => character == ' ' || character == '\0';

    /// <summary>
    /// Returns whether <paramref name="character"/> is permitted in a FITS
    /// keyword name.
    /// </summary>
    /// <remarks>
    /// Per the FITS standard a keyword name may contain only uppercase letters,
    /// digits, the underscore and the hyphen.
    /// </remarks>
    /// <param name="character">The character to test.</param>
    /// <returns>
    /// <c>true</c> if <paramref name="character"/> is a valid keyword character.
    /// </returns>
    public static bool IsKeyword( char character ) => KeywordCharacters.Contains( character );
}
