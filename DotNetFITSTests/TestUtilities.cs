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
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using DotNetFITS;

namespace DotNetFITSTests;

/// <summary>
/// Shared fixtures and block-construction helpers for the test suite, together
/// with the self-tests that verify those helpers.
/// </summary>
/// <remarks>
/// Blocks are produced as <see cref="ReadOnlyMemory{ Byte }"/> - the model's data
/// type - so they feed the block, section and file layers directly.
/// </remarks>
public class TestUtilities
{
    /// <summary>
    /// The sample <c>.fits</c>/<c>.fit</c> files used as parsing fixtures.
    /// </summary>
    /// <remarks>
    /// The heavy fixtures live in the <c>Test Files</c> directory at the
    /// repository root, outside any project, so they are not copied to the build
    /// output. They are located relative to this source file's compile-time path
    /// (captured at compile time via <c>[CallerFilePath]</c>): a test assembly only ever
    /// runs from its own checkout, so the path stays valid at run time. Returned
    /// sorted by file name.
    /// </remarks>
    public static IReadOnlyList< string > TestFiles => ResolveTestFiles();

    /// <summary>
    /// Resolves <see cref="TestFiles"/> relative to this source file's location.
    /// </summary>
    /// <param name="sourceFilePath">
    /// The compile-time path of the calling source file, supplied automatically by
    /// <see cref="CallerFilePathAttribute"/>; it is not passed explicitly.
    /// </param>
    /// <returns>The sample files, sorted by file name; empty when none are found.</returns>
    private static IReadOnlyList< string > ResolveTestFiles( [ CallerFilePath ] string sourceFilePath = "" )
    {
        string? testsDirectory = Path.GetDirectoryName( sourceFilePath );
        string? repositoryRoot = testsDirectory is null ? null : Path.GetDirectoryName( testsDirectory );

        if( repositoryRoot is null )
        {
            return [];
        }

        string root = Path.Combine( repositoryRoot, "Test Files" );

        if( Directory.Exists( root ) == false )
        {
            return [];
        }

        return Directory.EnumerateFiles( root, "*", SearchOption.AllDirectories )
            .Where( path => Path.GetExtension( path ) is ".fits" or ".fit" )
            .OrderBy( path => Path.GetFileName( path ), StringComparer.Ordinal )
            .ToList();
    }

    /// <summary>
    /// Builds a single full-size block filled with a repeated byte.
    /// </summary>
    /// <param name="fill">The byte to repeat across the whole block.</param>
    /// <returns>
    /// A <see cref="FITSFile.BlockSize"/>-byte block of <paramref name="fill"/> bytes.
    /// </returns>
    public static ReadOnlyMemory< byte > DataBlock( byte fill )
    {
        byte[] block = new byte[ FITSFile.BlockSize ];

        Array.Fill( block, fill );

        return block;
    }

    /// <summary>
    /// Builds a header block from keyword/value pairs.
    /// </summary>
    /// <remarks>
    /// Each pair is rendered as a card, formatting <c>END</c>,
    /// <c>HISTORY</c>/<c>COMMENT</c> and <c>CONTINUE</c> keywords according to
    /// their syntax and using the <c>keyword= value</c> form for everything else.
    /// </remarks>
    /// <param name="keywords">The keyword name/value pairs to render.</param>
    /// <returns>A full-size, space-padded header block.</returns>
    /// <exception cref="TestError">
    /// A keyword name exceeds <see cref="FITSFile.KeywordLength"/> characters, or
    /// the block overflows.
    /// </exception>
    public static ReadOnlyMemory< byte > HeaderBlock( IReadOnlyList< ( string Name, string Value ) > keywords )
    {
        IEnumerable< string > fields = keywords.Select
        (
            keyword =>
            {
                if( keyword.Name.Length > FITSFile.KeywordLength )
                {
                    throw TestError.Invalid( "Keyword name is too long" );
                }

                string name = keyword.Name.PaddedOrTruncated( FITSFile.KeywordLength );

                return keyword.Name switch
                {
                    "END"                  => name,
                    "HISTORY" or "COMMENT" => name + keyword.Value,
                    "CONTINUE"             => name + "  " + keyword.Value,
                    _                      => name + "= " + keyword.Value,
                };
            }
        );

        return HeaderBlock( fields.ToList() );
    }

    /// <summary>
    /// Builds a header block from pre-formatted record strings.
    /// </summary>
    /// <remarks>
    /// Each field is padded to a card and the assembled text is padded to a full
    /// block.
    /// </remarks>
    /// <param name="fields">The record strings, each at most <see cref="FITSFile.CardSize"/> characters.</param>
    /// <returns>A full-size, space-padded header block.</returns>
    /// <exception cref="TestError">
    /// A field exceeds <see cref="FITSFile.CardSize"/> characters, the records
    /// overflow a block, or the text is not ASCII.
    /// </exception>
    public static ReadOnlyMemory< byte > HeaderBlock( IReadOnlyList< string > fields )
    {
        string text = string.Concat
        (
            fields.Select
            (
                field =>
                {
                    if( field.Length > FITSFile.CardSize )
                    {
                        throw TestError.Invalid( "Keyword line is too long" );
                    }

                    return field.PaddedOrTruncated( FITSFile.CardSize );
                }
            )
        );

        if( text.Length > FITSFile.BlockSize )
        {
            throw TestError.Invalid( "Header block is too long" );
        }

        string padded = text.PaddedOrTruncated( FITSFile.BlockSize );

        if( padded.Any( character => character > '\u007F' ) )
        {
            throw TestError.Invalid( "Cannot convert string to ASCII data" );
        }

        return Encoding.ASCII.GetBytes( padded );
    }

    /// <summary>
    /// Builds a valid primary-header block prefixed with the mandatory keywords.
    /// </summary>
    /// <remarks>
    /// Prepends <c>SIMPLE</c>/<c>BITPIX</c>/<c>NAXIS</c>, appends the given
    /// keywords, and optionally adds an <c>END</c> marker.
    /// </remarks>
    /// <param name="includeEndMarker">Whether to append the <c>END</c> record.</param>
    /// <param name="keywords">Additional keyword name/value pairs to include.</param>
    /// <returns>A full-size header block.</returns>
    public static ReadOnlyMemory< byte > StandardHeaderBlock( bool includeEndMarker, IReadOnlyList< ( string Name, string Value ) > keywords )
    {
        List< ( string Name, string Value ) > records =
        [
            ( "SIMPLE", "T" ),
            ( "BITPIX", "8" ),
            ( "NAXIS",  "0" ),
            .. keywords,
        ];

        if( includeEndMarker )
        {
            records.Add( ( "END", "" ) );
        }

        return HeaderBlock( records );
    }

    /// <summary>
    /// Builds a valid extension-header block prefixed with the mandatory keywords.
    /// </summary>
    /// <remarks>
    /// Prepends <c>XTENSION</c>/<c>BITPIX</c>/<c>NAXIS</c>, appends the given
    /// keywords, and optionally adds an <c>END</c> marker.
    /// </remarks>
    /// <param name="includeEndMarker">Whether to append the <c>END</c> record.</param>
    /// <param name="keywords">Additional keyword name/value pairs to include.</param>
    /// <returns>A full-size extension-header block.</returns>
    public static ReadOnlyMemory< byte > StandardExtensionBlock( bool includeEndMarker, IReadOnlyList< ( string Name, string Value ) > keywords )
    {
        List< ( string Name, string Value ) > records =
        [
            ( "XTENSION", "'TABLE    '" ),
            ( "BITPIX",   "8"           ),
            ( "NAXIS",    "0"           ),
            .. keywords,
        ];

        if( includeEndMarker )
        {
            records.Add( ( "END", "" ) );
        }

        return HeaderBlock( records );
    }

    /// <summary>
    /// Removes a temporary file created by a test, ignoring any failure.
    /// </summary>
    /// <remarks>
    /// Kept <c>internal</c> rather than <c>public</c>: a public <c>void</c> method on this
    /// test class would be mistaken for an unmarked test by the xUnit analyzer
    /// (<c>xUnit1013</c>). It is reachable from every test in the assembly.
    /// </remarks>
    /// <param name="path">The path of the temporary file to remove.</param>
    internal static void RemoveTemporaryFile( string path )
    {
        try
        {
            File.Delete( path );
        }
        catch( Exception )
        {
            // Cleanup is best-effort: a deletion failure must not mask the test's
            // own result.
        }
    }

    /// <summary>
    /// The set of sample files discovered at the repository root is non-empty.
    /// </summary>
    [ Fact ]
    public void HasTestFiles()
    {
        Assert.NotEmpty( TestUtilities.TestFiles );
    }

    /// <summary>
    /// A data block is exactly one FITS block in size.
    /// </summary>
    [ Fact ]
    public void DataBlockHasBlockSize()
    {
        Assert.Equal( FITSFile.BlockSize, TestUtilities.DataBlock( 0x00 ).Length );
        Assert.Equal( FITSFile.BlockSize, TestUtilities.DataBlock( 0xFF ).Length );
    }

    /// <summary>
    /// A header block built from keyword/value pairs renders each keyword with
    /// the correct record syntax and pads the block with spaces.
    /// </summary>
    [ Fact ]
    public void HeaderBlockFromKeywordsRendersEachRecord()
    {
        ReadOnlyMemory< byte > block = TestUtilities.HeaderBlock(
        [
            ( "SIMPLE",   "T"        ),
            ( "BITPIX",   "8"        ),
            ( "NAXIS",    "0"        ),
            ( "FOO",      "'Test&'"  ),
            ( "CONTINUE", "'Test'"   ),
            ( "HISTORY",  "Test"     ),
            ( "COMMENT",  "Test"     ),
            ( "END",      ""         ),
        ] );

        Assert.Equal( FITSFile.BlockSize, block.Length );

        string[] records = block.Chunked( FITSFile.CardSize ).Select( chunk => Encoding.ASCII.GetString( chunk.Span ) ).ToArray();

        Assert.Equal( "SIMPLE  = T"      .PadRight( FITSFile.CardSize ), records[ 0 ] );
        Assert.Equal( "BITPIX  = 8"      .PadRight( FITSFile.CardSize ), records[ 1 ] );
        Assert.Equal( "NAXIS   = 0"      .PadRight( FITSFile.CardSize ), records[ 2 ] );
        Assert.Equal( "FOO     = 'Test&'".PadRight( FITSFile.CardSize ), records[ 3 ] );
        Assert.Equal( "CONTINUE  'Test'" .PadRight( FITSFile.CardSize ), records[ 4 ] );
        Assert.Equal( "HISTORY Test"     .PadRight( FITSFile.CardSize ), records[ 5 ] );
        Assert.Equal( "COMMENT Test"     .PadRight( FITSFile.CardSize ), records[ 6 ] );
        Assert.Equal( "END"              .PadRight( FITSFile.CardSize ), records[ 7 ] );

        Assert.All( records.Skip( 8 ), record => Assert.Equal( new string( ' ', FITSFile.CardSize ), record ) );
    }

    /// <summary>
    /// A header block built from pre-formatted record strings pads each record to
    /// a card and the block with spaces.
    /// </summary>
    [ Fact ]
    public void HeaderBlockFromFieldsPadsEachRecord()
    {
        ReadOnlyMemory< byte > block = TestUtilities.HeaderBlock(
        [
            "SIMPLE  = T",
            "BITPIX  = 8",
            "NAXIS   = 0",
            "END",
        ] );

        Assert.Equal( FITSFile.BlockSize, block.Length );

        string[] records = block.Chunked( FITSFile.CardSize ).Select( chunk => Encoding.ASCII.GetString( chunk.Span ) ).ToArray();

        Assert.Equal( "SIMPLE  = T".PadRight( FITSFile.CardSize ), records[ 0 ] );
        Assert.Equal( "BITPIX  = 8".PadRight( FITSFile.CardSize ), records[ 1 ] );
        Assert.Equal( "NAXIS   = 0".PadRight( FITSFile.CardSize ), records[ 2 ] );
        Assert.Equal( "END"        .PadRight( FITSFile.CardSize ), records[ 3 ] );

        Assert.All( records.Skip( 4 ), record => Assert.Equal( new string( ' ', FITSFile.CardSize ), record ) );
    }

    /// <summary>
    /// A standard header block prepends the mandatory <c>SIMPLE</c>/<c>BITPIX</c>/
    /// <c>NAXIS</c> keywords and appends an <c>END</c> marker when requested.
    /// </summary>
    [ Fact ]
    public void StandardHeaderBlockWithEndMarkerPrependsMandatoryKeywords()
    {
        ReadOnlyMemory< byte > block = TestUtilities.StandardHeaderBlock(
            includeEndMarker: true,
            keywords:
            [
                ( "FOO", "42" ),
                ( "BAR", "00" ),
            ]
        );

        Assert.Equal( FITSFile.BlockSize, block.Length );

        string[] records = block.Chunked( FITSFile.CardSize ).Select( chunk => Encoding.ASCII.GetString( chunk.Span ) ).ToArray();

        Assert.Equal( "SIMPLE  = T" .PadRight( FITSFile.CardSize ), records[ 0 ] );
        Assert.Equal( "BITPIX  = 8" .PadRight( FITSFile.CardSize ), records[ 1 ] );
        Assert.Equal( "NAXIS   = 0" .PadRight( FITSFile.CardSize ), records[ 2 ] );
        Assert.Equal( "FOO     = 42".PadRight( FITSFile.CardSize ), records[ 3 ] );
        Assert.Equal( "BAR     = 00".PadRight( FITSFile.CardSize ), records[ 4 ] );
        Assert.Equal( "END"         .PadRight( FITSFile.CardSize ), records[ 5 ] );

        Assert.All( records.Skip( 6 ), record => Assert.Equal( new string( ' ', FITSFile.CardSize ), record ) );
    }

    /// <summary>
    /// A standard header block without an <c>END</c> marker omits it while still
    /// prepending the mandatory keywords.
    /// </summary>
    [ Fact ]
    public void StandardHeaderBlockWithoutEndMarkerOmitsTheEndRecord()
    {
        ReadOnlyMemory< byte > block = TestUtilities.StandardHeaderBlock(
            includeEndMarker: false,
            keywords:
            [
                ( "FOO", "42" ),
                ( "BAR", "00" ),
            ]
        );

        Assert.Equal( FITSFile.BlockSize, block.Length );

        string[] records = block.Chunked( FITSFile.CardSize ).Select( chunk => Encoding.ASCII.GetString( chunk.Span ) ).ToArray();

        Assert.Equal( "SIMPLE  = T" .PadRight( FITSFile.CardSize ), records[ 0 ] );
        Assert.Equal( "BITPIX  = 8" .PadRight( FITSFile.CardSize ), records[ 1 ] );
        Assert.Equal( "NAXIS   = 0" .PadRight( FITSFile.CardSize ), records[ 2 ] );
        Assert.Equal( "FOO     = 42".PadRight( FITSFile.CardSize ), records[ 3 ] );
        Assert.Equal( "BAR     = 00".PadRight( FITSFile.CardSize ), records[ 4 ] );

        Assert.All( records.Skip( 5 ), record => Assert.Equal( new string( ' ', FITSFile.CardSize ), record ) );
    }

    /// <summary>
    /// A standard extension block prepends the mandatory <c>XTENSION</c>/
    /// <c>BITPIX</c>/<c>NAXIS</c> keywords and appends an <c>END</c> marker when
    /// requested.
    /// </summary>
    [ Fact ]
    public void StandardExtensionBlockWithEndMarkerPrependsMandatoryKeywords()
    {
        ReadOnlyMemory< byte > block = TestUtilities.StandardExtensionBlock(
            includeEndMarker: true,
            keywords:
            [
                ( "FOO", "42" ),
                ( "BAR", "00" ),
            ]
        );

        Assert.Equal( FITSFile.BlockSize, block.Length );

        string[] records = block.Chunked( FITSFile.CardSize ).Select( chunk => Encoding.ASCII.GetString( chunk.Span ) ).ToArray();

        Assert.Equal( "XTENSION= 'TABLE    '".PadRight( FITSFile.CardSize ), records[ 0 ] );
        Assert.Equal( "BITPIX  = 8"          .PadRight( FITSFile.CardSize ), records[ 1 ] );
        Assert.Equal( "NAXIS   = 0"          .PadRight( FITSFile.CardSize ), records[ 2 ] );
        Assert.Equal( "FOO     = 42"         .PadRight( FITSFile.CardSize ), records[ 3 ] );
        Assert.Equal( "BAR     = 00"         .PadRight( FITSFile.CardSize ), records[ 4 ] );
        Assert.Equal( "END"                  .PadRight( FITSFile.CardSize ), records[ 5 ] );

        Assert.All( records.Skip( 6 ), record => Assert.Equal( new string( ' ', FITSFile.CardSize ), record ) );
    }

    /// <summary>
    /// A standard extension block without an <c>END</c> marker omits it while
    /// still prepending the mandatory keywords.
    /// </summary>
    [ Fact ]
    public void StandardExtensionBlockWithoutEndMarkerOmitsTheEndRecord()
    {
        ReadOnlyMemory< byte > block = TestUtilities.StandardExtensionBlock(
            includeEndMarker: false,
            keywords:
            [
                ( "FOO", "42" ),
                ( "BAR", "00" ),
            ]
        );

        Assert.Equal( FITSFile.BlockSize, block.Length );

        string[] records = block.Chunked( FITSFile.CardSize ).Select( chunk => Encoding.ASCII.GetString( chunk.Span ) ).ToArray();

        Assert.Equal( "XTENSION= 'TABLE    '".PadRight( FITSFile.CardSize ), records[ 0 ] );
        Assert.Equal( "BITPIX  = 8"          .PadRight( FITSFile.CardSize ), records[ 1 ] );
        Assert.Equal( "NAXIS   = 0"          .PadRight( FITSFile.CardSize ), records[ 2 ] );
        Assert.Equal( "FOO     = 42"         .PadRight( FITSFile.CardSize ), records[ 3 ] );
        Assert.Equal( "BAR     = 00"         .PadRight( FITSFile.CardSize ), records[ 4 ] );

        Assert.All( records.Skip( 5 ), record => Assert.Equal( new string( ' ', FITSFile.CardSize ), record ) );
    }
}
