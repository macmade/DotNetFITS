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
using DotNetFITS;

namespace DotNetFITSTests;

/// <summary>
/// Unit tests for the <see cref="StringExtensions"/> helpers.
/// </summary>
public class StringExtensionsTests
{
    /// <summary>
    /// A string shorter than the requested length is right-padded with spaces
    /// to reach exactly that length.
    /// </summary>
    [ Fact ]
    public void PaddedOrTruncatedPadsShortStrings()
    {
        Assert.Equal( "AB   ", "AB".PaddedOrTruncated( 5 ) );
    }

    /// <summary>
    /// A string longer than the requested length is truncated at the end to
    /// exactly that length.
    /// </summary>
    [ Fact ]
    public void PaddedOrTruncatedTruncatesLongStrings()
    {
        Assert.Equal( "ABC", "ABCDEF".PaddedOrTruncated( 3 ) );
    }

    /// <summary>
    /// A string already at the requested length is returned unchanged.
    /// </summary>
    [ Fact ]
    public void PaddedOrTruncatedLeavesExactLengthUnchanged()
    {
        Assert.Equal( "ABC", "ABC".PaddedOrTruncated( 3 ) );
    }

    /// <summary>
    /// Padding uses the supplied pad character instead of the default space.
    /// </summary>
    [ Fact ]
    public void PaddedOrTruncatedPadsWithCustomCharacter()
    {
        Assert.Equal( "AB000", "AB".PaddedOrTruncated( 5, '0' ) );
    }

    /// <summary>
    /// A requested length of zero yields an empty string.
    /// </summary>
    [ Fact ]
    public void PaddedOrTruncatedToZeroLengthYieldsEmptyString()
    {
        Assert.Equal( "", "ABC".PaddedOrTruncated( 0 ) );
    }

    /// <summary>
    /// An empty string is padded up to the requested length.
    /// </summary>
    [ Fact ]
    public void PaddedOrTruncatedPadsEmptyString()
    {
        Assert.Equal( "   ", "".PaddedOrTruncated( 3 ) );
    }

    /// <summary>
    /// A negative length is rejected with an <see cref="ArgumentOutOfRangeException"/>
    /// that names the <c>length</c> parameter, rather than leaking an opaque exception.
    /// </summary>
    [ Fact ]
    public void PaddedOrTruncatedRejectsNegativeLength()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>( () => "ABC".PaddedOrTruncated( -1 ) );

        Assert.Equal( "length", exception.ParamName );
    }

    /// <summary>
    /// <see cref="StringExtensions.LeftTrimming"/> removes every leading
    /// character matching the predicate, stops at the first that does not, and
    /// yields an empty string when they all match.
    /// </summary>
    [ Fact ]
    public void LeftTrimmingRemovesLeadingMatchingCharacters()
    {
        Assert.Equal( "hello, world", "    hello, world".LeftTrimming( character => character == ' ' ) );
        Assert.Equal( "hello, world", "!!!!hello, world".LeftTrimming( character => character == '!' ) );
        Assert.Equal( "hello, world", "!!  hello, world".LeftTrimming( character => character == ' ' || character == '!' ) );
        Assert.Equal( "hello, world", "hello, world"    .LeftTrimming( character => character == ' ' ) );
        Assert.Equal( "",             "    "            .LeftTrimming( character => character == ' ' ) );
        Assert.Equal( "",             ""                .LeftTrimming( character => character == ' ' ) );
    }

    /// <summary>
    /// <see cref="StringExtensions.RightTrimming"/> removes every trailing
    /// character matching the predicate, stops at the first that does not, and
    /// yields an empty string when they all match.
    /// </summary>
    [ Fact ]
    public void RightTrimmingRemovesTrailingMatchingCharacters()
    {
        Assert.Equal( "hello, world", "hello, world    ".RightTrimming( character => character == ' ' ) );
        Assert.Equal( "hello, world", "hello, world!!!!".RightTrimming( character => character == '!' ) );
        Assert.Equal( "hello, world", "hello, world!!  ".RightTrimming( character => character == ' ' || character == '!' ) );
        Assert.Equal( "hello, world", "hello, world"    .RightTrimming( character => character == ' ' ) );
        Assert.Equal( "",             "    "            .RightTrimming( character => character == ' ' ) );
        Assert.Equal( "",             ""                .RightTrimming( character => character == ' ' ) );
    }
}
