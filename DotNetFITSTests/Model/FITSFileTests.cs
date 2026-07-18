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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DotNetFITS;

namespace DotNetFITSTests;

/// <summary>
/// Unit tests for <see cref="FITSFile"/>.
/// </summary>
/// <remarks>
/// Exercises both the read path - constructing a file from bytes or a file path,
/// splitting the blocks into sections by geometry, and validating the mandatory
/// keywords and data-segment sizes - and the write path: building files and
/// extensions from scratch, editing HDUs, and validated serialization to bytes or
/// disk, including the round-trips through the serializer.
/// </remarks>
public class FITSFileTests
{
    /// <summary>
    /// The fixed FITS size constants carry the values mandated by the standard:
    /// a 2880-byte block, an 80-byte card, an 8-byte keyword field and a
    /// 20-byte fixed-format value field.
    /// </summary>
    [ Fact ]
    public void SizeConstantsMatchTheFITSStandard()
    {
        Assert.Equal( 2880, FITSFile.BlockSize );
        Assert.Equal( 80,   FITSFile.CardSize );
        Assert.Equal( 8,    FITSFile.KeywordLength );
        Assert.Equal( 20,   FITSFile.FixedValueFieldWidth );
    }

    /// <summary>
    /// The maximum data-segment size is the 2^53 ceiling used to reject corrupt
    /// geometries before the size arithmetic can overflow.
    /// </summary>
    [ Fact ]
    public void MaxDataSizeIsTwoToThePowerOfFiftyThree()
    {
        Assert.Equal( 1L << 53, FITSFile.MaxDataSize );
    }

    /// <summary>
    /// Every sample file under the repository's <c>Test Files</c> directory parses
    /// under lenient options.
    /// </summary>
    [ Fact ]
    public void ParseAllTestFiles()
    {
        Assert.NotEmpty( TestUtilities.TestFiles );

        foreach( string path in TestUtilities.TestFiles )
        {
            _ = new FITSFile( path, FITSParsingOptions.Lenient );
        }
    }

    /// <summary>
    /// A clean file serialized with the strict options reproduces its original
    /// bytes exactly for every sample file: geometry-driven parsing assigns each
    /// block to exactly one section in file order, so re-serializing must reproduce
    /// the input.
    /// </summary>
    [ Fact ]
    public void AllTestFilesRoundTrip()
    {
        Assert.NotEmpty( TestUtilities.TestFiles );

        foreach( string path in TestUtilities.TestFiles )
        {
            byte[]   data = File.ReadAllBytes( path );
            FITSFile file = new FITSFile( data, FITSParsingOptions.Lenient );

            Assert.True( file.Data.Span.SequenceEqual( data ), $"Round-trip mismatch for { Path.GetFileName( path ) }" );
        }
    }

    /// <summary>
    /// A missing file path is rejected.
    /// </summary>
    [ Fact ]
    public void InvalidURL()
    {
        Assert.Throws< FITSException >( () => new FITSFile( "/foo/bar.fits", FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// An empty file is rejected (its empty contents fail the empty-data guard).
    /// </summary>
    [ Fact ]
    public void EmptyFile()
    {
        string path = TempFitsPath();

        File.WriteAllBytes( path, [] );

        try
        {
            Assert.Throws< FITSException >( () => new FITSFile( path, FITSParsingOptions.Lenient ) );
        }
        finally
        {
            File.Delete( path );
        }
    }

    /// <summary>
    /// A readable but empty file is rejected by the empty-data guard.
    /// </summary>
    [ Fact ]
    public void UnreadableFile()
    {
        string path = TempFitsPath();

        File.WriteAllBytes( path, [] );

        try
        {
            Assert.Throws< FITSException >( () => new FITSFile( path, FITSParsingOptions.Lenient ) );
        }
        finally
        {
            File.Delete( path );
        }
    }

    /// <summary>
    /// Empty data is rejected.
    /// </summary>
    [ Fact ]
    public void EmptyData()
    {
        Assert.Throws< FITSException >( () => new FITSFile( ReadOnlyMemory< byte >.Empty, FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// A missing file is rejected with <see cref="FITSErrorKind.InvalidFileURL"/>.
    /// </summary>
    [ Fact ]
    public void MissingFileThrowsInvalidFileURL()
    {
        FITSException exception = Assert.Throws< FITSException >( () => new FITSFile( "/no/such/file.fits", FITSParsingOptions.Lenient ) );

        Assert.Equal( FITSErrorKind.InvalidFileURL, exception.Kind );
    }

    /// <summary>
    /// A directory path is rejected with <see cref="FITSErrorKind.InvalidFileURL"/>.
    /// </summary>
    [ Fact ]
    public void DirectoryThrowsInvalidFileURL()
    {
        FITSException exception = Assert.Throws< FITSException >( () => new FITSFile( Path.GetTempPath(), FITSParsingOptions.Lenient ) );

        Assert.Equal( FITSErrorKind.InvalidFileURL, exception.Kind );
    }

    /// <summary>
    /// A readable-but-unreadable existing file is rejected with
    /// <see cref="FITSErrorKind.CannotReadFile"/>.
    /// </summary>
    /// <remarks>
    /// The unreadable state is set up with POSIX permissions, so the assertion
    /// runs only on platforms where those apply and only for an unprivileged user
    /// (a privileged user can read a permission-less file, as the read
    /// classification intends).
    /// </remarks>
    [ Fact ]
    public void UnreadableFileThrowsCannotReadFile()
    {
        if( OperatingSystem.IsWindows() )
        {
            return;
        }

        string path = TempFitsPath();

        File.WriteAllBytes( path, [ 0x00 ] );

        try
        {
            File.SetUnixFileMode( path, UnixFileMode.None );

            if( IsReadable( path ) )
            {
                return;
            }

            FITSException exception = Assert.Throws< FITSException >( () => new FITSFile( path, FITSParsingOptions.Lenient ) );

            Assert.Equal( FITSErrorKind.CannotReadFile, exception.Kind );
        }
        finally
        {
            TestUtilities.RemoveTemporaryFile( path );
        }
    }

    /// <summary>
    /// A parsed file serialized to bytes and reparsed yields an identical
    /// serialization and textual summary.
    /// </summary>
    [ Fact ]
    public void Data()
    {
        string? path = TestUtilities.TestFiles.FirstOrDefault( candidate => Path.GetFileName( candidate ) == "FOSy19g0309t_c2f.fits" );

        Assert.NotNull( path );

        FITSFile file = new FITSFile( path, FITSParsingOptions.Lenient );
        FITSFile copy = new FITSFile( file.Data, FITSParsingOptions.Lenient );

        Assert.Equal( file.Data.ToArray(), copy.Data.ToArray() );
        Assert.Equal( file.ToString(),     copy.ToString() );
    }

    /// <summary>
    /// The textual summary is non-empty and differs from the default type-name
    /// representation.
    /// </summary>
    [ Fact ]
    public void Description()
    {
        FITSFile file = new FITSFile( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] ), FITSParsingOptions.Lenient );

        Assert.NotEmpty( file.ToString() );
        Assert.NotEqual( typeof( FITSFile ).ToString(), file.ToString() );
    }

    /// <summary>
    /// A header that legitimately spans two blocks - its <c>END</c> marker in the
    /// second block - is read as a single section.
    /// </summary>
    [ Fact ]
    public void EndMarkerInSecondHeaderBlock()
    {
        ReadOnlyMemory< byte > block1 = TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "0" ), ( "FOO", "1" ) ] );
        ReadOnlyMemory< byte > block2 = TestUtilities.HeaderBlock( [ ( "BAR", "1" ), ( "END", "" ) ] );
        FITSFile               file   = new FITSFile( Combine( block1, block2 ), FITSParsingOptions.Lenient );

        Assert.Single( file.Sections );
    }

    /// <summary>
    /// A custom keyword beginning with <c>END</c> as the first block's last
    /// non-blank record does not end the header early; the header spans both
    /// blocks up to the real <c>END</c> marker.
    /// </summary>
    [ Fact ]
    public void EndPrefixKeywordDoesNotEndHeaderEarly()
    {
        ReadOnlyMemory< byte > block1 = TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "0" ), ( "ENDED", "1" ) ] );
        ReadOnlyMemory< byte > block2 = TestUtilities.HeaderBlock( [ ( "FOO", "1" ), ( "END", "" ) ] );
        FITSFile               file   = new FITSFile( Combine( block1, block2 ), FITSParsingOptions.Lenient );

        Assert.Single( file.Sections );
    }

    /// <summary>
    /// A duplicated geometry keyword resolves first-wins, so a header with two
    /// <c>NAXIS1</c> records (implying one data block, then two) parses with a
    /// single data block under strict parsing.
    /// </summary>
    [ Fact ]
    public void DuplicateGeometryKeywordResolvesFirstWins()
    {
        ReadOnlyMemory< byte > header = TestUtilities.HeaderBlock(
            [
                ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "1" ),
                ( "NAXIS1", "2880" ), ( "NAXIS1", "5760" ), ( "END", "" ),
            ]
        );
        FITSFile file = new FITSFile( Combine( header, TestUtilities.DataBlock( 0xFF ) ), FITSParsingOptions.Strict );

        Assert.Equal( 2, file.Sections.Count );
        Assert.Equal( FITSFile.BlockSize, file.Sections[ 1 ].DataSize );
    }

    /// <summary>
    /// A file whose first block is an extension is rejected: the first section is
    /// validated as a primary header and its missing <c>SIMPLE</c> keyword fails.
    /// </summary>
    [ Fact ]
    public void NoHeader()
    {
        Assert.Throws< FITSException >( () => new FITSFile( TestUtilities.StandardExtensionBlock( includeEndMarker: true, keywords: [] ), FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// A primary header missing <c>SIMPLE</c> is rejected.
    /// </summary>
    [ Fact ]
    public void NoSimpleProperty()
    {
        Assert.Throws< FITSException >( () => new FITSFile( TestUtilities.HeaderBlock( [ ( "BITPIX", "8" ), ( "END", "" ) ] ), FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// A <c>SIMPLE</c> that is not logical-<c>true</c> is rejected.
    /// </summary>
    [ Fact ]
    public void InvalidSimpleProperty()
    {
        Assert.Throws< FITSException >( () => new FITSFile( TestUtilities.HeaderBlock( [ ( "SIMPLE", "0" ), ( "END", "" ) ] ), FITSParsingOptions.Lenient ) );
        Assert.Throws< FITSException >( () => new FITSFile( TestUtilities.HeaderBlock( [ ( "SIMPLE", "F" ), ( "END", "" ) ] ), FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// A primary header missing <c>BITPIX</c> is rejected.
    /// </summary>
    [ Fact ]
    public void NoBitpixProperty()
    {
        Assert.Throws< FITSException >( () => new FITSFile( TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "END", "" ) ] ), FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// A <c>BITPIX</c> of the wrong type or an unsupported value is rejected.
    /// </summary>
    [ Fact ]
    public void InvalidBitpixProperty()
    {
        Assert.Throws< FITSException >( () => new FITSFile( TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "T" ), ( "END", "" ) ] ), FITSParsingOptions.Lenient ) );
        Assert.Throws< FITSException >( () => new FITSFile( TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "0" ), ( "END", "" ) ] ), FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// Each of the six standard <c>BITPIX</c> values is accepted and parsed as an
    /// integer.
    /// </summary>
    [ Fact ]
    public void ValidBitpixProperties()
    {
        foreach( long value in new long[] { 8, 16, 32, 64, -32, -64 } )
        {
            FITSFile     file   = new FITSFile( TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", value.ToString( CultureInfo.InvariantCulture ) ), ( "NAXIS", "0" ), ( "END", "" ) ] ), FITSParsingOptions.Lenient );
            FITSSection? header = file.Header;

            Assert.NotNull( header );
            Assert.Equal( "BITPIX",              header.Properties[ 1 ].Name );
            Assert.Equal( FITSValueKind.Integer, header.Properties[ 1 ].Value.Kind );
            Assert.Equal( value,                 header.Properties[ 1 ].Value.AsInteger );
        }
    }

    /// <summary>
    /// A primary header missing <c>NAXIS</c> is rejected.
    /// </summary>
    [ Fact ]
    public void NoNaxisProperty()
    {
        Assert.Throws< FITSException >( () => new FITSFile( TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "END", "" ) ] ), FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// A <c>NAXIS</c> of the wrong type, requiring an absent axis, or negative is
    /// rejected.
    /// </summary>
    [ Fact ]
    public void InvalidNaxisProperty()
    {
        Assert.Throws< FITSException >( () => new FITSFile( TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "T " ), ( "END", "" ) ] ), FITSParsingOptions.Lenient ) );
        Assert.Throws< FITSException >( () => new FITSFile( TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "1 " ), ( "END", "" ) ] ), FITSParsingOptions.Lenient ) );
        Assert.Throws< FITSException >( () => new FITSFile( TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "-1" ), ( "END", "" ) ] ), FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// A <c>NAXIS</c> above the FITS maximum of 999 is rejected with the specific
    /// out-of-range diagnostic.
    /// </summary>
    [ Fact ]
    public void NaxisAboveMaximumIsRejected()
    {
        FITSException exception = Assert.Throws< FITSException >( () => new FITSFile( TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "1000" ), ( "END", "" ) ] ), FITSParsingOptions.Lenient ) );

        Assert.Contains( "NAXIS",        exception.Message );
        Assert.Contains( "out of range", exception.Message );
    }

    /// <summary>
    /// A <c>NAXISn</c> of the wrong type or negative is rejected.
    /// </summary>
    [ Fact ]
    public void InvalidNaxisNProperty()
    {
        Assert.Throws< FITSException >( () => new FITSFile( TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "1" ), ( "NAXIS1", "T " ), ( "END", "" ) ] ), FITSParsingOptions.Lenient ) );
        Assert.Throws< FITSException >( () => new FITSFile( TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "1" ), ( "NAXIS1", "-1" ), ( "END", "" ) ] ), FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// The primary header is exposed with its parsed properties, in order.
    /// </summary>
    [ Fact ]
    public void Header()
    {
        FITSFile     file   = new FITSFile( TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "0" ), ( "FOOBAR", "42" ), ( "END", "" ) ] ), FITSParsingOptions.Lenient );
        FITSSection? header = file.Header;

        Assert.NotNull( header );
        Assert.Equal( FITSSection.Kind.Header, header.SectionKind );
        Assert.Equal( 4, header.Properties.Count );

        Assert.Equal( "SIMPLE", header.Properties[ 0 ].Name );
        Assert.Equal( FITSValueKind.Logical, header.Properties[ 0 ].Value.Kind );
        Assert.Equal( true, header.Properties[ 0 ].Value.AsLogical );

        Assert.Equal( "BITPIX", header.Properties[ 1 ].Name );
        Assert.Equal( FITSValueKind.Integer, header.Properties[ 1 ].Value.Kind );
        Assert.Equal( 8L, header.Properties[ 1 ].Value.AsInteger );

        Assert.Equal( "NAXIS", header.Properties[ 2 ].Name );
        Assert.Equal( FITSValueKind.Integer, header.Properties[ 2 ].Value.Kind );
        Assert.Equal( 0L, header.Properties[ 2 ].Value.AsInteger );

        Assert.Equal( "FOOBAR", header.Properties[ 3 ].Name );
        Assert.Equal( FITSValueKind.Integer, header.Properties[ 3 ].Value.Kind );
        Assert.Equal( 42L, header.Properties[ 3 ].Value.AsInteger );
    }

    /// <summary>
    /// The extension sections are exposed in file order with their parsed
    /// properties.
    /// </summary>
    [ Fact ]
    public void Extensions()
    {
        ReadOnlyMemory< byte >        header     = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] );
        ReadOnlyMemory< byte >        ext1       = TestUtilities.HeaderBlock( [ ( "XTENSION", "'TABLE   '" ), ( "BITPIX", "8" ), ( "NAXIS", "0" ), ( "PCOUNT", "0" ), ( "GCOUNT", "1" ), ( "FOO", "1" ), ( "END", "" ) ] );
        ReadOnlyMemory< byte >        ext2       = TestUtilities.HeaderBlock( [ ( "XTENSION", "'IMAGE   '" ), ( "BITPIX", "8" ), ( "NAXIS", "0" ), ( "PCOUNT", "0" ), ( "GCOUNT", "1" ), ( "BAR", "2" ), ( "END", "" ) ] );
        FITSFile                      file       = new FITSFile( Combine( header, ext1, ext2 ), FITSParsingOptions.Lenient );
        IReadOnlyList< FITSSection >  extensions = file.Extensions;

        Assert.Equal( 2, extensions.Count );

        Assert.Equal( FITSSection.Kind.Extension, extensions[ 0 ].SectionKind );
        Assert.Equal( "XTENSION", extensions[ 0 ].Properties[ 0 ].Name );
        Assert.Equal( FITSValueKind.String, extensions[ 0 ].Properties[ 0 ].Value.Kind );
        Assert.Equal( "TABLE", extensions[ 0 ].Properties[ 0 ].Value.AsString );
        Assert.Equal( "FOO", extensions[ 0 ].Properties[ ^1 ].Name );
        Assert.Equal( 1L, extensions[ 0 ].Properties[ ^1 ].Value.AsInteger );

        Assert.Equal( FITSSection.Kind.Extension, extensions[ 1 ].SectionKind );
        Assert.Equal( "XTENSION", extensions[ 1 ].Properties[ 0 ].Name );
        Assert.Equal( FITSValueKind.String, extensions[ 1 ].Properties[ 0 ].Value.Kind );
        Assert.Equal( "IMAGE", extensions[ 1 ].Properties[ 0 ].Value.AsString );
        Assert.Equal( "BAR", extensions[ 1 ].Properties[ ^1 ].Name );
        Assert.Equal( 2L, extensions[ 1 ].Properties[ ^1 ].Value.AsInteger );
    }

    /// <summary>
    /// A valid extension header with the mandatory keywords is accepted.
    /// </summary>
    [ Fact ]
    public void ValidExtensionHeaderIsAccepted()
    {
        ReadOnlyMemory< byte > header = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] );
        ReadOnlyMemory< byte > ext    = TestUtilities.HeaderBlock( [ ( "XTENSION", "'IMAGE   '" ), ( "BITPIX", "8" ), ( "NAXIS", "0" ), ( "PCOUNT", "0" ), ( "GCOUNT", "1" ), ( "END", "" ) ] );

        _ = new FITSFile( Combine( header, ext ), FITSParsingOptions.Lenient );
    }

    /// <summary>
    /// An extension missing its mandatory keywords is rejected.
    /// </summary>
    [ Fact ]
    public void ExtensionMissingMandatoryKeywordsIsRejected()
    {
        ReadOnlyMemory< byte > header = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] );
        ReadOnlyMemory< byte > ext    = TestUtilities.HeaderBlock( [ ( "XTENSION", "'IMAGE   '" ), ( "FOO", "1" ), ( "END", "" ) ] );

        Assert.Throws< FITSException >( () => new FITSFile( Combine( header, ext ), FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// An extension missing <c>PCOUNT</c>/<c>GCOUNT</c> is rejected.
    /// </summary>
    [ Fact ]
    public void ExtensionMissingPcountGcountIsRejected()
    {
        ReadOnlyMemory< byte > header = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] );
        ReadOnlyMemory< byte > ext    = TestUtilities.HeaderBlock( [ ( "XTENSION", "'IMAGE   '" ), ( "BITPIX", "8" ), ( "NAXIS", "0" ), ( "END", "" ) ] );

        Assert.Throws< FITSException >( () => new FITSFile( Combine( header, ext ), FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// An extension whose <c>GCOUNT</c> precedes <c>PCOUNT</c> violates the
    /// mandatory keyword order and is rejected.
    /// </summary>
    [ Fact ]
    public void ExtensionWithMisorderedPcountGcountIsRejected()
    {
        ReadOnlyMemory< byte > header = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] );
        ReadOnlyMemory< byte > ext    = TestUtilities.HeaderBlock( [ ( "XTENSION", "'IMAGE   '" ), ( "BITPIX", "8" ), ( "NAXIS", "0" ), ( "GCOUNT", "1" ), ( "PCOUNT", "0" ), ( "END", "" ) ] );

        Assert.Throws< FITSException >( () => new FITSFile( Combine( header, ext ), FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// An extension whose <c>XTENSION</c> value is not a string is rejected.
    /// </summary>
    [ Fact ]
    public void ExtensionWithNonStringXtensionIsRejected()
    {
        ReadOnlyMemory< byte > header = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] );
        ReadOnlyMemory< byte > ext    = TestUtilities.HeaderBlock( [ ( "XTENSION", "8" ), ( "BITPIX", "8" ), ( "NAXIS", "0" ), ( "PCOUNT", "0" ), ( "GCOUNT", "1" ), ( "END", "" ) ] );

        Assert.Throws< FITSException >( () => new FITSFile( Combine( header, ext ), FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// A data-length mismatch (a declared data segment with no data blocks
    /// following) is rejected in strict mode.
    /// </summary>
    [ Fact ]
    public void DataLengthMismatchIsRejectedWhenStrict()
    {
        ReadOnlyMemory< byte > header = TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "1" ), ( "NAXIS1", "2880" ), ( "END", "" ) ] );

        Assert.Throws< FITSException >( () => new FITSFile( header, FITSParsingOptions.Strict ) );
    }

    /// <summary>
    /// A data-length mismatch is tolerated in lenient mode.
    /// </summary>
    [ Fact ]
    public void DataLengthMismatchIsToleratedWhenLenient()
    {
        ReadOnlyMemory< byte > header = TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "1" ), ( "NAXIS1", "2880" ), ( "END", "" ) ] );

        _ = new FITSFile( header, FITSParsingOptions.Lenient );
    }

    /// <summary>
    /// A file whose header carries an orphaned <c>CONTINUE</c> parses under lenient
    /// options and round-trips byte-for-byte, the record's bytes being retained.
    /// </summary>
    [ Fact ]
    public void OrphanedContinueFileRoundTrips()
    {
        ReadOnlyMemory< byte > block = TestUtilities.HeaderBlock(
            [
                ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "0" ),
                ( "FOOBAR", "'hello'" ), ( "CONTINUE", "', world'" ), ( "END", "" ),
            ]
        );
        FITSFile     file   = new FITSFile( block, FITSParsingOptions.Lenient );
        FITSSection? header = file.Header;

        Assert.NotNull( header );
        Assert.Contains( header.Properties, property => property.Name == "CONTINUE" );
        Assert.Equal( block.ToArray(), file.Data.ToArray() );
    }

    /// <summary>
    /// A data segment of exactly the declared size is accepted.
    /// </summary>
    [ Fact ]
    public void CorrectlySizedDataIsAccepted()
    {
        ReadOnlyMemory< byte > header = TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "1" ), ( "NAXIS1", "2880" ), ( "END", "" ) ] );

        _ = new FITSFile( Combine( header, TestUtilities.DataBlock( 0x00 ) ), FITSParsingOptions.Strict );
    }

    /// <summary>
    /// A <c>NAXISn</c> product that overflows 64 bits throws rather than trapping,
    /// even under lenient options.
    /// </summary>
    [ Fact ]
    public void NaxisProductOverflowThrowsInsteadOfTrapping()
    {
        ReadOnlyMemory< byte > header = TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "2" ), ( "NAXIS1", long.MaxValue.ToString( CultureInfo.InvariantCulture ) ), ( "NAXIS2", "2" ), ( "END", "" ) ] );

        Assert.Throws< FITSException >( () => new FITSFile( header, FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// An extension <c>PCOUNT + product</c> that overflows 64 bits throws rather
    /// than trapping.
    /// </summary>
    [ Fact ]
    public void ExtensionPcountProductOverflowThrowsInsteadOfTrapping()
    {
        ReadOnlyMemory< byte > header = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] );
        ReadOnlyMemory< byte > ext    = TestUtilities.HeaderBlock( [ ( "XTENSION", "'IMAGE   '" ), ( "BITPIX", "8" ), ( "NAXIS", "1" ), ( "NAXIS1", "1" ), ( "PCOUNT", long.MaxValue.ToString( CultureInfo.InvariantCulture ) ), ( "GCOUNT", "1" ), ( "END", "" ) ] );

        Assert.Throws< FITSException >( () => new FITSFile( Combine( header, ext ), FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// A <c>(|BITPIX| / 8) x elements</c> byte count that overflows 64 bits throws
    /// rather than trapping.
    /// </summary>
    [ Fact ]
    public void BitpixByteCountOverflowThrowsInsteadOfTrapping()
    {
        ReadOnlyMemory< byte > header = TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "64" ), ( "NAXIS", "1" ), ( "NAXIS1", ( long.MaxValue / 4 ).ToString( CultureInfo.InvariantCulture ) ), ( "END", "" ) ] );

        Assert.Throws< FITSException >( () => new FITSFile( header, FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// A non-ASCII data segment, fixed as data by the geometry, parses without
    /// being rejected or classified as a header.
    /// </summary>
    [ Fact ]
    public void NonAsciiDataSegmentParsesWithoutClassification()
    {
        ReadOnlyMemory< byte > header = TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "1" ), ( "NAXIS1", "2880" ), ( "END", "" ) ] );
        ReadOnlyMemory< byte > bytes  = Combine( header, TestUtilities.DataBlock( 0xFF ) );
        FITSFile               file   = new FITSFile( bytes, FITSParsingOptions.Strict );

        Assert.Equal( 2, file.Sections.Count );
        Assert.Equal( FITSSection.Kind.Data, file.Sections[ 1 ].SectionKind );
        Assert.Equal( bytes.ToArray(), file.Data.ToArray() );
    }

    /// <summary>
    /// A <c>NAXIS = 0</c> primary followed by an all-spaces block parses cleanly in
    /// strict mode, the padding retained rather than becoming a phantom data
    /// section.
    /// </summary>
    [ Fact ]
    public void TrailingPaddingAfterEndIsNotData()
    {
        ReadOnlyMemory< byte > header = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] );
        ReadOnlyMemory< byte > bytes  = Combine( header, TestUtilities.DataBlock( 0x20 ) );
        FITSFile               file   = new FITSFile( bytes, FITSParsingOptions.Strict );

        Assert.Single( file.Sections );
        Assert.All( file.Sections, section => Assert.NotEqual( FITSSection.Kind.Data, section.SectionKind ) );
        Assert.Empty( file.Extensions );
        Assert.Equal( bytes.ToArray(), file.Data.ToArray() );
    }

    /// <summary>
    /// A data block that is ASCII and begins with <c>XTENSION=</c> is consumed as
    /// data by the geometry, not mis-split into a new extension.
    /// </summary>
    [ Fact ]
    public void DataBlockResemblingExtensionIsConsumedAsData()
    {
        ReadOnlyMemory< byte > header = TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "1" ), ( "NAXIS1", "2880" ), ( "END", "" ) ] );
        ReadOnlyMemory< byte > data   = TestUtilities.HeaderBlock( [ "XTENSION= 'TABLE    '" ] );
        FITSFile               file   = new FITSFile( Combine( header, data ), FITSParsingOptions.Strict );

        Assert.Equal( 2, file.Sections.Count );
        Assert.Equal( FITSSection.Kind.Header, file.Sections[ 0 ].SectionKind );
        Assert.Equal( FITSSection.Kind.Data,   file.Sections[ 1 ].SectionKind );
        Assert.Empty( file.Extensions );
    }

    /// <summary>
    /// A multi-HDU file with an ASCII data block resembling an extension is split
    /// by geometry alone into the correct sections.
    /// </summary>
    [ Fact ]
    public void MultiHduFileSplitsByGeometry()
    {
        ReadOnlyMemory< byte > header = TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "1" ), ( "NAXIS1", "2880" ), ( "END", "" ) ] );
        ReadOnlyMemory< byte > data1  = TestUtilities.HeaderBlock( [ "XTENSION= 'TABLE    '" ] );
        ReadOnlyMemory< byte > ext    = TestUtilities.HeaderBlock( [ ( "XTENSION", "'IMAGE   '" ), ( "BITPIX", "8" ), ( "NAXIS", "1" ), ( "NAXIS1", "2880" ), ( "PCOUNT", "0" ), ( "GCOUNT", "1" ), ( "END", "" ) ] );
        ReadOnlyMemory< byte > data2  = TestUtilities.DataBlock( 0x00 );
        ReadOnlyMemory< byte > bytes  = Combine( header, data1, ext, data2 );
        FITSFile               file   = new FITSFile( bytes, FITSParsingOptions.Strict );

        Assert.Equal( 4, file.Sections.Count );
        Assert.Equal( FITSSection.Kind.Header,    file.Sections[ 0 ].SectionKind );
        Assert.Equal( FITSSection.Kind.Data,      file.Sections[ 1 ].SectionKind );
        Assert.Equal( FITSSection.Kind.Extension, file.Sections[ 2 ].SectionKind );
        Assert.Equal( FITSSection.Kind.Data,      file.Sections[ 3 ].SectionKind );
        Assert.Single( file.Extensions );
        Assert.Equal( bytes.ToArray(), file.Data.ToArray() );
    }

    /// <summary>
    /// A multi-block data segment truncated to fewer blocks than declared is
    /// rejected in strict mode and tolerated in lenient mode.
    /// </summary>
    [ Fact ]
    public void TruncatedMultiBlockDataIsRejectedWhenStrictToleratedWhenLenient()
    {
        ReadOnlyMemory< byte > header = TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "1" ), ( "NAXIS1", "5760" ), ( "END", "" ) ] );
        ReadOnlyMemory< byte > data   = TestUtilities.DataBlock( 0x00 );

        Assert.Throws< FITSException >( () => new FITSFile( Combine( header, data ), FITSParsingOptions.Strict ) );

        _ = new FITSFile( Combine( header, data ), FITSParsingOptions.Lenient );
    }

    /// <summary>
    /// A block count above <see cref="int"/> range is carried in 64 bits, so the
    /// expected data size is reported intact in the mismatch diagnostic rather than
    /// narrowed.
    /// </summary>
    [ Fact ]
    public void LargeBlockCountExpectedSizeIsNotNarrowed()
    {
        ReadOnlyMemory< byte > header = TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "1" ), ( "NAXIS1", "10000000000000" ), ( "END", "" ) ] );

        FITSException exception = Assert.Throws< FITSException >( () => new FITSFile( header, FITSParsingOptions.Strict ) );

        Assert.Contains( "10000000002240", exception.Message );
    }

    /// <summary>
    /// A primary header whose <c>END</c> is followed by a non-blank record still
    /// terminates the header section, so a following extension is parsed on its
    /// own.
    /// </summary>
    [ Fact ]
    public void EndNotLastRecordTerminatesSectionConsistently()
    {
        ReadOnlyMemory< byte > header = TestUtilities.HeaderBlock( [ "SIMPLE  = T", "BITPIX  = 8", "NAXIS   = 0", "END", "JUNK    = 1" ] );
        ReadOnlyMemory< byte > ext    = TestUtilities.HeaderBlock( [ ( "XTENSION", "'IMAGE   '" ), ( "BITPIX", "8" ), ( "NAXIS", "0" ), ( "PCOUNT", "0" ), ( "GCOUNT", "1" ), ( "END", "" ) ] );
        FITSFile               file   = new FITSFile( Combine( header, ext ), FITSParsingOptions.Lenient );

        Assert.Equal( 2, file.Sections.Count );
        Assert.Single( file.Extensions );
    }

    /// <summary>
    /// A trailing partial block is rejected in strict mode and zero-padded to full
    /// size in lenient mode.
    /// </summary>
    [ Fact ]
    public void TrailingPartialBlockIsRejectedWhenStrictPaddedWhenLenient()
    {
        ReadOnlyMemory< byte > header  = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] );
        byte[]                 partial = new byte[ 100 ];

        Array.Fill( partial, ( byte )0x20 );

        ReadOnlyMemory< byte > data = Combine( header, partial );

        Assert.Throws< FITSException >( () => new FITSFile( data, FITSParsingOptions.Strict ) );

        FITSFile file = new FITSFile( data, FITSParsingOptions.Lenient );

        Assert.Single( file.Sections );
        Assert.Equal( FITSFile.BlockSize * 2, file.Data.Length );
        Assert.Equal( Combine( header, partial, new byte[ FITSFile.BlockSize - 100 ] ).ToArray(), file.Data.ToArray() );
    }

    /// <summary>
    /// A header whose custom keyword is NUL-padded, with a NUL-filled block tail,
    /// is rejected in strict mode and recovered in lenient mode.
    /// </summary>
    [ Fact ]
    public void NulPaddedHeaderIsRejectedWhenStrictParsedWhenLenient()
    {
        List< byte > keyword = [];

        keyword.AddRange( Encoding.ASCII.GetBytes( "FOO" ) );
        keyword.AddRange( new byte[ 5 ] );
        keyword.AddRange( Encoding.ASCII.GetBytes( "= 1" ) );
        keyword.AddRange( Enumerable.Repeat( ( byte )0x20, FITSFile.CardSize - keyword.Count ) );

        List< byte > blockBytes = [];

        blockBytes.AddRange( Record( "SIMPLE  = T" ) );
        blockBytes.AddRange( Record( "BITPIX  = 8" ) );
        blockBytes.AddRange( Record( "NAXIS   = 0" ) );
        blockBytes.AddRange( keyword );
        blockBytes.AddRange( Record( "END" ) );
        blockBytes.AddRange( new byte[ FITSFile.BlockSize - blockBytes.Count ] );

        ReadOnlyMemory< byte > block = blockBytes.ToArray();

        Assert.Throws< FITSException >( () => new FITSFile( block, FITSParsingOptions.Strict ) );

        FITSFile file = new FITSFile( block, FITSParsingOptions.Lenient );

        Assert.Single( file.Sections );
        Assert.Equal( "FOO", file.Header?.Properties[ ^1 ].Name );
        Assert.Equal( 1L,    file.Header?.Properties[ ^1 ].Value.AsInteger );
        Assert.Equal( block.ToArray(), file.Data.ToArray() );
    }

    /// <summary>
    /// A geometry that does not overflow but exceeds the sanity ceiling is
    /// rejected.
    /// </summary>
    [ Fact ]
    public void AbsurdlyLargeButNonOverflowingGeometryIsRejected()
    {
        ReadOnlyMemory< byte > header = TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "1" ), ( "NAXIS1", "1000000000000000000" ), ( "END", "" ) ] );

        Assert.Throws< FITSException >( () => new FITSFile( header, FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// A clean file serialized with the lenient options reproduces its original
    /// bytes exactly for every sample file.
    /// </summary>
    [ Fact ]
    public void SerializedDataRoundTripsAllTestFiles()
    {
        Assert.NotEmpty( TestUtilities.TestFiles );

        foreach( string path in TestUtilities.TestFiles )
        {
            byte[]   data = File.ReadAllBytes( path );
            FITSFile file = new FITSFile( data, FITSParsingOptions.Lenient );

            Assert.True( file.SerializedData( FITSSerializationOptions.Lenient ).Span.SequenceEqual( data ), $"Round-trip mismatch for { Path.GetFileName( path ) }" );
        }
    }

    /// <summary>
    /// A file written to disk and reread reproduces its original bytes, both on disk
    /// and when reparsed.
    /// </summary>
    [ Fact ]
    public void WriteThenRereadReproducesBytes()
    {
        ReadOnlyMemory< byte > bytes = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [ ( "FOO", "42" ) ] );
        FITSFile               file  = new FITSFile( bytes, FITSParsingOptions.Strict );
        string                 path  = TempFitsPath();

        try
        {
            file.Write( path, FITSSerializationOptions.Strict );

            byte[]   reread   = File.ReadAllBytes( path );
            FITSFile reparsed = new FITSFile( reread, FITSParsingOptions.Strict );

            Assert.Equal( bytes.ToArray(), reread );
            Assert.Equal( bytes.ToArray(), reparsed.Data.ToArray() );
        }
        finally
        {
            TestUtilities.RemoveTemporaryFile( path );
        }
    }

    /// <summary>
    /// Strict serialization rejects a data-size mismatch that lenient serialization
    /// tolerates.
    /// </summary>
    [ Fact ]
    public void StrictSerializationRejectsDataSizeMismatch()
    {
        ReadOnlyMemory< byte > header = TestUtilities.HeaderBlock( [ ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "1" ), ( "NAXIS1", "5760" ), ( "END", "" ) ] );
        FITSFile               file   = new FITSFile( Combine( header, TestUtilities.DataBlock( 0x00 ) ), FITSParsingOptions.Lenient );

        Assert.Throws< FITSException >( () => file.SerializedData( FITSSerializationOptions.Strict ) );

        _ = file.SerializedData( FITSSerializationOptions.Lenient );
    }

    /// <summary>
    /// Writing to an unwritable location throws
    /// <see cref="FITSErrorKind.CannotWriteFile"/>.
    /// </summary>
    [ Fact ]
    public void WriteToUnwritableURLThrowsCannotWriteFile()
    {
        ReadOnlyMemory< byte > bytes = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] );
        FITSFile               file  = new FITSFile( bytes, FITSParsingOptions.Strict );
        string                 path  = $"/no/such/directory/{ Guid.NewGuid().ToString() }.fits";

        FITSException exception = Assert.Throws< FITSException >( () => file.Write( path, FITSSerializationOptions.Strict ) );

        Assert.Equal( FITSErrorKind.CannotWriteFile, exception.Kind );
    }

    /// <summary>
    /// A 3x2 unsigned-byte image built from scratch serializes to a header block and
    /// a data block and round-trips through a reparse.
    /// </summary>
    [ Fact ]
    public void BuildsPrimaryHDUFromScratchAndRoundTrips()
    {
        byte[]                 pixels = [ 1, 2, 3, 4, 5, 6 ];
        FITSFile               file   = new FITSFile( 8, [ 3, 2 ], pixels );
        ReadOnlyMemory< byte > bytes  = file.SerializedData( FITSSerializationOptions.Strict );

        Assert.Equal( FITSFile.BlockSize * 2, bytes.Length );

        FITSFile     reparsed = new FITSFile( bytes, FITSParsingOptions.Strict );
        FITSSection? header   = reparsed.Header;

        Assert.NotNull( header );
        Assert.Equal( 2, reparsed.Sections.Count );
        Assert.Equal( FITSValue.Logical( true ), header[ "SIMPLE" ]?.Value );
        Assert.Equal( 8L, header.Bitpix );
        Assert.Equal( 2L, header.Naxis );
        Assert.Equal( 3L, header.NaxisAt( 1 ) );
        Assert.Equal( 2L, header.NaxisAt( 2 ) );

        FITSSection? segment = reparsed.Sections.FirstOrDefault( section => section.SectionKind == FITSSection.Kind.Data );

        Assert.NotNull( segment );
        Assert.Equal( pixels, segment.Data.Slice( 0, 6 ).ToArray() );
    }

    /// <summary>
    /// A NAXIS = 0 primary with no axes and no data serializes to a single header
    /// block.
    /// </summary>
    [ Fact ]
    public void BuildsHeadersOnlyPrimaryWithNoData()
    {
        FITSFile               file  = new FITSFile( 8, [] );
        ReadOnlyMemory< byte > bytes = file.SerializedData( FITSSerializationOptions.Strict );

        Assert.Equal( FITSFile.BlockSize, bytes.Length );

        FITSFile reparsed = new FITSFile( bytes, FITSParsingOptions.Strict );

        Assert.Single( reparsed.Sections );
        Assert.Equal( 0L, reparsed.Header?.Naxis );
    }

    /// <summary>
    /// Appending an extension adds its mandatory keywords and declares
    /// <c>EXTEND = T</c> in the primary header.
    /// </summary>
    [ Fact ]
    public void AppendsExtensionAndAutoAddsExtendKeyword()
    {
        FITSFile file = new FITSFile( 8, [ 2, 2 ], new byte[] { 1, 2, 3, 4 } );

        file.AppendExtension( "IMAGE", 8, [ 2, 2 ], data: new byte[] { 5, 6, 7, 8 } );

        Assert.Equal( FITSValue.Logical( true ), file.Header?[ "EXTEND" ]?.Value );

        ReadOnlyMemory< byte > bytes    = file.SerializedData( FITSSerializationOptions.Strict );
        FITSFile               reparsed = new FITSFile( bytes, FITSParsingOptions.Strict );

        Assert.Single( reparsed.Extensions );

        FITSSection extension = reparsed.Extensions[ 0 ];

        Assert.Equal( FITSValue.Logical( true ),   reparsed.Header?[ "EXTEND" ]?.Value );
        Assert.Equal( FITSValue.String( "IMAGE" ), extension[ "XTENSION" ]?.Value );
        Assert.Equal( 8L, extension.Bitpix );
        Assert.Equal( 2L, extension.Naxis );
        Assert.Equal( FITSValue.Integer( 0 ), extension[ "PCOUNT" ]?.Value );
        Assert.Equal( FITSValue.Integer( 1 ), extension[ "GCOUNT" ]?.Value );
    }

    /// <summary>
    /// A from-scratch file written to disk and read back preserves its geometry and
    /// data.
    /// </summary>
    [ Fact ]
    public void WritesFromScratchFileToDiskAndReadsBack()
    {
        byte[]   pixels = [ 10, 20, 30, 40 ];
        FITSFile file   = new FITSFile( 8, [ 2, 2 ], pixels );
        string   path   = TempFitsPath();

        try
        {
            file.Write( path, FITSSerializationOptions.Strict );

            FITSFile     reread  = new FITSFile( path, FITSParsingOptions.Strict );
            FITSSection? segment = reread.Sections.FirstOrDefault( section => section.SectionKind == FITSSection.Kind.Data );

            Assert.NotNull( segment );
            Assert.Equal( 2L, reread.Header?.Naxis );
            Assert.Equal( pixels, segment.Data.Slice( 0, 4 ).ToArray() );
        }
        finally
        {
            TestUtilities.RemoveTemporaryFile( path );
        }
    }

    /// <summary>
    /// From-scratch construction defers the data-size check to write: a data segment
    /// too small for the geometry is rejected by strict serialization and tolerated
    /// by lenient.
    /// </summary>
    [ Fact ]
    public void FromScratchFileDefersDataSizeValidationToWrite()
    {
        FITSFile file = new FITSFile( 8, [ 5760 ], Filled( 0x00, 100 ) );

        Assert.Throws< FITSException >( () => file.SerializedData( FITSSerializationOptions.Strict ) );

        _ = file.SerializedData( FITSSerializationOptions.Lenient );
    }

    /// <summary>
    /// A pathological primary <c>NAXIS</c> does not overflow-trap the <c>EXTEND</c>
    /// insertion-index computation; the keyword is appended safely.
    /// </summary>
    [ Fact ]
    public void AppendExtensionWithPathologicalNaxisDoesNotTrap()
    {
        FITSFile     file    = new FITSFile( 8, [ 2, 2 ], new byte[] { 1, 2, 3, 4 } );
        FITSSection? primary = file.Header;

        Assert.NotNull( primary );

        primary.SetProperty( new FITSProperty( "NAXIS", long.MaxValue, FITSSerializationOptions.Strict ) );

        file.AppendExtension( "IMAGE", 8, [ 2, 2 ], data: new byte[] { 5, 6, 7, 8 } );

        Assert.Equal( FITSValue.Logical( true ), file.Header?[ "EXTEND" ]?.Value );
    }

    /// <summary>
    /// A NAXIS = 0 primary carrying a data segment is rejected on write with a
    /// message that names the zero-data geometry.
    /// </summary>
    [ Fact ]
    public void NaxisZeroPrimaryWithDataIsRejectedWithClearMessage()
    {
        FITSFile file = new FITSFile( 8, [], new byte[] { 1, 2, 3, 4 } );

        FITSException exception = Assert.Throws< FITSException >( () => file.SerializedData( FITSSerializationOptions.Strict ) );

        Assert.Contains( "data segment", exception.Message );
        Assert.Contains( "NAXIS",        exception.Message );
    }

    /// <summary>
    /// Editing a keyword in one section re-renders only that section; untouched
    /// sections are re-emitted from their retained bytes, byte-for-byte.
    /// </summary>
    [ Fact ]
    public void EditingOnlyOneSectionKeepsOthersByteForByte()
    {
        FITSFile builder = new FITSFile( 8, [ 2, 2 ], new byte[] { 1, 2, 3, 4 } );

        builder.AppendExtension( "IMAGE", 8, [ 2, 2 ], data: new byte[] { 5, 6, 7, 8 } );

        ReadOnlyMemory< byte > original = builder.SerializedData( FITSSerializationOptions.Strict );
        FITSFile               file     = new FITSFile( original, FITSParsingOptions.Strict );
        byte[]                 before   = ExtensionData( file );

        file.Header?.SetProperty( new FITSProperty( "OBJECT", "M42", FITSSerializationOptions.Strict ) );

        ReadOnlyMemory< byte > rewritten = file.SerializedData( FITSSerializationOptions.Strict );
        FITSFile               reparsed  = new FITSFile( rewritten, FITSParsingOptions.Strict );

        Assert.Equal( FITSValue.String( "M42" ), reparsed.Header?[ "OBJECT" ]?.Value );
        Assert.Equal( before, ExtensionData( reparsed ) );
    }

    /// <summary>
    /// Replacing a parsed data segment's payload re-renders only that segment; the
    /// header stays byte-for-byte identical.
    /// </summary>
    [ Fact ]
    public void ReplacingDataPayloadOfParsedSectionRerendersOnlyThatSegment()
    {
        FITSFile     builder = new FITSFile( 8, [ 2, 2 ], new byte[] { 1, 2, 3, 4 } );
        FITSFile     file    = new FITSFile( builder.SerializedData( FITSSerializationOptions.Strict ), FITSParsingOptions.Strict );
        FITSSection? header  = file.Header;

        Assert.NotNull( header );

        byte[]       headerBefore = header.Data.ToArray();
        FITSSection? segment      = file.Sections.FirstOrDefault( section => section.SectionKind == FITSSection.Kind.Data );

        Assert.NotNull( segment );

        segment.SetDataPayload( new byte[] { 9, 8, 7, 6 } );

        FITSFile reparsed = new FITSFile( file.SerializedData( FITSSerializationOptions.Strict ), FITSParsingOptions.Strict );

        Assert.Equal( headerBefore, reparsed.Header?.Data.ToArray() );
        Assert.Equal( new byte[] { 9, 8, 7, 6 }, reparsed.Sections[ ^1 ].Data.Slice( 0, 4 ).ToArray() );
    }

    /// <summary>
    /// Removing an extension drops its header and data segment together, and an
    /// out-of-range index is rejected.
    /// </summary>
    [ Fact ]
    public void RemoveExtensionDropsHeaderAndData()
    {
        FITSFile file = new FITSFile( 8, [] );

        file.AppendExtension( "IMAGE", 8, [ 2, 2 ], data: new byte[] { 1, 2, 3, 4 } );
        file.AppendExtension( "IMAGE", 8, [ 3 ],    data: new byte[] { 7, 8, 9 } );

        Assert.Equal( 2, file.Extensions.Count );

        file.RemoveExtension( 0 );

        Assert.Single( file.Extensions );
        Assert.Throws< FITSException >( () => file.RemoveExtension( 5 ) );

        FITSFile reparsed = new FITSFile( file.SerializedData( FITSSerializationOptions.Strict ), FITSParsingOptions.Strict );

        Assert.Single( reparsed.Extensions );
        Assert.Equal( 1L, reparsed.Extensions[ 0 ].Naxis );
    }

    /// <summary>
    /// Moving an extension reorders the HDUs, and an out-of-range index is rejected.
    /// </summary>
    [ Fact ]
    public void MoveExtensionReordersHDUs()
    {
        FITSFile file = new FITSFile( 8, [] );

        file.AppendExtension( "IMAGE", 8, [ 2, 2 ], data: new byte[] { 1, 2, 3, 4 } );
        file.AppendExtension( "IMAGE", 8, [ 3 ],    data: new byte[] { 7, 8, 9 } );

        file.MoveExtension( 1, 0 );

        Assert.Throws< FITSException >( () => file.MoveExtension( 0, 9 ) );

        FITSFile reparsed = new FITSFile( file.SerializedData( FITSSerializationOptions.Strict ), FITSParsingOptions.Strict );

        Assert.Equal( 2, reparsed.Extensions.Count );
        Assert.Equal( 1L, reparsed.Extensions[ 0 ].Naxis );
        Assert.Equal( 2L, reparsed.Extensions[ 1 ].Naxis );
    }

    /// <summary>
    /// Re-shaping the primary updates its geometry keywords and data segment
    /// together.
    /// </summary>
    [ Fact ]
    public void SetPrimaryDataUpdatesGeometryAndPayloadTogether()
    {
        FITSFile builder = new FITSFile( 8, [ 2, 2 ], new byte[] { 1, 2, 3, 4 } );
        FITSFile file    = new FITSFile( builder.SerializedData( FITSSerializationOptions.Strict ), FITSParsingOptions.Strict );

        file.SetPrimaryData( 16, [ 3, 3 ], Filled( 0xAB, 18 ) );

        FITSFile     reparsed = new FITSFile( file.SerializedData( FITSSerializationOptions.Strict ), FITSParsingOptions.Strict );
        FITSSection? header   = reparsed.Header;

        Assert.NotNull( header );
        Assert.Equal( 16L, header.Bitpix );
        Assert.Equal( 2L,  header.Naxis );
        Assert.Equal( 3L,  header.NaxisAt( 1 ) );
        Assert.Equal( 3L,  header.NaxisAt( 2 ) );

        FITSSection? segment = reparsed.Sections.FirstOrDefault( section => section.SectionKind == FITSSection.Kind.Data );

        Assert.NotNull( segment );
        Assert.Equal( Filled( 0xAB, 18 ), segment.Data.Slice( 0, 18 ).ToArray() );
    }

    /// <summary>
    /// Re-shaping an extension updates its geometry and preserves its
    /// <c>PCOUNT</c>/<c>GCOUNT</c>.
    /// </summary>
    [ Fact ]
    public void SetExtensionDataUpdatesGeometryAndPreservesPcountGcount()
    {
        FITSFile builder = new FITSFile( 8, [] );

        builder.AppendExtension( "IMAGE", 8, [ 2, 2 ], data: new byte[] { 1, 2, 3, 4 } );

        FITSFile file = new FITSFile( builder.SerializedData( FITSSerializationOptions.Strict ), FITSParsingOptions.Strict );

        file.SetExtensionData( 0, 16, [ 3 ], Filled( 0xCD, 6 ) );

        FITSFile     reparsed  = new FITSFile( file.SerializedData( FITSSerializationOptions.Strict ), FITSParsingOptions.Strict );
        FITSSection? extension = reparsed.Extensions.FirstOrDefault();

        Assert.NotNull( extension );
        Assert.Equal( 16L, extension.Bitpix );
        Assert.Equal( 1L,  extension.Naxis );
        Assert.Equal( 3L,  extension.NaxisAt( 1 ) );
        Assert.Equal( FITSValue.Integer( 0 ), extension[ "PCOUNT" ]?.Value );
        Assert.Equal( FITSValue.Integer( 1 ), extension[ "GCOUNT" ]?.Value );

        FITSSection segment = reparsed.Sections[ ^1 ];

        Assert.Equal( FITSSection.Kind.Data, segment.SectionKind );
        Assert.Equal( Filled( 0xCD, 6 ), segment.Data.Slice( 0, 6 ).ToArray() );
    }

    /// <summary>
    /// A pathological extension index throws rather than overflow-trapping the
    /// bounds check.
    /// </summary>
    [ Fact ]
    public void ExtensionEditsWithPathologicalIndexDoNotTrap()
    {
        FITSFile file = new FITSFile( 8, [] );

        file.AppendExtension( "IMAGE", 8, [ 2, 2 ], data: new byte[] { 1, 2, 3, 4 } );

        Assert.Throws< FITSException >( () => file.RemoveExtension( int.MaxValue ) );
        Assert.Throws< FITSException >( () => file.SetExtensionData( int.MaxValue, 8, [ 1 ], new byte[] { 0 } ) );
    }

    /// <summary>
    /// Concatenates block buffers into a single buffer.
    /// </summary>
    /// <param name="parts">The buffers to concatenate, in order.</param>
    /// <returns>The concatenated bytes.</returns>
    private static ReadOnlyMemory< byte > Combine( params ReadOnlyMemory< byte >[] parts )
    {
        return parts.SelectMany( part => part.ToArray() ).ToArray();
    }

    /// <summary>
    /// Renders a record string as an 80-byte, space-padded ASCII card.
    /// </summary>
    /// <param name="text">The record text.</param>
    /// <returns>The 80 ASCII bytes of the card.</returns>
    private static byte[] Record( string text )
    {
        return Encoding.ASCII.GetBytes( text.PaddedOrTruncated( FITSFile.CardSize ) );
    }

    /// <summary>
    /// A unique temporary <c>.fits</c> path in the system temporary directory.
    /// </summary>
    /// <returns>The temporary file path.</returns>
    private static string TempFitsPath()
    {
        return Path.Combine( Path.GetTempPath(), Guid.NewGuid().ToString() + ".fits" );
    }

    /// <summary>
    /// Reports whether a file can currently be opened for reading.
    /// </summary>
    /// <param name="path">The file path to probe.</param>
    /// <returns><c>true</c> if the file opens for reading.</returns>
    private static bool IsReadable( string path )
    {
        try
        {
            using FileStream stream = File.OpenRead( path );

            return true;
        }
        catch( IOException )
        {
            return false;
        }
        catch( UnauthorizedAccessException )
        {
            return false;
        }
    }

    /// <summary>
    /// Builds a byte array filled with a repeated value.
    /// </summary>
    /// <param name="value">The byte to repeat.</param>
    /// <param name="count">The number of bytes.</param>
    /// <returns>
    /// A <paramref name="count"/>-byte array of <paramref name="value"/> bytes.
    /// </returns>
    private static byte[] Filled( byte value, int count )
    {
        byte[] bytes = new byte[ count ];

        Array.Fill( bytes, value );

        return bytes;
    }

    /// <summary>
    /// The serialized bytes of a file's first extension section.
    /// </summary>
    /// <param name="file">The file whose first extension section to serialize.</param>
    /// <returns>The first extension section's serialized bytes.</returns>
    private static byte[] ExtensionData( FITSFile file )
    {
        FITSSection? extension = file.Extensions.FirstOrDefault();

        Assert.NotNull( extension );

        return extension.Data.ToArray();
    }
}
