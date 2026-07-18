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
using System.Text;
using DotNetFITS;

namespace DotNetFITSTests;

/// <summary>
/// Unit tests for <see cref="FITSBlock"/>.
/// </summary>
public class FITSBlockTests
{
    /// <summary>
    /// A block of ASCII bytes reports ASCII-only content; a block containing a
    /// non-ASCII byte does not.
    /// </summary>
    [ Fact ]
    public void ContainsOnlyASCIIReflectsBlockContents()
    {
        FITSBlock block1 = new FITSBlock( TestUtilities.DataBlock( 0x20 ), FITSParsingOptions.Strict );
        FITSBlock block2 = new FITSBlock( TestUtilities.DataBlock( 0xFF ), FITSParsingOptions.Strict );

        Assert.True( block1.ContainsOnlyASCII );
        Assert.False( block2.ContainsOnlyASCII );
    }

    /// <summary>
    /// A block reports an <c>END</c> marker when a record trims to exactly
    /// <c>END</c>. A leading space makes the keyword not <c>END</c>, and an
    /// <c>END</c> that is not the last non-blank record still counts: the first
    /// <c>END</c> terminates the section.
    /// </summary>
    [ Fact ]
    public void HasEndMarkerDetectsTheFirstEndRecord()
    {
        ReadOnlyMemory<byte> data1  = TestUtilities.HeaderBlock( [ "FOO     = 1", "BAR     = 1", "END        " ] );
        ReadOnlyMemory<byte> data2  = TestUtilities.HeaderBlock( [ "FOO     = 1", "BAR     = 1", " END       " ] );
        ReadOnlyMemory<byte> data3  = TestUtilities.HeaderBlock( [ "FOO     = 1", "END        ", "BAR     = 1" ] );
        FITSBlock            block1 = new FITSBlock( data1, FITSParsingOptions.Strict );
        FITSBlock            block2 = new FITSBlock( data2, FITSParsingOptions.Strict );
        FITSBlock            block3 = new FITSBlock( data3, FITSParsingOptions.Strict );

        Assert.True( block1.HasEndMarker );
        Assert.False( block2.HasEndMarker );
        Assert.True( block3.HasEndMarker );
    }

    /// <summary>
    /// A custom keyword that merely begins with <c>END</c> is not mistaken for the
    /// <c>END</c> marker.
    /// </summary>
    [ Fact ]
    public void HasEndMarkerMatchesExactlyNotByPrefix()
    {
        ReadOnlyMemory<byte> data1  = TestUtilities.HeaderBlock( [ "FOO     = 1", "ENDED   = 1" ] );
        ReadOnlyMemory<byte> data2  = TestUtilities.HeaderBlock( [ "FOO     = 1", "ENDTIME = 1" ] );
        FITSBlock            block1 = new FITSBlock( data1, FITSParsingOptions.Strict );
        FITSBlock            block2 = new FITSBlock( data2, FITSParsingOptions.Strict );

        Assert.False( block1.HasEndMarker );
        Assert.False( block2.HasEndMarker );
    }

    /// <summary>
    /// A NUL-padded <c>END</c> record is recognized as the marker only when NUL is
    /// allowed as padding. Under space-only padding <c>END\0…</c> does not trim to
    /// <c>END</c>, so the marker is missed;
    /// <see cref="FITSParsingOptions.AllowNulPadding"/> folds NUL into the padding
    /// so it is recognized.
    /// </summary>
    [ Fact ]
    public void HasEndMarkerTreatsNulAsPaddingOnlyWhenAllowed()
    {
        byte[] data = new byte[ FITSFile.BlockSize ];
        byte[] end  = Encoding.ASCII.GetBytes( "END" );

        Array.Fill( data, ( byte )0x20 );
        Encoding.ASCII.GetBytes( "FOO     = 1" ).CopyTo( data, 0 );
        end.CopyTo( data, FITSFile.CardSize );
        Array.Fill( data, ( byte )0x00, FITSFile.CardSize + end.Length, FITSFile.CardSize - end.Length );

        Assert.False( new FITSBlock( data, FITSParsingOptions.Strict ).HasEndMarker );
        Assert.True( new FITSBlock( data, FITSParsingOptions.AllowNulPadding ).HasEndMarker );
    }

    /// <summary>
    /// A block has an extension marker only when it is ASCII and its first record
    /// begins with the <c>XTENSION=</c> keyword. A missing <c>=</c>, a leading
    /// space, or the keyword on a later record are all rejected.
    /// </summary>
    [ Fact ]
    public void HasExtensionMarkerDetectsTheXtensionKeyword()
    {
        ReadOnlyMemory<byte> data1  = TestUtilities.HeaderBlock( [ "XTENSION  'TABLE    ' ", "FOO     = 1          ", "BAR     = 1" ] );
        ReadOnlyMemory<byte> data2  = TestUtilities.HeaderBlock( [ "XTENSION= 'TABLE    ' ", "FOO     = 1          ", "BAR     = 1" ] );
        ReadOnlyMemory<byte> data3  = TestUtilities.HeaderBlock( [ " XTENSION= 'TABLE    '", "FOO     = 1          ", "BAR     = 1" ] );
        ReadOnlyMemory<byte> data4  = TestUtilities.HeaderBlock( [ "FOO     = 1           ", "XTENSION= 'TABLE    '", "BAR     = 1" ] );
        FITSBlock            block1 = new FITSBlock( data1, FITSParsingOptions.Strict );
        FITSBlock            block2 = new FITSBlock( data2, FITSParsingOptions.Strict );
        FITSBlock            block3 = new FITSBlock( data3, FITSParsingOptions.Strict );
        FITSBlock            block4 = new FITSBlock( data4, FITSParsingOptions.Strict );

        Assert.False( block1.HasExtensionMarker );
        Assert.True( block2.HasExtensionMarker );
        Assert.False( block3.HasExtensionMarker );
        Assert.False( block4.HasExtensionMarker );
    }

    /// <summary>
    /// A standard extension header block that ends with an <c>END</c> record
    /// reports both an extension marker and an end marker.
    /// </summary>
    [ Fact ]
    public void HasEndMarkerAndExtensionMarkerCanBothBeTrue()
    {
        ReadOnlyMemory<byte> data  = TestUtilities.StandardExtensionBlock( includeEndMarker: true, keywords: [ ( "FOO", "1" ), ( "BAR", "1" ) ] );
        FITSBlock            block = new FITSBlock( data, FITSParsingOptions.Strict );

        Assert.True( block.HasEndMarker );
        Assert.True( block.HasExtensionMarker );
    }

    /// <summary>
    /// A block containing a non-ASCII byte is reported as binary, not ASCII-only.
    /// </summary>
    [ Fact ]
    public void BinaryBlockIsNotAscii()
    {
        byte[] data = TestUtilities.StandardHeaderBlock( includeEndMarker: false, keywords: [ ( "FOO", "1" ), ( "BAR", "1" ) ] ).ToArray();

        data[ FITSFile.BlockSize - 1 ] = 0xFF;

        FITSBlock block = new FITSBlock( data, FITSParsingOptions.Strict );

        Assert.False( block.ContainsOnlyASCII );
    }

    /// <summary>
    /// A binary block reports no <c>END</c> marker: the end scan is skipped for
    /// non-ASCII blocks.
    /// </summary>
    [ Fact ]
    public void BinaryBlockHasNoEndMarker()
    {
        byte[] data = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [ ( "FOO", "1" ), ( "BAR", "1" ) ] ).ToArray();

        data[ FITSFile.BlockSize - 1 ] = 0xFF;

        FITSBlock block = new FITSBlock( data, FITSParsingOptions.Strict );

        Assert.False( block.ContainsOnlyASCII );
        Assert.False( block.HasEndMarker );
    }

    /// <summary>
    /// A binary block reports no extension marker: the marker check requires an
    /// ASCII block.
    /// </summary>
    [ Fact ]
    public void BinaryBlockHasNoExtensionMarker()
    {
        byte[] data = TestUtilities.StandardExtensionBlock( includeEndMarker: true, keywords: [ ( "FOO", "1" ), ( "BAR", "1" ) ] ).ToArray();

        data[ FITSFile.BlockSize - 1 ] = 0xFF;

        FITSBlock block = new FITSBlock( data, FITSParsingOptions.Strict );

        Assert.False( block.ContainsOnlyASCII );
        Assert.False( block.HasExtensionMarker );
    }

    /// <summary>
    /// Constructing a block from empty data is rejected.
    /// </summary>
    [ Fact ]
    public void ConstructorRejectsEmptyData()
    {
        Assert.Throws<FITSException>( () => new FITSBlock( ReadOnlyMemory<byte>.Empty, FITSParsingOptions.Strict ) );
    }

    /// <summary>
    /// A wrong-sized buffer is rejected with
    /// <see cref="FITSErrorKind.InvalidBlockSize"/>, ahead of any byte-level scan
    /// of the block.
    /// </summary>
    [ Fact ]
    public void ConstructorRejectsWrongSizeWithInvalidBlockSize()
    {
        FITSException exception = Assert.Throws<FITSException>( () => new FITSBlock( new byte[ FITSFile.BlockSize + 1 ], FITSParsingOptions.Strict ) );

        Assert.Equal( FITSErrorKind.InvalidBlockSize, exception.Kind );
    }

    /// <summary>
    /// The textual summary is non-empty and differs from the default type-name
    /// representation, so it conveys the block's structural flags.
    /// </summary>
    [ Fact ]
    public void ToStringSummarizesTheStructuralFlags()
    {
        FITSBlock block = new FITSBlock( TestUtilities.DataBlock( 0x20 ), FITSParsingOptions.Strict );

        Assert.NotEmpty( block.ToString() );
        Assert.NotEqual( typeof( FITSBlock ).ToString(), block.ToString() );
    }
}
