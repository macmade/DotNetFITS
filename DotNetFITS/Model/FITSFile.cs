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

namespace DotNetFITS;

/// <summary>
/// A parsed FITS (Flexible Image Transport System) file.
/// </summary>
/// <remarks>
/// Parsing splits the input into fixed-size blocks, groups them into
/// <see cref="FITSSection"/> units (a primary header, optional extensions, and
/// their data segments) by following the declared header geometry, then validates
/// the mandatory keywords and the data-segment sizes.
/// <see cref="FITSParsingOptions"/> controls how strictly noncompliant input is
/// treated. The type also defines the FITS size constants fixed by the standard.
/// From-scratch construction and serialization are added by the write layer.
/// <para>
/// A file holds mutable section state and composes <see cref="FITSBlock"/>, whose
/// flags cache lazily on read, so even concurrent reads of a fully-parsed file
/// race: it is not thread-safe.
/// </para>
/// </remarks>
public class FITSFile
{
    /// <summary>
    /// The size, in bytes, of a single FITS block. Fixed by the standard at
    /// 2880.
    /// </summary>
    public const int BlockSize = 2880;

    /// <summary>
    /// The size, in bytes, of a single FITS header record (card). Fixed by the
    /// standard at 80.
    /// </summary>
    public const int CardSize = 80;

    /// <summary>
    /// The length, in bytes, of the keyword-name field at the start of a header
    /// record. Fixed by the standard at 8.
    /// </summary>
    public const int KeywordLength = 8;

    /// <summary>
    /// The width, in bytes, of the fixed-format value field (bytes 11-30), in
    /// which scalar values are right-justified per FITS 4.0 section 4.2.
    /// </summary>
    public const int FixedValueFieldWidth = 20;

    /// <summary>
    /// An upper bound, in bytes, on a single data segment.
    /// </summary>
    /// <remarks>
    /// A geometry implying a larger segment is rejected as corrupt rather than
    /// yielding a meaningless multi-exabyte expected size. The ceiling sits far
    /// above any real FITS file (approximately 9 PB) yet safely within a signed
    /// 64-bit integer, so the size math can never overflow once a value passes
    /// it.
    /// </remarks>
    public const long MaxDataSize = 1L << 53;

    /// <summary>
    /// The <c>BITPIX</c> values the FITS standard permits.
    /// </summary>
    private static readonly long[] ValidBitpixValues = [ 8, 16, 32, 64, -32, -64 ];

    /// <summary>The mutable backing store of <see cref="Sections"/>.</summary>
    private List< FITSSection > SectionList { get; set; }

    /// <summary>
    /// The file's sections, in file order. The first is always the primary header.
    /// </summary>
    /// <remarks>
    /// The returned list is a snapshot copy, so mutating it does not change the
    /// file's sections.
    /// </remarks>
    public IReadOnlyList< FITSSection > Sections => this.SectionList.ToList();

    /// <summary>The primary header section, or <c>null</c> if the file has no sections.</summary>
    public FITSSection? Header => this.SectionList.FirstOrDefault();

    /// <summary>The extension sections, in file order.</summary>
    public IReadOnlyList< FITSSection > Extensions => this.SectionList.Where( section => section.SectionKind == FITSSection.Kind.Extension ).ToList();

    /// <summary>
    /// Reads and parses a FITS file from a file path.
    /// </summary>
    /// <param name="path">The location of the file to read.</param>
    /// <param name="options">The parsing options to apply.</param>
    /// <exception cref="FITSException">
    /// The path is missing or a directory
    /// (<see cref="FITSErrorKind.InvalidFileURL"/>), the contents cannot be read
    /// (<see cref="FITSErrorKind.CannotReadFile"/>), or any error raised while
    /// parsing the data.
    /// </exception>
    public FITSFile( string path, FITSParsingOptions options ) : this( ReadFileBytes( path ), options )
    {}

    /// <summary>
    /// Parses a FITS file from raw bytes.
    /// </summary>
    /// <remarks>
    /// Chunks the data into <see cref="BlockSize"/>-byte blocks, groups them into
    /// sections, finalizes each section, then validates that the first section is a
    /// primary header, that mandatory keywords are present and well-formed in every
    /// header and extension, and that each data segment's length matches the size
    /// implied by its header geometry.
    /// </remarks>
    /// <param name="data">The complete file contents.</param>
    /// <param name="options">The parsing options to apply.</param>
    /// <exception cref="FITSException">
    /// The data is empty, has a structural or validation failure, or raises any
    /// other error while parsing blocks and sections.
    /// </exception>
    public FITSFile( ReadOnlyMemory< byte > data, FITSParsingOptions options )
    {
        if( data.IsEmpty )
        {
            throw FITSException.DataError( "Data is empty" );
        }

        ReadOnlyMemory< byte > bytes     = data;
        int                    remainder = bytes.Length % FITSFile.BlockSize;

        // A trailing partial block would fail the even-division chunking, which
        // rejects the whole file before any leniency applies. Pad it out to a full
        // block so the original bytes survive and parsing can proceed.
        if( remainder != 0 && options.HasFlag( FITSParsingOptions.AllowTrailingPartialBlock ) )
        {
            byte[] padded = new byte[ bytes.Length + ( FITSFile.BlockSize - remainder ) ];

            bytes.Span.CopyTo( padded );

            bytes = padded;
        }

        IReadOnlyList< FITSBlock > blocks = bytes.Chunked( FITSFile.BlockSize ).Select( block => new FITSBlock( block, options ) ).ToList();

        this.SectionList = SectionsFrom( blocks, options );
    }

    /// <summary>
    /// Reads a file's bytes, classifying a failure as an invalid location or an
    /// unreadable file.
    /// </summary>
    /// <remarks>
    /// The classification happens only after the read fails, so there is no
    /// time-of-check/time-of-use gap: a missing path or a directory is an invalid
    /// location, anything else is an unreadable file.
    /// </remarks>
    /// <param name="path">The file path to read.</param>
    /// <returns>The file's bytes.</returns>
    /// <exception cref="FITSException">
    /// The path is missing or a directory
    /// (<see cref="FITSErrorKind.InvalidFileURL"/>), or the contents cannot be read
    /// (<see cref="FITSErrorKind.CannotReadFile"/>).
    /// </exception>
    private static ReadOnlyMemory< byte > ReadFileBytes( string path )
    {
        try
        {
            return File.ReadAllBytes( path );
        }
        catch( Exception )
        {
            if( Directory.Exists( path ) || File.Exists( path ) == false )
            {
                throw FITSException.InvalidFileURL( path );
            }

            throw FITSException.CannotReadFile( path );
        }
    }

    /// <summary>
    /// Groups blocks into sections by following the declared header geometry.
    /// </summary>
    /// <remarks>
    /// Reads a header (the first section) or extension up to its <c>END</c> block,
    /// finalizes and validates it, then consumes exactly the number of data blocks
    /// its geometry implies before reading the next header or extension.
    /// </remarks>
    /// <param name="blocks">The file's blocks, in order.</param>
    /// <param name="options">The parsing options to apply.</param>
    /// <returns>The file's sections, in order, starting with the primary header.</returns>
    /// <exception cref="FITSException">
    /// A header is missing or invalid, the geometry is unsound, or a data segment's
    /// length does not match the geometry and
    /// <see cref="FITSParsingOptions.AllowDataLengthMismatch"/> is not set.
    /// </exception>
    private static List< FITSSection > SectionsFrom( IReadOnlyList< FITSBlock > blocks, FITSParsingOptions options )
    {
        List< FITSSection > sections = new List< FITSSection >();
        int                 index    = 0;

        while( index < blocks.Count )
        {
            // Blank blocks remaining after the final HDU are trailing padding,
            // retained for round-tripping rather than parsed as a new HDU.
            if( sections.Count > 0 && blocks.Skip( index ).All( block => block.Data.IsBlank() ) )
            {
                foreach( FITSBlock block in blocks.Skip( index ) )
                {
                    sections[ ^1 ].AppendPadding( block );
                }

                break;
            }

            FITSSection.Kind kind   = sections.Count == 0 ? FITSSection.Kind.Header : FITSSection.Kind.Extension;
            FITSSection      header = new FITSSection( kind, block: null );

            // Accumulate header blocks up to and including the END block.
            while( index < blocks.Count )
            {
                FITSBlock block = blocks[ index ];
                index += 1;

                header.Append( block );

                if( block.HasEndMarker )
                {
                    break;
                }
            }

            header.FinalizeSection( options );

            IReadOnlyList< FITSProperty > properties = header.Properties;

            ValidateMandatoryKeywords( properties, kind == FITSSection.Kind.Extension );
            sections.Add( header );

            long expected   = ExpectedDataSize( properties );
            long blockCount = expected / FITSFile.BlockSize;

            if( blockCount <= 0 )
            {
                continue;
            }

            FITSSection segment  = new FITSSection( FITSSection.Kind.Data, block: null );
            long        consumed = 0;

            while( consumed < blockCount && index < blocks.Count )
            {
                segment.Append( blocks[ index ] );

                index    += 1;
                consumed += 1;
            }

            if( consumed != blockCount && options.HasFlag( FITSParsingOptions.AllowDataLengthMismatch ) == false )
            {
                throw FITSException.InvalidFileData( $"Data length mismatch: expected { expected.ToString( CultureInfo.InvariantCulture ) } bytes but found { segment.DataSize.ToString( CultureInfo.InvariantCulture ) }" );
            }

            if( consumed > 0 )
            {
                sections.Add( segment );
            }
        }

        return sections;
    }

    /// <summary>
    /// Validates the mandatory keywords (name, order and type) of a primary header
    /// or a conforming extension per FITS 4.0 section 4.4.1:
    /// <c>SIMPLE</c>/<c>XTENSION</c>, <c>BITPIX</c>, <c>NAXIS</c>, <c>NAXISn</c>,
    /// then <c>PCOUNT</c> and <c>GCOUNT</c> for extensions.
    /// </summary>
    /// <remarks>
    /// Ordering is always enforced: each mandatory keyword must occupy its exact
    /// index, even under <see cref="FITSParsingOptions.Lenient"/> - no parsing
    /// option relaxes this.
    /// </remarks>
    /// <param name="properties">The section's properties, in order.</param>
    /// <param name="isExtension">
    /// <c>true</c> to validate as an extension (expecting <c>XTENSION</c>,
    /// <c>PCOUNT</c> and <c>GCOUNT</c>); <c>false</c> for the primary header
    /// (expecting <c>SIMPLE</c>).
    /// </param>
    /// <exception cref="FITSException">
    /// A mandatory keyword is missing, out of order, of the wrong type, or has an
    /// invalid value.
    /// </exception>
    private static void ValidateMandatoryKeywords( IReadOnlyList< FITSProperty > properties, bool isExtension )
    {
        if( isExtension )
        {
            Validate( 0, properties, "XTENSION", FITSValueKind.String );
        }
        else
        {
            Validate( 0, properties, "SIMPLE", FITSValueKind.Logical, value =>
            {
                if( value.AsLogical != true )
                {
                    throw FITSException.InvalidFileData( "Invalid value for SIMPLE property" );
                }
            } );
        }

        Validate( 1, properties, "BITPIX", FITSValueKind.Integer, value =>
        {
            long? integer = value.AsInteger;

            if( integer is null || ValidBitpixValues.Contains( integer.Value ) == false )
            {
                throw FITSException.InvalidFileData( "Invalid value for BITPIX property" );
            }
        } );

        Validate( 2, properties, "NAXIS", FITSValueKind.Integer );

        long naxis = properties[ 2 ].Value.AsInteger ?? 0;

        // FITS 4.0 (section 4.4.1) caps NAXIS at 999.
        if( naxis < 0 || naxis > 999 )
        {
            throw FITSException.InvalidFileData( $"NAXIS value out of range: { naxis.ToString( CultureInfo.InvariantCulture ) } (expected 0...999)" );
        }

        for( int index = 0; index < naxis; index += 1 )
        {
            int axis = index + 1;

            Validate( index + 3, properties, $"NAXIS{ axis.ToString( CultureInfo.InvariantCulture ) }", FITSValueKind.Integer, value =>
            {
                if( ( value.AsInteger ?? -1 ) < 0 )
                {
                    throw FITSException.InvalidFileData( $"Invalid value for NAXIS{ axis.ToString( CultureInfo.InvariantCulture ) } property" );
                }
            } );
        }

        if( isExtension )
        {
            // PCOUNT and GCOUNT immediately follow the NAXISn set.
            Validate( ( int )naxis + 3, properties, "PCOUNT", FITSValueKind.Integer );
            Validate( ( int )naxis + 4, properties, "GCOUNT", FITSValueKind.Integer );
        }
    }

    /// <summary>
    /// Asserts that a property at a given index has the expected name and type,
    /// with an optional extra value check.
    /// </summary>
    /// <param name="index">The position the property must occupy.</param>
    /// <param name="properties">The properties to check.</param>
    /// <param name="name">The keyword name expected at <paramref name="index"/>.</param>
    /// <param name="kind">The value kind expected at <paramref name="index"/>.</param>
    /// <param name="extra">An optional extra validation of the value.</param>
    /// <exception cref="FITSException">
    /// The property is missing, misnamed, of the wrong kind, or rejected by
    /// <paramref name="extra"/>.
    /// </exception>
    private static void Validate( int index, IReadOnlyList< FITSProperty > properties, string name, FITSValueKind kind, Action< FITSValue >? extra = null )
    {
        if( properties.Count <= index )
        {
            throw FITSException.InvalidFileData( $"Missing property { name } expected at index { index.ToString( CultureInfo.InvariantCulture ) }" );
        }

        if( properties[ index ].Name != name )
        {
            throw FITSException.InvalidFileData( $"Missing property { name } expected at index { index.ToString( CultureInfo.InvariantCulture ) } - Found { properties[ index ].Name } instead" );
        }

        if( properties[ index ].Value.Kind != kind )
        {
            throw FITSException.InvalidFileData( $"Invalid type for property { name } at index { index.ToString( CultureInfo.InvariantCulture ) } - Expected { kind } but found { properties[ index ].Value.Kind }" );
        }

        extra?.Invoke( properties[ index ].Value );
    }

    /// <summary>
    /// The expected data-segment size in bytes (padded to a whole number of blocks)
    /// for a header or extension, per the general FITS 4.0 data-size formula
    /// <c>|BITPIX|/8 x GCOUNT x ( PCOUNT + product of NAXISn )</c>.
    /// </summary>
    /// <remarks>
    /// Absent <c>PCOUNT</c>/<c>GCOUNT</c> default to 0 and 1, so a standard array
    /// reduces to <c>|BITPIX|/8 x product of NAXISn</c>. For random groups
    /// (<c>GROUPS = T</c>) the first axis is excluded from the product.
    /// <c>NAXIS = 0</c> means no data follow.
    /// </remarks>
    /// <param name="properties">
    /// The header or extension properties supplying <c>BITPIX</c>, <c>NAXIS</c>,
    /// <c>NAXISn</c>, <c>GROUPS</c>, <c>PCOUNT</c> and <c>GCOUNT</c>.
    /// </param>
    /// <returns>
    /// The expected data-segment size in bytes, or <c>0</c> when no data follow
    /// (<c>NAXIS == 0</c>).
    /// </returns>
    /// <exception cref="FITSException">
    /// The geometry overflows a 64-bit size or exceeds <see cref="MaxDataSize"/>.
    /// </exception>
    private static long ExpectedDataSize( IReadOnlyList< FITSProperty > properties )
    {
        // Resolve each keyword once, first occurrence winning, so the per-axis loop
        // below is O(NAXIS) lookups rather than O(NAXIS x properties).
        Dictionary< string, FITSProperty > keywords = new Dictionary< string, FITSProperty >();

        foreach( FITSProperty property in properties )
        {
            keywords.TryAdd( property.Name, property );
        }

        long IntegerOrDefault( string name, long fallback )
        {
            return keywords.TryGetValue( name, out FITSProperty? property ) ? property.Value.AsInteger ?? fallback : fallback;
        }

        long bitpix = IntegerOrDefault( "BITPIX", 0 );
        long naxis  = IntegerOrDefault( "NAXIS", 0 );

        if( naxis <= 0 )
        {
            return 0;
        }

        // Random groups (GROUPS = T) use NAXIS1 = 0 as a group indicator and count
        // the data via GCOUNT/PCOUNT, so the first axis is left out of the element
        // product.
        bool groups  = keywords.TryGetValue( "GROUPS", out FITSProperty? groupsProperty ) && groupsProperty.Value.AsLogical == true;
        long product = 1;

        for( long n = 1; n <= naxis; n += 1 )
        {
            if( groups && n == 1 )
            {
                continue;
            }

            product = CheckedMultiply( product, IntegerOrDefault( $"NAXIS{ n.ToString( CultureInfo.InvariantCulture ) }", 0 ) );
        }

        // |BITPIX|/8 x GCOUNT x (PCOUNT + product), with PCOUNT/GCOUNT defaulting to
        // 0 and 1 so a standard array reduces to |BITPIX|/8 x product.
        long pcount   = IntegerOrDefault( "PCOUNT", 0 );
        long gcount   = IntegerOrDefault( "GCOUNT", 1 );
        long elements = CheckedMultiply( gcount, CheckedAdd( pcount, product ) );
        long bytes    = CheckedMultiply( Math.Abs( bitpix ) / 8, elements );

        if( bytes < 0 || bytes > FITSFile.MaxDataSize )
        {
            throw FITSException.InvalidFileData( $"Data geometry exceeds the maximum supported size of { FITSFile.MaxDataSize.ToString( CultureInfo.InvariantCulture ) } bytes" );
        }

        // Round the byte size up to a whole number of blocks.
        long blockCount = ( bytes + FITSFile.BlockSize - 1 ) / FITSFile.BlockSize;

        return blockCount * FITSFile.BlockSize;
    }

    /// <summary>
    /// Multiplies two 64-bit factors, throwing rather than trapping on overflow.
    /// </summary>
    /// <param name="a">The first factor.</param>
    /// <param name="b">The second factor.</param>
    /// <returns>The product.</returns>
    /// <exception cref="FITSException">The product overflows a 64-bit size.</exception>
    private static long CheckedMultiply( long a, long b )
    {
        try
        {
            return checked( a * b );
        }
        catch( OverflowException )
        {
            throw FITSException.InvalidFileData( "Data geometry overflows 64-bit size" );
        }
    }

    /// <summary>
    /// Adds two 64-bit values, throwing rather than trapping on overflow.
    /// </summary>
    /// <param name="a">The first addend.</param>
    /// <param name="b">The second addend.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="FITSException">The sum overflows a 64-bit size.</exception>
    private static long CheckedAdd( long a, long b )
    {
        try
        {
            return checked( a + b );
        }
        catch( OverflowException )
        {
            throw FITSException.InvalidFileData( "Data geometry overflows 64-bit size" );
        }
    }

    /// <summary>Returns a multi-line, human-readable summary of the file and its sections.</summary>
    /// <returns>The formatted description.</returns>
    public override string ToString()
    {
        string sections = string.Join( "\n", this.SectionList.Select( section => section.ToString( 2 ) ) );

        return $"FITSFile\n{{\n    Sections:\n    [\n{ sections }\n    ]\n}}";
    }
}
