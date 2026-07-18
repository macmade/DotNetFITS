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

using DotNetFITS;

namespace DotNetFITSTests;

/// <summary>
/// Unit tests for the <see cref="FITSCharacterSet"/> membership predicates.
/// </summary>
/// <remarks>
/// Each test scans the whole <see cref="char"/> range and asserts the predicate
/// matches its expected membership, proving it holds exactly the expected
/// characters and no others.
/// </remarks>
public class FITSCharacterSetTests
{
    /// <summary>
    /// <see cref="FITSCharacterSet.IsPadding"/> matches the ASCII space and no
    /// other character.
    /// </summary>
    [ Fact ]
    public void IsPaddingMatchesOnlyTheSpaceCharacter()
    {
        for( int codeUnit = char.MinValue; codeUnit <= char.MaxValue; codeUnit++ )
        {
            char character = ( char )codeUnit;

            Assert.Equal( character == ' ', FITSCharacterSet.IsPadding( character ) );
        }
    }

    /// <summary>
    /// <see cref="FITSCharacterSet.IsPaddingWithNul"/> matches the ASCII space
    /// and the NUL byte, and no other character.
    /// </summary>
    [ Fact ]
    public void IsPaddingWithNulMatchesOnlySpaceAndNul()
    {
        for( int codeUnit = char.MinValue; codeUnit <= char.MaxValue; codeUnit++ )
        {
            char character = ( char )codeUnit;

            Assert.Equal( character == ' ' || character == '\0', FITSCharacterSet.IsPaddingWithNul( character ) );
        }
    }

    /// <summary>
    /// <see cref="FITSCharacterSet.IsKeyword"/> matches exactly the uppercase
    /// letters, digits, underscore and hyphen the FITS standard permits in a
    /// keyword name, and no other character.
    /// </summary>
    [ Fact ]
    public void IsKeywordMatchesExactlyTheAllowedCharacters()
    {
        const string allowed = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_-";

        for( int codeUnit = char.MinValue; codeUnit <= char.MaxValue; codeUnit++ )
        {
            char character = ( char )codeUnit;

            Assert.Equal( allowed.Contains( character ), FITSCharacterSet.IsKeyword( character ) );
        }
    }
}
