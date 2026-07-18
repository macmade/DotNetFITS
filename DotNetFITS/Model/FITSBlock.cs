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
using System.Linq;
using System.Text;

namespace DotNetFITS;

/// <summary>
/// A single fixed-size FITS block.
/// </summary>
/// <remarks>
/// A FITS file is a sequence of 2880-byte blocks (<see cref="FITSFile.BlockSize"/>).
/// This type wraps one such block and exposes the structural facts (ASCII-ness,
/// extension marker, end marker) the parser needs to group blocks into sections.
/// <para>
/// The structural flags are computed and cached on first access, so a block whose
/// role the header geometry already fixes (a data block) is never scanned. Because
/// that caching mutates on read, concurrent reads of the same block race the
/// caching: a <see cref="FITSBlock"/> is not thread-safe, and a fully-scanned block
/// is no safer to share than a fresh one.
/// </para>
/// </remarks>
public sealed class FITSBlock
{
    /// <summary>The raw 2880 bytes of the block.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>The parsing options applied when computing the structural flags.</summary>
    private FITSParsingOptions Options { get; }

    /// <summary>
    /// The cached <see cref="ContainsOnlyASCII"/> result, or <c>null</c> until it is
    /// first computed.
    /// </summary>
    private bool? CachedContainsOnlyASCII { get; set; }

    /// <summary>
    /// The cached <see cref="HasExtensionMarker"/> result, or <c>null</c> until it
    /// is first computed.
    /// </summary>
    private bool? CachedHasExtensionMarker { get; set; }

    /// <summary>
    /// The cached <see cref="HasEndMarker"/> result, or <c>null</c> until it is
    /// first computed.
    /// </summary>
    private bool? CachedHasEndMarker { get; set; }

    /// <summary>
    /// Creates a block from its raw bytes.
    /// </summary>
    /// <param name="data">
    /// The block's bytes. Must be exactly <see cref="FITSFile.BlockSize"/> (2880)
    /// bytes long.
    /// </param>
    /// <param name="options">
    /// The parsing options applied when computing the structural flags.
    /// </param>
    /// <exception cref="FITSException">
    /// <paramref name="data"/> is not exactly <see cref="FITSFile.BlockSize"/>
    /// bytes.
    /// </exception>
    public FITSBlock( ReadOnlyMemory<byte> data, FITSParsingOptions options )
    {
        if( data.Length != FITSFile.BlockSize )
        {
            throw FITSException.InvalidBlockSize( data.Length );
        }

        this.Data    = data;
        this.Options = options;
    }

    /// <summary>
    /// A value indicating whether the block contains only ASCII bytes.
    /// </summary>
    /// <remarks>
    /// Computed on first access, so data blocks the parser never inspects are not
    /// scanned. FITS headers and extensions are ASCII; a binary data block is not.
    /// </remarks>
    public bool ContainsOnlyASCII
    {
        get
        {
            this.CachedContainsOnlyASCII ??= this.Data.ContainsOnlyASCII();

            return this.CachedContainsOnlyASCII.Value;
        }
    }

    /// <summary>
    /// A value indicating whether the block begins a new extension.
    /// </summary>
    /// <remarks>
    /// <c>true</c> when the block is ASCII and starts with the <c>XTENSION=</c>
    /// keyword. Computed on first access.
    /// </remarks>
    public bool HasExtensionMarker
    {
        get
        {
            this.CachedHasExtensionMarker ??= this.ContainsOnlyASCII && this.Data.StartsWith( "XTENSION="u8 );

            return this.CachedHasExtensionMarker.Value;
        }
    }

    /// <summary>
    /// A value indicating whether the block contains an <c>END</c> marker record.
    /// </summary>
    /// <remarks>
    /// Computed on first access by scanning the block's 80-byte records. Always
    /// <c>false</c> for a non-ASCII (data) block. The first <c>END</c> record
    /// terminates a header or extension section, matching how the section locates
    /// <c>END</c>, so a block whose <c>END</c> is not its last non-blank record
    /// still ends the section.
    /// </remarks>
    public bool HasEndMarker
    {
        get
        {
            this.CachedHasEndMarker ??= this.ComputeHasEndMarker();

            return this.CachedHasEndMarker.Value;
        }
    }

    /// <summary>Returns a textual summary of the block's structural flags.</summary>
    /// <returns>A summary string listing the three structural flags.</returns>
    public override string ToString() => $"FITSBlock {{ ContainsOnlyASCII: {this.ContainsOnlyASCII}, HasEndMarker: {this.HasEndMarker}, HasExtensionMarker: {this.HasExtensionMarker} }}";

    /// <summary>
    /// Scans the block's records for an <c>END</c> marker.
    /// </summary>
    /// <remarks>
    /// Returns <c>false</c> without scanning for a non-ASCII block. Otherwise each
    /// 80-byte record is right-trimmed of its padding - the ASCII space, plus NUL
    /// when <see cref="FITSParsingOptions.AllowNulPadding"/> is set - and the first
    /// record trimming to exactly <c>END</c> matches. The 2880-byte block size
    /// enforced at construction is an exact multiple of the card size, so the record
    /// split never fails.
    /// </remarks>
    /// <returns><c>true</c> if any record is an <c>END</c> marker.</returns>
    private bool ComputeHasEndMarker()
    {
        if( this.ContainsOnlyASCII == false )
        {
            return false;
        }

        Func<char, bool> isPadding = this.Options.HasFlag( FITSParsingOptions.AllowNulPadding ) ? FITSCharacterSet.IsPaddingWithNul : FITSCharacterSet.IsPadding;

        return this.Data.Chunked( FITSFile.CardSize ).Any( record => Encoding.ASCII.GetString( record.Span ).RightTrimming( isPadding ) == "END" );
    }
}
