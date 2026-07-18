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
using System.Collections.Generic;
using System.Linq;
using DotNetFITS;

namespace DotNetFITSTests;

/// <summary>
/// Unit tests for the <see cref="ByteMemoryExtensions"/> helpers.
/// </summary>
/// <remarks>
/// Exercises the <see cref="ByteMemoryExtensions"/> helpers on
/// <see cref="ReadOnlyMemory{ Byte }"/>, including guard tests for <c>IsBlank</c>,
/// <c>ContainsOnlyFITSPrintable</c> and <c>StartsWith</c>.
/// </remarks>
public class ByteMemoryExtensionsTests
{
    /// <summary>
    /// <see cref="ByteMemoryExtensions.ContainsOnlyASCII"/> is <c>true</c> when
    /// every byte is <c>0x7F</c> or below, and <c>false</c> once a byte exceeds it.
    /// </summary>
    [ Fact ]
    public void ContainsOnlyASCIIIsTrueOnlyForBytesUpTo0x7F()
    {
        ReadOnlyMemory< byte > ascii  = Enumerable.Range( 0x00, 0x80 ).Select( value => ( byte )value ).ToArray();
        ReadOnlyMemory< byte > binary = Enumerable.Range( 0x00, 0x100 ).Select( value => ( byte )value ).ToArray();

        Assert.True( ascii.ContainsOnlyASCII() );
        Assert.False( binary.ContainsOnlyASCII() );
    }

    /// <summary>
    /// <see cref="ByteMemoryExtensions.IsBlank"/> is <c>true</c> only when every
    /// byte is an ASCII space or NUL.
    /// </summary>
    [ Fact ]
    public void IsBlankIsTrueOnlyForSpaceAndNulBytes()
    {
        ReadOnlyMemory< byte > spaces = Enumerable.Repeat( ( byte )0x20, 16 ).ToArray();
        ReadOnlyMemory< byte > nuls   = Enumerable.Repeat( ( byte )0x00, 16 ).ToArray();
        ReadOnlyMemory< byte > mixed  = new byte[] { 0x20, 0x00, 0x20, 0x00 };
        ReadOnlyMemory< byte > text   = new byte[] { 0x20, 0x41, 0x00 };

        Assert.True( spaces.IsBlank() );
        Assert.True( nuls.IsBlank() );
        Assert.True( mixed.IsBlank() );
        Assert.False( text.IsBlank() );
    }

    /// <summary>
    /// <see cref="ByteMemoryExtensions.ContainsOnlyFITSPrintable"/> is <c>true</c>
    /// only when every byte is a printable FITS character (<c>0x20</c> to
    /// <c>0x7E</c> inclusive).
    /// </summary>
    [ Fact ]
    public void ContainsOnlyFITSPrintableIsTrueOnlyForBytes0x20To0x7E()
    {
        ReadOnlyMemory< byte > printable   = Enumerable.Range( 0x20, ( 0x7E - 0x20 ) + 1 ).Select( value => ( byte )value ).ToArray();
        ReadOnlyMemory< byte > withControl = new byte[] { 0x20, 0x1F, 0x21 };
        ReadOnlyMemory< byte > withDelete  = new byte[] { 0x20, 0x7F, 0x21 };

        Assert.True( printable.ContainsOnlyFITSPrintable() );
        Assert.False( withControl.ContainsOnlyFITSPrintable() );
        Assert.False( withDelete.ContainsOnlyFITSPrintable() );
    }

    /// <summary>
    /// <see cref="ByteMemoryExtensions.Chunked"/> splits data whose length is an
    /// exact multiple of the chunk size into that many equal-sized chunks.
    /// </summary>
    [ Fact ]
    public void ChunkedSplitsIntoEqualSizedChunks()
    {
        ReadOnlyMemory< byte > data = Enumerable.Range( 0x00, 0x100 ).Select( value => ( byte )value ).ToArray();

        Assert.Equal( 256, data.Chunked(   1 ).Count );
        Assert.Equal( 128, data.Chunked(   2 ).Count );
        Assert.Equal(  64, data.Chunked(   4 ).Count );
        Assert.Equal(  32, data.Chunked(   8 ).Count );
        Assert.Equal(  16, data.Chunked(  16 ).Count );
        Assert.Equal(   8, data.Chunked(  32 ).Count );
        Assert.Equal(   4, data.Chunked(  64 ).Count );
        Assert.Equal(   2, data.Chunked( 128 ).Count );
        Assert.Single( data.Chunked( 256 ) );
    }

    /// <summary>
    /// <see cref="ByteMemoryExtensions.Chunked"/> throws a
    /// <see cref="FITSException"/> for a non-positive chunk size or a length that
    /// is not an exact multiple of the size.
    /// </summary>
    [ Fact ]
    public void ChunkedThrowsForNonPositiveSizeOrUnevenLength()
    {
        ReadOnlyMemory< byte > data = Enumerable.Range( 0x00, 0x100 ).Select( value => ( byte )value ).ToArray();

        Assert.Throws< FITSException >( () => data.Chunked( 0 ) );
        Assert.Throws< FITSException >( () => data.Chunked( 3 ) );
    }

    /// <summary>
    /// <see cref="ByteMemoryExtensions.Chunked"/> chunks a sub-slice against its
    /// own offsets, so a slice that does not start at index zero yields the
    /// correct bytes.
    /// </summary>
    /// <remarks>
    /// Because <see cref="ReadOnlyMemory{ T }.Slice(int, int)"/> re-bases a slice to
    /// index zero, this pins that the chunk offset arithmetic stays correct for a
    /// slice that does not start at the buffer's beginning.
    /// </remarks>
    [ Fact ]
    public void ChunkedHandlesASubSliceIndependentlyOfItsOffset()
    {
        ReadOnlyMemory< byte > full  = Enumerable.Range( 0, 160 ).Select( value => ( byte )value ).ToArray();
        ReadOnlyMemory< byte > slice = full.Slice( 80, 80 );

        IReadOnlyList< ReadOnlyMemory< byte > > chunks = slice.Chunked( 40 );

        Assert.Equal( 2, chunks.Count );
        Assert.Equal( Enumerable.Range(  80, 40 ).Select( value => ( byte )value ).ToArray(), chunks[ 0 ].ToArray() );
        Assert.Equal( Enumerable.Range( 120, 40 ).Select( value => ( byte )value ).ToArray(), chunks[ 1 ].ToArray() );
    }

    /// <summary>
    /// <see cref="ByteMemoryExtensions.StartsWith"/> reports whether the data
    /// begins with a given byte prefix, including <c>false</c> when the prefix is
    /// longer than the data.
    /// </summary>
    [ Fact ]
    public void StartsWithMatchesABytePrefix()
    {
        ReadOnlyMemory< byte > data      = "XTENSION= 'TABLE    '"u8.ToArray();
        ReadOnlyMemory< byte > shortData = "XT"u8.ToArray();

        Assert.True( data.StartsWith( "XTENSION="u8 ) );
        Assert.False( data.StartsWith( "SIMPLE  ="u8 ) );
        Assert.False( shortData.StartsWith( "XTENSION="u8 ) );
    }
}
