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
using System.Linq;
using DotNetFITS;

namespace DotNetFITSTests;

/// <summary>
/// Unit tests for <see cref="FITSSection"/>.
/// </summary>
/// <remarks>
/// The section-level behaviors that a header round-trips through the file layer -
/// keyword lookup, geometry accessors, clean-versus-dirty serialization,
/// building from a model, in-place editing and single ownership - are exercised
/// here directly, by building a section from a block and finalizing it (which is
/// what the file layer does internally) and re-parsing rendered bytes through a
/// fresh <see cref="FITSSection"/>. The <c>FITSFile</c>-based variants of those
/// tests are added with the file reader.
/// </remarks>
public class FITSSectionTests
{
    /// <summary>
    /// A data section is created from a block, or empty, and reports its kind and
    /// whether it holds any bytes.
    /// </summary>
    [ Fact ]
    public void InitDataSection()
    {
        FITSBlock   block    = new FITSBlock( TestUtilities.DataBlock( 0xFF ), FITSParsingOptions.Strict );
        FITSSection section1 = new FITSSection( FITSSection.Kind.Data, block );
        FITSSection section2 = new FITSSection( FITSSection.Kind.Data, block: null );

        Assert.Equal( FITSSection.Kind.Data, section1.SectionKind );
        Assert.Equal( FITSSection.Kind.Data, section2.SectionKind );

        Assert.False( section1.Data.IsEmpty );
        Assert.True( section2.Data.IsEmpty );
    }

    /// <summary>
    /// A header section is created from a block (with or without an <c>END</c>
    /// marker), or empty, and reports its kind and content.
    /// </summary>
    [ Fact ]
    public void InitHeaderSection()
    {
        FITSBlock   block1   = new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true,  keywords: [] ), FITSParsingOptions.Strict );
        FITSBlock   block2   = new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: false, keywords: [] ), FITSParsingOptions.Strict );
        FITSSection section1 = new FITSSection( FITSSection.Kind.Header, block1 );
        FITSSection section2 = new FITSSection( FITSSection.Kind.Header, block2 );
        FITSSection section3 = new FITSSection( FITSSection.Kind.Header, block: null );

        Assert.Equal( FITSSection.Kind.Header, section1.SectionKind );
        Assert.Equal( FITSSection.Kind.Header, section2.SectionKind );
        Assert.Equal( FITSSection.Kind.Header, section3.SectionKind );

        Assert.False( section1.Data.IsEmpty );
        Assert.False( section2.Data.IsEmpty );
        Assert.True( section3.Data.IsEmpty );
    }

    /// <summary>
    /// An extension section is created from a block (with or without an <c>END</c>
    /// marker), or empty, and reports its kind and content.
    /// </summary>
    [ Fact ]
    public void InitExtensionSection()
    {
        FITSBlock   block1   = new FITSBlock( TestUtilities.StandardExtensionBlock( includeEndMarker: true,  keywords: [] ), FITSParsingOptions.Strict );
        FITSBlock   block2   = new FITSBlock( TestUtilities.StandardExtensionBlock( includeEndMarker: false, keywords: [] ), FITSParsingOptions.Strict );
        FITSSection section1 = new FITSSection( FITSSection.Kind.Extension, block1 );
        FITSSection section2 = new FITSSection( FITSSection.Kind.Extension, block2 );
        FITSSection section3 = new FITSSection( FITSSection.Kind.Extension, block: null );

        Assert.Equal( FITSSection.Kind.Extension, section1.SectionKind );
        Assert.Equal( FITSSection.Kind.Extension, section2.SectionKind );
        Assert.Equal( FITSSection.Kind.Extension, section3.SectionKind );

        Assert.False( section1.Data.IsEmpty );
        Assert.False( section2.Data.IsEmpty );
        Assert.True( section3.Data.IsEmpty );
    }

    /// <summary>
    /// A data section's serialized bytes are its blocks concatenated in append
    /// order.
    /// </summary>
    [ Fact ]
    public void DataConcatenatesBlocksInOrder()
    {
        ReadOnlyMemory< byte > bytes1  = TestUtilities.DataBlock( 0x01 );
        ReadOnlyMemory< byte > bytes2  = TestUtilities.DataBlock( 0x02 );
        FITSSection            section = new FITSSection( FITSSection.Kind.Data, new FITSBlock( bytes1, FITSParsingOptions.Strict ) );

        section.Append( new FITSBlock( bytes2, FITSParsingOptions.Strict ) );

        byte[] expected = [ ..bytes1.ToArray(), ..bytes2.ToArray() ];

        Assert.Equal( FITSFile.BlockSize * 2, section.DataSize );
        Assert.Equal( expected, section.Data.ToArray() );
    }

    /// <summary>
    /// A data section accepts any block, with or without an existing one.
    /// </summary>
    [ Fact ]
    public void AppendData()
    {
        FITSBlock   block    = new FITSBlock( TestUtilities.DataBlock( 0xFF ), FITSParsingOptions.Strict );
        FITSSection section1 = new FITSSection( FITSSection.Kind.Data, block );
        FITSSection section2 = new FITSSection( FITSSection.Kind.Data, block: null );

        section1.Append( block );
        section2.Append( block );
    }

    /// <summary>
    /// A header section enforces the structural rules on appended blocks: no data
    /// after an <c>END</c> marker, ASCII only, and no mid-section extension marker.
    /// </summary>
    [ Fact ]
    public void AppendHeader()
    {
        FITSBlock   block1   = new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true,  keywords: [] ), FITSParsingOptions.Strict );
        FITSBlock   block2   = new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: false, keywords: [] ), FITSParsingOptions.Strict );
        FITSSection section1 = new FITSSection( FITSSection.Kind.Header, block1 );
        FITSSection section2 = new FITSSection( FITSSection.Kind.Header, block2 );
        FITSSection section3 = new FITSSection( FITSSection.Kind.Header, block: null );

        Assert.Throws< FITSException >( () => section1.Append( new FITSBlock( TestUtilities.DataBlock( 0x20 ), FITSParsingOptions.Strict ) ) );

        section2.Append( new FITSBlock( TestUtilities.DataBlock( 0x20 ), FITSParsingOptions.Strict ) );
        section3.Append( new FITSBlock( TestUtilities.DataBlock( 0x20 ), FITSParsingOptions.Strict ) );

        Assert.Throws< FITSException >( () => section2.Append( new FITSBlock( TestUtilities.DataBlock( 0xFF ), FITSParsingOptions.Strict ) ) );
        Assert.Throws< FITSException >( () => section3.Append( new FITSBlock( TestUtilities.DataBlock( 0xFF ), FITSParsingOptions.Strict ) ) );

        section2.Append( new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] ), FITSParsingOptions.Strict ) );
        section3.Append( new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] ), FITSParsingOptions.Strict ) );

        Assert.Throws< FITSException >( () => section2.Append( new FITSBlock( TestUtilities.DataBlock( 0x20 ), FITSParsingOptions.Strict ) ) );
        Assert.Throws< FITSException >( () => section3.Append( new FITSBlock( TestUtilities.DataBlock( 0x20 ), FITSParsingOptions.Strict ) ) );
    }

    /// <summary>
    /// An extension section enforces the same structural rules as a header: no
    /// data after an <c>END</c> marker, ASCII only, and no second extension marker.
    /// </summary>
    [ Fact ]
    public void AppendExtension()
    {
        FITSBlock   block1   = new FITSBlock( TestUtilities.StandardExtensionBlock( includeEndMarker: true,  keywords: [] ), FITSParsingOptions.Strict );
        FITSBlock   block2   = new FITSBlock( TestUtilities.StandardExtensionBlock( includeEndMarker: false, keywords: [] ), FITSParsingOptions.Strict );
        FITSSection section1 = new FITSSection( FITSSection.Kind.Extension, block1 );
        FITSSection section2 = new FITSSection( FITSSection.Kind.Extension, block2 );
        FITSSection section3 = new FITSSection( FITSSection.Kind.Extension, block: null );

        Assert.Throws< FITSException >( () => section1.Append( new FITSBlock( TestUtilities.DataBlock( 0x20 ), FITSParsingOptions.Strict ) ) );

        section2.Append( new FITSBlock( TestUtilities.DataBlock( 0x20 ), FITSParsingOptions.Strict ) );
        section3.Append( new FITSBlock( TestUtilities.DataBlock( 0x20 ), FITSParsingOptions.Strict ) );

        Assert.Throws< FITSException >( () => section2.Append( new FITSBlock( TestUtilities.DataBlock( 0xFF ), FITSParsingOptions.Strict ) ) );
        Assert.Throws< FITSException >( () => section3.Append( new FITSBlock( TestUtilities.DataBlock( 0xFF ), FITSParsingOptions.Strict ) ) );

        Assert.Throws< FITSException >( () => section2.Append( new FITSBlock( TestUtilities.StandardExtensionBlock( includeEndMarker: true, keywords: [] ), FITSParsingOptions.Strict ) ) );
        Assert.Throws< FITSException >( () => section3.Append( new FITSBlock( TestUtilities.StandardExtensionBlock( includeEndMarker: true, keywords: [] ), FITSParsingOptions.Strict ) ) );

        section2.Append( new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] ), FITSParsingOptions.Strict ) );
        section3.Append( new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] ), FITSParsingOptions.Strict ) );

        Assert.Throws< FITSException >( () => section2.Append( new FITSBlock( TestUtilities.DataBlock( 0x20 ), FITSParsingOptions.Strict ) ) );
        Assert.Throws< FITSException >( () => section3.Append( new FITSBlock( TestUtilities.DataBlock( 0x20 ), FITSParsingOptions.Strict ) ) );
    }

    /// <summary>
    /// Consecutive <c>HISTORY</c> records are merged into one property when the
    /// merge option is enabled.
    /// </summary>
    [ Fact ]
    public void MergeHistory()
    {
        ( string Name, string Value )[] keywords = [ ( "HISTORY", "hello" ), ( "HISTORY", "world" ) ];
        FITSSection                     section  = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: keywords ), FITSParsingOptions.Strict ) );

        section.FinalizeSection( FITSParsingOptions.Lenient );

        FITSProperty? property = section.Properties.FirstOrDefault( property => property.Name == "HISTORY" );

        Assert.NotNull( property );
        Assert.Equal( "hello\nworld", property.Comment );
    }

    /// <summary>
    /// Consecutive <c>HISTORY</c> records stay separate when merging is disabled.
    /// </summary>
    [ Fact ]
    public void MergeHistoryDisabled()
    {
        ( string Name, string Value )[] keywords = [ ( "HISTORY", "hello" ), ( "HISTORY", "world" ) ];
        FITSSection                     section  = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: keywords ), FITSParsingOptions.Strict ) );

        section.FinalizeSection( FITSParsingOptions.None );

        List< FITSProperty > properties = section.Properties.Where( property => property.Name == "HISTORY" ).ToList();

        Assert.Equal( 2, properties.Count );
        Assert.Equal( "hello", properties[ 0 ].Comment );
        Assert.Equal( "world", properties[ 1 ].Comment );
    }

    /// <summary>
    /// Consecutive <c>COMMENT</c> records are merged into one property when the
    /// merge option is enabled.
    /// </summary>
    [ Fact ]
    public void MergeComment()
    {
        ( string Name, string Value )[] keywords = [ ( "COMMENT", "hello" ), ( "COMMENT", "world" ) ];
        FITSSection                     section  = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: keywords ), FITSParsingOptions.Strict ) );

        section.FinalizeSection( FITSParsingOptions.Lenient );

        FITSProperty? property = section.Properties.FirstOrDefault( property => property.Name == "COMMENT" );

        Assert.NotNull( property );
        Assert.Equal( "hello\nworld", property.Comment );
    }

    /// <summary>
    /// Consecutive <c>COMMENT</c> records stay separate when merging is disabled.
    /// </summary>
    [ Fact ]
    public void MergeCommentDisabled()
    {
        ( string Name, string Value )[] keywords = [ ( "COMMENT", "hello" ), ( "COMMENT", "world" ) ];
        FITSSection                     section  = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: keywords ), FITSParsingOptions.Strict ) );

        section.FinalizeSection( FITSParsingOptions.None );

        List< FITSProperty > properties = section.Properties.Where( property => property.Name == "COMMENT" ).ToList();

        Assert.Equal( 2, properties.Count );
        Assert.Equal( "hello", properties[ 0 ].Comment );
        Assert.Equal( "world", properties[ 1 ].Comment );
    }

    /// <summary>
    /// A long string split across a <c>CONTINUE</c> record is reassembled when the
    /// merge option is enabled.
    /// </summary>
    [ Fact ]
    public void MergeString()
    {
        ( string Name, string Value )[] keywords = [ ( "FOOBAR", "'hello&'" ), ( "CONTINUE", "', world'" ) ];
        FITSSection                     section  = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: keywords ), FITSParsingOptions.Strict ) );

        section.FinalizeSection( FITSParsingOptions.MergeStringProperties );

        FITSProperty? property = section.Properties.FirstOrDefault( property => property.Name == "FOOBAR" );

        Assert.NotNull( property );
        Assert.Equal( "hello, world", property.Value.AsString );
    }

    /// <summary>
    /// A <c>CONTINUE</c> record stays a separate property when string merging is
    /// disabled.
    /// </summary>
    [ Fact ]
    public void MergeStringDisabled()
    {
        ( string Name, string Value )[] keywords = [ ( "FOOBAR", "'hello&'" ), ( "CONTINUE", "', world'" ) ];
        FITSSection                     section  = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: keywords ), FITSParsingOptions.Strict ) );

        section.FinalizeSection( FITSParsingOptions.None );

        FITSProperty? property1 = section.Properties.FirstOrDefault( property => property.Name == "FOOBAR" );
        FITSProperty? property2 = section.Properties.FirstOrDefault( property => property.Name == "CONTINUE" );

        Assert.NotNull( property1 );
        Assert.NotNull( property2 );

        Assert.Equal( "hello&",   property1.Value.AsString );
        Assert.Equal( ", world",  property2.Value.AsString );
    }

    /// <summary>
    /// A <c>CONTINUE</c> that cannot be merged - because its predecessor is not a
    /// continued string, or because it has no predecessor - fails in strict mode.
    /// </summary>
    [ Fact ]
    public void MergeStringFail()
    {
        string[]    fields1  = [ "FOOBAR  = 'hello'", "CONTINUE  ', world'", "END" ];
        string[]    fields2  = [ "CONTINUE  ', world'", "END" ];
        FITSSection section1 = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.HeaderBlock( fields1 ), FITSParsingOptions.Strict ) );
        FITSSection section2 = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.HeaderBlock( fields2 ), FITSParsingOptions.Strict ) );

        Assert.Throws< FITSException >( () => section1.FinalizeSection( FITSParsingOptions.Strict ) );
        Assert.Throws< FITSException >( () => section2.FinalizeSection( FITSParsingOptions.Strict ) );
    }

    /// <summary>
    /// An orphaned <c>CONTINUE</c> - after a non-continued string, or with no
    /// predecessor at all - is kept as a standalone property when
    /// <see cref="FITSParsingOptions.AllowOrphanedContinue"/> is set.
    /// </summary>
    [ Fact ]
    public void OrphanedContinueIsStandaloneWithFlag()
    {
        string[]           fields1  = [ "FOOBAR  = 'hello'", "CONTINUE  ', world'", "END" ];
        string[]           fields2  = [ "CONTINUE  ', world'", "END" ];
        FITSSection        section1 = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.HeaderBlock( fields1 ), FITSParsingOptions.Strict ) );
        FITSSection        section2 = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.HeaderBlock( fields2 ), FITSParsingOptions.Strict ) );
        FITSParsingOptions options  = FITSParsingOptions.MergeStringProperties | FITSParsingOptions.AllowOrphanedContinue;

        section1.FinalizeSection( options );
        section2.FinalizeSection( options );

        Assert.Equal( "hello",   section1.Properties.FirstOrDefault( property => property.Name == "FOOBAR"   )?.Value.AsString );
        Assert.Equal( ", world", section1.Properties.FirstOrDefault( property => property.Name == "CONTINUE" )?.Value.AsString );
        Assert.Equal( ", world", section2.Properties.FirstOrDefault( property => property.Name == "CONTINUE" )?.Value.AsString );
    }

    /// <summary>
    /// A record whose value matches no known FITS type is rejected when unknown
    /// properties are not allowed.
    /// </summary>
    [ Fact ]
    public void UnknownPropertiesDisabled()
    {
        FITSSection section = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [ ( "FOOBAR", "a" ) ] ), FITSParsingOptions.Strict ) );

        Assert.Throws< FITSException >( () => section.FinalizeSection( FITSParsingOptions.None ) );
    }

    /// <summary>
    /// Header text containing a non-printable control byte is rejected in strict
    /// mode.
    /// </summary>
    [ Fact ]
    public void HeaderWithNonPrintableByteIsRejectedWhenStrict()
    {
        FITSSection section = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [ ( "COMMENT", "\u0001hi" ) ] ), FITSParsingOptions.Strict ) );

        Assert.Throws< FITSException >( () => section.FinalizeSection( FITSParsingOptions.Strict ) );
    }

    /// <summary>
    /// A non-printable control byte in header text is tolerated in lenient mode,
    /// and the record is still parsed.
    /// </summary>
    [ Fact ]
    public void HeaderWithNonPrintableByteIsToleratedWhenLenient()
    {
        FITSSection section = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [ ( "COMMENT", "\u0001hi" ) ] ), FITSParsingOptions.Strict ) );

        section.FinalizeSection( FITSParsingOptions.Lenient );

        Assert.Equal( "\u0001hi", section.Properties.FirstOrDefault( property => property.Name == "COMMENT" )?.Comment );
    }

    /// <summary>
    /// Finalizing an already-finalized section throws.
    /// </summary>
    [ Fact ]
    public void FinalizeTwiceThrows()
    {
        FITSSection section = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] ), FITSParsingOptions.Strict ) );

        section.FinalizeSection( FITSParsingOptions.Lenient );

        Assert.Throws< FITSException >( () => section.FinalizeSection( FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// Appending a block to an already-finalized section throws.
    /// </summary>
    [ Fact ]
    public void AppendAfterFinalizeThrows()
    {
        FITSSection section = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] ), FITSParsingOptions.Strict ) );

        section.FinalizeSection( FITSParsingOptions.Lenient );

        Assert.Throws< FITSException >( () => section.Append( new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: false, keywords: [] ), FITSParsingOptions.Strict ) ) );
    }

    /// <summary>
    /// The textual summary is non-empty and differs from the default type-name
    /// representation.
    /// </summary>
    [ Fact ]
    public void ToStringSummarizesTheSection()
    {
        FITSSection section = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] ), FITSParsingOptions.Strict ) );

        Assert.NotEmpty( section.ToString() );
        Assert.NotEqual( typeof( FITSSection ).ToString(), section.ToString() );
    }

    /// <summary>
    /// The textual summary reports the section's retained data size, updated as
    /// blocks are appended.
    /// </summary>
    [ Fact ]
    public void ToStringReportsDataSize()
    {
        FITSSection section = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: false, keywords: [] ), FITSParsingOptions.Strict ) );

        Assert.Contains( $"Data Size:  { FITSFile.BlockSize.ToString( CultureInfo.InvariantCulture ) }", section.ToString() );

        section.Append( new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] ), FITSParsingOptions.Strict ) );

        Assert.Contains( $"Data Size:  { ( FITSFile.BlockSize * 2 ).ToString( CultureInfo.InvariantCulture ) }", section.ToString() );
    }

    /// <summary>
    /// A header with more than one <c>END</c> marker is rejected.
    /// </summary>
    [ Fact ]
    public void MultipleEndMarkers()
    {
        FITSSection section = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [ ( "END", "" ) ] ), FITSParsingOptions.Strict ) );

        Assert.Throws< FITSException >( () => section.FinalizeSection( FITSParsingOptions.None ) );
    }

    /// <summary>
    /// A header with no <c>END</c> marker is rejected.
    /// </summary>
    [ Fact ]
    public void NoEndMarker()
    {
        FITSSection section = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: false, keywords: [] ), FITSParsingOptions.Strict ) );

        Assert.Throws< FITSException >( () => section.FinalizeSection( FITSParsingOptions.None ) );
    }

    /// <summary>
    /// Blank records immediately before the <c>END</c> marker are trimmed, while a
    /// blank record between non-blank records is kept.
    /// </summary>
    [ Fact ]
    public void RemoveEmptyPropertiesAtEnd()
    {
        string[] fields  = [ "SIMPLE  = T", "BITPIX  = 8", "NAXIS   = 0", "           ", "FOOBAR  = 1", "           ", "           " ];
        string[] fields1 = [ ..fields[ ..^2 ], "END" ];
        string[] fields2 = [ ..fields,        "END" ];

        Assert.Equal( 6, fields1.Length );
        Assert.Equal( 8, fields2.Length );

        FITSSection section1 = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.HeaderBlock( fields1 ), FITSParsingOptions.Strict ) );
        FITSSection section2 = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.HeaderBlock( fields2 ), FITSParsingOptions.Strict ) );

        section1.FinalizeSection( FITSParsingOptions.Lenient );
        section2.FinalizeSection( FITSParsingOptions.Lenient );

        Assert.Equal( 5, section1.Properties.Count );
        Assert.Equal( 5, section2.Properties.Count );
    }

    /// <summary>
    /// A header of only blank records before <c>END</c> trims to no properties.
    /// </summary>
    [ Fact ]
    public void AllBlankHeaderTrimsToEmptyProperties()
    {
        string[]    fields  = [ "           ", "           ", "END" ];
        FITSSection section = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.HeaderBlock( fields ), FITSParsingOptions.Strict ) );

        section.FinalizeSection( FITSParsingOptions.Strict );

        Assert.Empty( section.Properties );
    }

    /// <summary>
    /// A non-blank record following the <c>END</c> marker is rejected in strict
    /// mode.
    /// </summary>
    [ Fact ]
    public void NonBlankContentAfterEndIsRejectedWhenStrict()
    {
        string[]    fields  = [ "SIMPLE  = T", "BITPIX  = 8", "NAXIS   = 0", "END", "FOOBAR  = 1" ];
        FITSSection section = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.HeaderBlock( fields ), FITSParsingOptions.Strict ) );

        Assert.Throws< FITSException >( () => section.FinalizeSection( FITSParsingOptions.Strict ) );
    }

    /// <summary>
    /// A non-blank record following the <c>END</c> marker is dropped from the
    /// properties but its bytes survive in the data when
    /// <see cref="FITSParsingOptions.AllowContentAfterEnd"/> is set.
    /// </summary>
    [ Fact ]
    public void NonBlankContentAfterEndIsToleratedWhenLenient()
    {
        string[]    fields  = [ "SIMPLE  = T", "BITPIX  = 8", "NAXIS   = 0", "END", "FOOBAR  = 1" ];
        FITSBlock   block   = new FITSBlock( TestUtilities.HeaderBlock( fields ), FITSParsingOptions.Lenient );
        FITSSection section = new FITSSection( FITSSection.Kind.Header, block );

        section.FinalizeSection( FITSParsingOptions.Lenient );

        Assert.Equal( 3, section.Properties.Count );
        Assert.DoesNotContain( section.Properties, property => property.Name == "FOOBAR" );
        Assert.Equal( block.Data.ToArray(), section.Data.ToArray() );
    }

    /// <summary>
    /// Blank padding after the <c>END</c> marker is accepted regardless of mode,
    /// with the trailing blanks trimmed from the properties.
    /// </summary>
    [ Fact ]
    public void BlankContentAfterEndIsAcceptedInBothModes()
    {
        string[] fields = [ "SIMPLE  = T", "BITPIX  = 8", "NAXIS   = 0", "END", "           ", "           " ];

        foreach( FITSParsingOptions options in new[] { FITSParsingOptions.Strict, FITSParsingOptions.Lenient } )
        {
            FITSSection section = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.HeaderBlock( fields ), options ) );

            section.FinalizeSection( options );

            Assert.Equal( 3, section.Properties.Count );
        }
    }

    /// <summary>
    /// A data section built from a raw payload renders it zero-padded to a whole
    /// block, preserving the original bytes.
    /// </summary>
    [ Fact ]
    public void SyntheticDataSectionPadsToBlockBoundaryWithZeros()
    {
        byte[] payload = new byte[ 100 ];

        Array.Fill( payload, ( byte )0xAB );

        FITSSection            section  = new FITSSection( payload );
        ReadOnlyMemory< byte > rendered = section.SerializedData( FITSSerializationOptions.Strict );

        Assert.Equal( FITSFile.BlockSize, rendered.Length );
        Assert.Equal( payload, rendered.Slice( 0, 100 ).ToArray() );
        Assert.All( rendered.Slice( 100 ).ToArray(), value => Assert.Equal( ( byte )0x00, value ) );
    }

    /// <summary>
    /// Building a section from properties rejects the data kind and the reserved
    /// <c>END</c> keyword.
    /// </summary>
    [ Fact ]
    public void BuildFromPropertiesRejectsDataKindAndEndKeyword()
    {
        FITSProperty simple = new FITSProperty( "SIMPLE", true,                 FITSSerializationOptions.Strict );
        FITSProperty end    = new FITSProperty( "END",    FITSValue.Undefined,  FITSSerializationOptions.Strict );

        Assert.Throws< FITSException >( () => new FITSSection( FITSSection.Kind.Data,   new List< FITSProperty > { simple } ) );
        Assert.Throws< FITSException >( () => new FITSSection( FITSSection.Kind.Header, new List< FITSProperty > { end } ) );
    }

    /// <summary>
    /// Every property mutation entry point rejects an invalid target: the reserved
    /// <c>END</c> keyword, property mutation on a data section, a payload on a
    /// header, and an out-of-range insert index.
    /// </summary>
    [ Fact ]
    public void MutationMethodsRejectInvalidTargets()
    {
        FITSSection  header         = new FITSSection( FITSSection.Kind.Header, new List< FITSProperty > { new FITSProperty( "SIMPLE", true, FITSSerializationOptions.Strict ) } );
        FITSSection  data           = new FITSSection( new byte[ 10 ] );
        FITSProperty end            = new FITSProperty( "END",    FITSValue.Undefined, FITSSerializationOptions.Strict );
        FITSProperty objectProperty = new FITSProperty( "OBJECT", "M42",              FITSSerializationOptions.Strict );

        Assert.Throws< FITSException >( () => header.Append( end ) );
        Assert.Throws< FITSException >( () => header.Insert( end, 0 ) );
        Assert.Throws< FITSException >( () => header.SetProperty( end ) );

        Assert.Throws< FITSException >( () => data.Append( objectProperty ) );
        Assert.Throws< FITSException >( () => data.RemoveProperties( "OBJECT" ) );
        Assert.Throws< FITSException >( () => header.SetDataPayload( ReadOnlyMemory< byte >.Empty ) );

        Assert.Throws< FITSException >( () => header.Insert( objectProperty, 99 ) );
    }

    /// <summary>
    /// Replacing a data section's payload marks it dirty and renders the new
    /// payload, zero-padded to the block boundary.
    /// </summary>
    [ Fact ]
    public void SetsDataPayloadAndRendersIt()
    {
        byte[] initial = new byte[ 10 ];
        byte[] payload = new byte[ 100 ];

        Array.Fill( initial, ( byte )0x01 );
        Array.Fill( payload, ( byte )0xAB );

        FITSSection section = new FITSSection( initial );

        section.SetDataPayload( payload );

        ReadOnlyMemory< byte > rendered = section.SerializedData( FITSSerializationOptions.Strict );

        Assert.Equal( FITSFile.BlockSize, rendered.Length );
        Assert.Equal( payload, rendered.Slice( 0, 100 ).ToArray() );
        Assert.All( rendered.Slice( 100 ).ToArray(), value => Assert.Equal( ( byte )0x00, value ) );
    }

    /// <summary>
    /// A property has a single owner: once it belongs to a section, adding it to
    /// another throws, while re-setting it on its own section is allowed and moving
    /// it after removal is allowed.
    /// </summary>
    [ Fact ]
    public void AddingAnOwnedPropertyToAnotherSectionThrows()
    {
        FITSProperty property = new FITSProperty( "OBJECT", "M42", FITSSerializationOptions.Strict );
        FITSSection  a        = new FITSSection( FITSSection.Kind.Header, new List< FITSProperty > { property } );
        FITSSection  b        = new FITSSection( FITSSection.Kind.Header, new List< FITSProperty >() );

        a.SetProperty( property );

        Assert.Throws< FITSException >( () => b.Append( property ) );
        Assert.Throws< FITSException >( () => b.Insert( property, 0 ) );
        Assert.Throws< FITSException >( () => b.SetProperty( property ) );
        Assert.Throws< FITSException >( () => new FITSSection( FITSSection.Kind.Header, new List< FITSProperty > { property } ) );

        a.RemoveProperties( "OBJECT" );

        b.Append( property );
    }

    /// <summary>
    /// The keyword lookup returns the first property with a matching name, or
    /// <c>null</c> when none matches.
    /// </summary>
    /// <remarks>
    /// A section-level port of the file-layer keyword-subscript test, deferred to
    /// the file reader in its <c>FITSFile</c>-based form.
    /// </remarks>
    [ Fact ]
    public void KeywordSubscriptReturnsFirstMatch()
    {
        ( string Name, string Value )[] keywords =
        [
            ( "SIMPLE", "T" ), ( "BITPIX", "8" ), ( "NAXIS", "0" ),
            ( "FOO", "1" ), ( "FOO", "2" ), ( "END", "" ),
        ];
        FITSSection header = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.HeaderBlock( keywords ), FITSParsingOptions.Lenient ) );

        header.FinalizeSection( FITSParsingOptions.Lenient );

        Assert.Equal( 1L, header[ "FOO" ]?.Value.AsInteger );
        Assert.Null( header[ "MISSING" ] );
    }

    /// <summary>
    /// The typed geometry accessors return the parsed <c>BITPIX</c>, <c>NAXIS</c>
    /// and <c>NAXISn</c> values of a header.
    /// </summary>
    /// <remarks>
    /// A section-level port of the file-layer geometry-accessor test, deferred to
    /// the file reader in its <c>FITSFile</c>-based form.
    /// </remarks>
    [ Fact ]
    public void TypedGeometryAccessorsReturnParsedValues()
    {
        ( string Name, string Value )[] keywords =
        [
            ( "SIMPLE", "T" ), ( "BITPIX", "16" ), ( "NAXIS", "2" ),
            ( "NAXIS1", "100" ), ( "NAXIS2", "200" ), ( "END", "" ),
        ];
        FITSSection header = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.HeaderBlock( keywords ), FITSParsingOptions.Lenient ) );

        header.FinalizeSection( FITSParsingOptions.Lenient );

        Assert.Equal( 16L,  header.Bitpix );
        Assert.Equal( 2L,   header.Naxis );
        Assert.Equal( 100L, header.NaxisAt( 1 ) );
        Assert.Equal( 200L, header.NaxisAt( 2 ) );
        Assert.Null( header.NaxisAt( 3 ) );
    }

    /// <summary>
    /// A data section carries no parsed properties, so every geometry accessor and
    /// the keyword lookup are <c>null</c>.
    /// </summary>
    /// <remarks>
    /// A section-level port of the file-layer test, deferred to the file reader in
    /// its <c>FITSFile</c>-based form.
    /// </remarks>
    [ Fact ]
    public void TypedGeometryAccessorsAreNilOnDataSection()
    {
        FITSSection segment = new FITSSection( FITSSection.Kind.Data, new FITSBlock( TestUtilities.DataBlock( 0x00 ), FITSParsingOptions.Strict ) );

        Assert.Null( segment.Bitpix );
        Assert.Null( segment.Naxis );
        Assert.Null( segment.NaxisAt( 1 ) );
        Assert.Null( segment[ "BITPIX" ] );
    }

    /// <summary>
    /// A clean (unmodified) parsed section re-serializes to its retained bytes
    /// byte-for-byte, whatever the options.
    /// </summary>
    /// <remarks>
    /// A section-level port of the file-layer test, deferred to the file reader in
    /// its <c>FITSFile</c>-based form.
    /// </remarks>
    [ Fact ]
    public void CleanSectionSerializesToRetainedBytes()
    {
        ReadOnlyMemory< byte > bytes  = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [ ( "FOO", "42" ) ] );
        FITSSection            header = new FITSSection( FITSSection.Kind.Header, new FITSBlock( bytes, FITSParsingOptions.Strict ) );

        header.FinalizeSection( FITSParsingOptions.Strict );

        Assert.Equal( bytes.ToArray(), header.SerializedData( FITSSerializationOptions.Strict ).ToArray() );
        Assert.Equal( bytes.ToArray(), header.SerializedData( FITSSerializationOptions.Lenient ).ToArray() );
        Assert.Equal( bytes.ToArray(), header.Data.ToArray() );
    }

    /// <summary>
    /// A dirtied header renders from its properties (not its retained bytes): a
    /// whole number of blocks that re-parse to an equal model.
    /// </summary>
    /// <remarks>
    /// A section-level port of the file-layer test, deferred to the file reader in
    /// its <c>FITSFile</c>-based form; the rendered bytes are re-parsed through a
    /// fresh <see cref="FITSSection"/> rather than the file reader.
    /// </remarks>
    [ Fact ]
    public void DirtiedHeaderSectionRendersFromModelAndReparsesEqual()
    {
        ReadOnlyMemory< byte > bytes  = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [ ( "FOOBAR", "42" ), ( "OBJECT", "'M42'" ) ] );
        FITSSection            header = new FITSSection( FITSSection.Kind.Header, new FITSBlock( bytes, FITSParsingOptions.Strict ) );

        header.FinalizeSection( FITSParsingOptions.Strict );
        header.MarkNeedsSerialization();

        ReadOnlyMemory< byte > rendered = header.SerializedData( FITSSerializationOptions.Strict );

        Assert.Equal( 0, rendered.Length % FITSFile.BlockSize );

        FITSSection reparsed = new FITSSection( FITSSection.Kind.Header, new FITSBlock( rendered, FITSParsingOptions.Strict ) );

        reparsed.FinalizeSection( FITSParsingOptions.Strict );

        Assert.Equal( header.Properties.Count, reparsed.Properties.Count );

        foreach( ( FITSProperty original, FITSProperty roundtripped ) in header.Properties.Zip( reparsed.Properties ) )
        {
            Assert.Equal( original.Name,    roundtripped.Name );
            Assert.Equal( original.Value,   roundtripped.Value );
            Assert.Equal( original.Comment, roundtripped.Comment );
        }
    }

    /// <summary>
    /// A header built from a model is dirty, so it renders from its properties (the
    /// library appends <c>END</c> and pads to the block boundary) and re-parses to
    /// an equal model.
    /// </summary>
    /// <remarks>
    /// A section-level port of the file-layer test, deferred to the file reader in
    /// its <c>FITSFile</c>-based form.
    /// </remarks>
    [ Fact ]
    public void BuildsHeaderFromPropertiesAndRoundTrips()
    {
        List< FITSProperty > properties =
        [
            new FITSProperty( "SIMPLE", true, FITSSerializationOptions.Strict ),
            new FITSProperty( "BITPIX", 8L,   FITSSerializationOptions.Strict ),
            new FITSProperty( "NAXIS",  0L,   FITSSerializationOptions.Strict ),
            new FITSProperty( "OBJECT", "M42", FITSSerializationOptions.Strict ),
        ];
        FITSSection            section  = new FITSSection( FITSSection.Kind.Header, properties );
        ReadOnlyMemory< byte > rendered = section.SerializedData( FITSSerializationOptions.Strict );

        Assert.Equal( FITSSection.Kind.Header, section.SectionKind );
        Assert.Equal( 0, rendered.Length % FITSFile.BlockSize );

        FITSSection reparsed = new FITSSection( FITSSection.Kind.Header, new FITSBlock( rendered, FITSParsingOptions.Strict ) );

        reparsed.FinalizeSection( FITSParsingOptions.Strict );

        Assert.Equal( properties.Count, reparsed.Properties.Count );

        foreach( ( FITSProperty original, FITSProperty roundtripped ) in properties.Zip( reparsed.Properties ) )
        {
            Assert.Equal( original.Name,  roundtripped.Name );
            Assert.Equal( original.Value, roundtripped.Value );
        }
    }

    /// <summary>
    /// Appending, inserting, replacing and removing properties is reflected in the
    /// section's model and in its serialization.
    /// </summary>
    /// <remarks>
    /// A section-level port of the file-layer test, deferred to the file reader in
    /// its <c>FITSFile</c>-based form.
    /// </remarks>
    [ Fact ]
    public void MutatesPropertiesAndReflectsInSerialization()
    {
        FITSSection section = new FITSSection(
            FITSSection.Kind.Header,
            new List< FITSProperty >
            {
                new FITSProperty( "SIMPLE", true, FITSSerializationOptions.Strict ),
                new FITSProperty( "BITPIX", 8L,   FITSSerializationOptions.Strict ),
                new FITSProperty( "NAXIS",  0L,   FITSSerializationOptions.Strict ),
            }
        );

        section.Append( new FITSProperty( "OBJECT",   "M42",     FITSSerializationOptions.Strict ) );
        section.Insert( new FITSProperty( "TELESCOP", "VLT",     FITSSerializationOptions.Strict ), 3 );
        section.SetProperty( new FITSProperty( "OBJECT", "NGC 224", FITSSerializationOptions.Strict ) );
        section.RemoveProperties( "TELESCOP" );

        string[] names = section.Properties.Select( property => property.Name ).ToArray();

        Assert.Equal( new[] { "SIMPLE", "BITPIX", "NAXIS", "OBJECT" }, names );
        Assert.Equal( FITSValue.String( "NGC 224" ), section.Properties.FirstOrDefault( property => property.Name == "OBJECT" )?.Value );

        ReadOnlyMemory< byte > rendered = section.SerializedData( FITSSerializationOptions.Strict );
        FITSSection            reparsed = new FITSSection( FITSSection.Kind.Header, new FITSBlock( rendered, FITSParsingOptions.Strict ) );

        reparsed.FinalizeSection( FITSParsingOptions.Strict );

        Assert.Equal( FITSValue.String( "NGC 224" ), reparsed.Properties.FirstOrDefault( property => property.Name == "OBJECT" )?.Value );
        Assert.DoesNotContain( reparsed.Properties, property => property.Name == "TELESCOP" );
    }

    /// <summary>
    /// A property edited in place marks its owning section dirty through the
    /// back-reference, so the section re-renders from the model instead of
    /// re-emitting its now-stale retained bytes.
    /// </summary>
    /// <remarks>
    /// A section-level port of the file-layer test, deferred to the file reader in
    /// its <c>FITSFile</c>-based form.
    /// </remarks>
    [ Fact ]
    public void EditingParsedPropertyInPlaceMarksSectionDirty()
    {
        ReadOnlyMemory< byte > bytes  = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [ ( "OBJECT", "'M42'" ), ( "COMMENT", "old" ) ] );
        FITSSection            header = new FITSSection( FITSSection.Kind.Header, new FITSBlock( bytes, FITSParsingOptions.Strict ) );

        header.FinalizeSection( FITSParsingOptions.Strict );

        FITSProperty? objectProperty = header.Properties.FirstOrDefault( property => property.Name == "OBJECT" );
        FITSProperty? note           = header.Properties.FirstOrDefault( property => property.Name == "COMMENT" );

        Assert.NotNull( objectProperty );
        Assert.NotNull( note );

        objectProperty.Value = FITSValue.String( "NGC 224" );
        note.Comment         = "new";

        ReadOnlyMemory< byte > rendered = header.SerializedData( FITSSerializationOptions.Strict );
        FITSSection            reparsed = new FITSSection( FITSSection.Kind.Header, new FITSBlock( rendered, FITSParsingOptions.Strict ) );

        reparsed.FinalizeSection( FITSParsingOptions.Strict );

        Assert.Equal( FITSValue.String( "NGC 224" ), reparsed.Properties.FirstOrDefault( property => property.Name == "OBJECT" )?.Value );
        Assert.Equal( "new", reparsed.Properties.FirstOrDefault( property => property.Name == "COMMENT" )?.Comment );
    }

    /// <summary>
    /// Reassigning value or comment to an equal one must not mark a clean parsed
    /// section dirty, so it keeps re-emitting its retained bytes byte-for-byte.
    /// </summary>
    /// <remarks>
    /// A section-level port of the file-layer test, deferred to the file reader in
    /// its <c>FITSFile</c>-based form.
    /// </remarks>
    [ Fact ]
    public void IdempotentPropertyReassignmentKeepsSectionClean()
    {
        ReadOnlyMemory< byte > bytes  = TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [ ( "OBJECT", "'M42'" ), ( "COMMENT", "note" ) ] );
        FITSSection            header = new FITSSection( FITSSection.Kind.Header, new FITSBlock( bytes, FITSParsingOptions.Strict ) );

        header.FinalizeSection( FITSParsingOptions.Strict );

        foreach( FITSProperty property in header.Properties )
        {
            property.Value   = property.Value;
            property.Comment = property.Comment;
        }

        Assert.Equal( bytes.ToArray(), header.SerializedData( FITSSerializationOptions.Strict ).ToArray() );
    }

    /// <summary>
    /// A padding block is retained without the header structural checks, so blank
    /// end-of-file padding round-trips through the data even after an <c>END</c>
    /// marker that would otherwise reject a further block.
    /// </summary>
    [ Fact ]
    public void AppendPaddingBypassesStructuralRules()
    {
        FITSSection header = new FITSSection( FITSSection.Kind.Header, new FITSBlock( TestUtilities.StandardHeaderBlock( includeEndMarker: true, keywords: [] ), FITSParsingOptions.Strict ) );

        Assert.Throws< FITSException >( () => header.Append( new FITSBlock( TestUtilities.DataBlock( 0x20 ), FITSParsingOptions.Strict ) ) );

        header.AppendPadding( new FITSBlock( TestUtilities.DataBlock( 0x20 ), FITSParsingOptions.Strict ) );

        Assert.Equal( FITSFile.BlockSize * 2, header.DataSize );
    }

    /// <summary>
    /// The serialized byte count of a data section pending serialization is its
    /// payload padded to the next block boundary, while a clean section reports its
    /// retained data size.
    /// </summary>
    [ Fact ]
    public void SerializedByteCountReflectsPaddedPayload()
    {
        FITSSection dirty = new FITSSection( new byte[ 100 ] );
        FITSSection clean = new FITSSection( FITSSection.Kind.Data, new FITSBlock( TestUtilities.DataBlock( 0x00 ), FITSParsingOptions.Strict ) );

        Assert.Equal( FITSFile.BlockSize, dirty.SerializedByteCount );
        Assert.Equal( FITSFile.BlockSize, clean.SerializedByteCount );
    }
}
