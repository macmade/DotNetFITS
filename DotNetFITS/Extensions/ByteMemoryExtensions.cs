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

namespace DotNetFITS;

/// <summary>
/// Byte-level helpers used to inspect and split FITS data.
/// </summary>
/// <remarks>
/// Extensions on <see cref="ReadOnlyMemory{Byte}"/>, a value type wrapping a
/// buffer, offset and length whose slicing is O(1) and shares storage, so FITS
/// data can be inspected and chunked without copying the bytes. The inspection
/// helpers read the memory through its <see cref="ReadOnlyMemory{T}.Span"/> for a
/// tight, allocation-free scan.
/// </remarks>
public static class ByteMemoryExtensions
{
    /// <summary>
    /// Returns whether every byte is a 7-bit ASCII value (in the range
    /// <c>0x00</c> to <c>0x7F</c>).
    /// </summary>
    /// <remarks>
    /// FITS headers and extensions are required to be ASCII, so this
    /// distinguishes header/extension blocks from binary data blocks. An empty
    /// span is vacuously ASCII.
    /// </remarks>
    /// <param name="data">The data to inspect.</param>
    /// <returns><c>true</c> if every byte is <c>0x7F</c> or below.</returns>
    public static bool ContainsOnlyASCII( this ReadOnlyMemory<byte> data ) => data.Span.IndexOfAnyExceptInRange( ( byte )0x00, ( byte )0x7F ) < 0;

    /// <summary>
    /// Returns whether every byte is blank padding: an ASCII space (<c>0x20</c>)
    /// or a NUL (<c>0x00</c>).
    /// </summary>
    /// <remarks>
    /// These are the fill bytes used to pad FITS blocks, so this recognizes
    /// trailing padding blocks. An empty span is vacuously blank.
    /// </remarks>
    /// <param name="data">The data to inspect.</param>
    /// <returns><c>true</c> if every byte is an ASCII space or NUL.</returns>
    public static bool IsBlank( this ReadOnlyMemory<byte> data ) => data.Span.IndexOfAnyExcept( ( byte )0x20, ( byte )0x00 ) < 0;

    /// <summary>
    /// Returns whether every byte is a printable FITS character (in the range
    /// <c>0x20</c> to <c>0x7E</c> inclusive).
    /// </summary>
    /// <remarks>
    /// This is the set of printable characters the FITS standard allows in header
    /// text. An empty span is vacuously printable.
    /// </remarks>
    /// <param name="data">The data to inspect.</param>
    /// <returns><c>true</c> if every byte is in the range <c>0x20</c> to <c>0x7E</c>.</returns>
    public static bool ContainsOnlyFITSPrintable( this ReadOnlyMemory<byte> data ) => data.Span.IndexOfAnyExceptInRange( ( byte )0x20, ( byte )0x7E ) < 0;

    /// <summary>
    /// Splits the data into consecutive, storage-sharing chunks of a fixed size.
    /// </summary>
    /// <param name="data">The data to split.</param>
    /// <param name="size">The size, in bytes, of each chunk. Must be positive.</param>
    /// <returns>
    /// The data split into contiguous slices of <paramref name="size"/> bytes
    /// each. Each slice re-bases to index zero and shares the original storage.
    /// </returns>
    /// <exception cref="FITSException">
    /// <paramref name="size"/> is not positive, or the length of
    /// <paramref name="data"/> is not an exact multiple of
    /// <paramref name="size"/>.
    /// </exception>
    public static IReadOnlyList<ReadOnlyMemory<byte>> Chunked( this ReadOnlyMemory<byte> data, int size )
    {
        if( size <= 0 )
        {
            throw FITSException.DataError( "Invalid chunk size" );
        }

        if( data.Length % size != 0 )
        {
            throw FITSException.DataError( "Data cannot be chunked evenly" );
        }

        return Enumerable.Range( 0, data.Length / size ).Select( index => data.Slice( index * size, size ) ).ToArray();
    }

    /// <summary>
    /// Returns whether the data begins with the given byte prefix.
    /// </summary>
    /// <remarks>
    /// Used by the block scanner to detect the <c>XTENSION=</c> marker. Returns
    /// <c>false</c> when <paramref name="prefix"/> is longer than the data.
    /// </remarks>
    /// <param name="data">The data to inspect.</param>
    /// <param name="prefix">The byte prefix to look for.</param>
    /// <returns><c>true</c> if <paramref name="data"/> starts with <paramref name="prefix"/>.</returns>
    public static bool StartsWith( this ReadOnlyMemory<byte> data, ReadOnlySpan<byte> prefix ) => data.Span.StartsWith( prefix );
}
