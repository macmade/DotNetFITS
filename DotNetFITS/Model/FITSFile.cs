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
using System.Buffers;
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
    /// Builds a file from a list of sections, adopting them without parsing or
    /// validating.
    /// </summary>
    /// <remarks>
    /// The private designated constructor behind the from-scratch construction API.
    /// Validation happens on write, via
    /// <see cref="SerializedData(FITSSerializationOptions)"/>.
    /// </remarks>
    /// <param name="sections">
    /// The file's sections, in order, starting with the primary header.
    /// </param>
    private FITSFile( List< FITSSection > sections )
    {
        this.SectionList = sections;
    }

    /// <summary>
    /// Builds a primary HDU from scratch, with its mandatory keywords populated to a
    /// standards-compliant minimum.
    /// </summary>
    /// <remarks>
    /// Assembles a primary header carrying <c>SIMPLE</c>, <c>BITPIX</c>, <c>NAXIS</c>
    /// and one <c>NAXISn</c> per axis, followed by a data segment when
    /// <paramref name="data"/> is provided. The file is not validated here;
    /// <see cref="SerializedData(FITSSerializationOptions)"/> and
    /// <see cref="Write(string, FITSSerializationOptions)"/> validate it against the
    /// FITS 4.0 standard on write.
    /// </remarks>
    /// <param name="bitpix">
    /// The <c>BITPIX</c> value (the standard permits 8, 16, 32, 64, -32 and -64; an
    /// out-of-range value is rejected on write).
    /// </param>
    /// <param name="axes">
    /// The <c>NAXISn</c> dimensions, in order; its count becomes <c>NAXIS</c>. An empty
    /// list is a <c>NAXIS = 0</c> primary with no data.
    /// </param>
    /// <param name="data">The data-segment bytes, or <c>null</c> for none.</param>
    /// <exception cref="FITSException">
    /// Any error raised while building the mandatory keywords.
    /// </exception>
    public FITSFile( int bitpix, IReadOnlyList< int > axes, ReadOnlyMemory< byte >? data = null ) : this( PrimarySections( bitpix, axes, data ) )
    {}

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

    /// <summary>
    /// Appends an extension HDU to the file, with its mandatory keywords populated to a
    /// standards-compliant minimum.
    /// </summary>
    /// <remarks>
    /// Assembles an extension header carrying <c>XTENSION</c>, <c>BITPIX</c>,
    /// <c>NAXIS</c>, one <c>NAXISn</c> per axis, <c>PCOUNT</c> and <c>GCOUNT</c>,
    /// followed by a data segment when <paramref name="data"/> is provided, and appends
    /// them to the file. It also declares <c>EXTEND = T</c> in the primary header when
    /// not already present (FITS 4.0 section 4.4.1.1), placed immediately after the
    /// primary's <c>NAXISn</c> block.
    /// <para>
    /// The file is not validated here;
    /// <see cref="SerializedData(FITSSerializationOptions)"/> and
    /// <see cref="Write(string, FITSSerializationOptions)"/> validate it on write.
    /// </para>
    /// </remarks>
    /// <param name="type">
    /// The <c>XTENSION</c> type value (e.g. <c>IMAGE</c>, <c>TABLE</c>, <c>BINTABLE</c>).
    /// </param>
    /// <param name="bitpix">The <c>BITPIX</c> value.</param>
    /// <param name="axes">The <c>NAXISn</c> dimensions, in order; its count becomes <c>NAXIS</c>.</param>
    /// <param name="pcount">The <c>PCOUNT</c> value.</param>
    /// <param name="gcount">The <c>GCOUNT</c> value.</param>
    /// <param name="data">The data-segment bytes, or <c>null</c> for none.</param>
    /// <returns>The appended extension header section, for further editing.</returns>
    /// <exception cref="FITSException">
    /// The file has no primary header, or any error raised while building the keywords.
    /// </exception>
    public FITSSection AppendExtension( string type, int bitpix, IReadOnlyList< int > axes, int pcount = 0, int gcount = 1, ReadOnlyMemory< byte >? data = null )
    {
        IReadOnlyList< FITSProperty > properties = MandatoryExtensionProperties( type, bitpix, axes, pcount, gcount );
        FITSSection                   header     = new FITSSection( FITSSection.Kind.Extension, properties );

        this.DeclareExtensionsInPrimary();

        this.SectionList.Add( header );

        if( data is ReadOnlyMemory< byte > payload )
        {
            this.SectionList.Add( new FITSSection( payload ) );
        }

        return header;
    }

    /// <summary>
    /// Builds the sections of a from-scratch primary HDU: a header of the mandatory
    /// keywords, optionally followed by a data segment.
    /// </summary>
    /// <remarks>
    /// A static helper so the public from-scratch constructor can build the sections
    /// before delegating to the private section-adopting constructor.
    /// </remarks>
    /// <param name="bitpix">The <c>BITPIX</c> value.</param>
    /// <param name="axes">The <c>NAXISn</c> dimensions, in order.</param>
    /// <param name="data">The data-segment bytes, or <c>null</c> for none.</param>
    /// <returns>The primary HDU's sections, starting with the header.</returns>
    /// <exception cref="FITSException">
    /// Any error raised while building the mandatory keywords.
    /// </exception>
    private static List< FITSSection > PrimarySections( int bitpix, IReadOnlyList< int > axes, ReadOnlyMemory< byte >? data )
    {
        IReadOnlyList< FITSProperty > properties = MandatoryPrimaryProperties( bitpix, axes );
        List< FITSSection >           sections   = [ new FITSSection( FITSSection.Kind.Header, properties ) ];

        if( data is ReadOnlyMemory< byte > payload )
        {
            sections.Add( new FITSSection( payload ) );
        }

        return sections;
    }

    /// <summary>
    /// Builds the mandatory keywords of a primary header - <c>SIMPLE</c>, <c>BITPIX</c>,
    /// <c>NAXIS</c> and one <c>NAXISn</c> per axis - in the order FITS 4.0 section 4.4.1
    /// requires.
    /// </summary>
    /// <param name="bitpix">The <c>BITPIX</c> value.</param>
    /// <param name="axes">The <c>NAXISn</c> dimensions, in order.</param>
    /// <returns>The mandatory primary-header properties, in order.</returns>
    /// <exception cref="FITSException">Any error raised while building a property.</exception>
    private static IReadOnlyList< FITSProperty > MandatoryPrimaryProperties( int bitpix, IReadOnlyList< int > axes )
    {
        List< FITSProperty > properties =
        [
            new FITSProperty( "SIMPLE", true,               FITSSerializationOptions.Strict ),
            new FITSProperty( "BITPIX", ( long )bitpix,     FITSSerializationOptions.Strict ),
            new FITSProperty( "NAXIS",  ( long )axes.Count, FITSSerializationOptions.Strict ),
        ];

        properties.AddRange( NaxisProperties( axes ) );

        return properties;
    }

    /// <summary>
    /// Builds the mandatory keywords of an extension header - <c>XTENSION</c>,
    /// <c>BITPIX</c>, <c>NAXIS</c>, one <c>NAXISn</c> per axis, <c>PCOUNT</c> and
    /// <c>GCOUNT</c> - in the order FITS 4.0 section 4.4.1 requires.
    /// </summary>
    /// <param name="type">The <c>XTENSION</c> type value.</param>
    /// <param name="bitpix">The <c>BITPIX</c> value.</param>
    /// <param name="axes">The <c>NAXISn</c> dimensions, in order.</param>
    /// <param name="pcount">The <c>PCOUNT</c> value.</param>
    /// <param name="gcount">The <c>GCOUNT</c> value.</param>
    /// <returns>The mandatory extension-header properties, in order.</returns>
    /// <exception cref="FITSException">Any error raised while building a property.</exception>
    private static IReadOnlyList< FITSProperty > MandatoryExtensionProperties( string type, int bitpix, IReadOnlyList< int > axes, int pcount, int gcount )
    {
        List< FITSProperty > properties =
        [
            new FITSProperty( "XTENSION", type,               FITSSerializationOptions.Strict ),
            new FITSProperty( "BITPIX",   ( long )bitpix,     FITSSerializationOptions.Strict ),
            new FITSProperty( "NAXIS",    ( long )axes.Count, FITSSerializationOptions.Strict ),
        ];

        properties.AddRange( NaxisProperties( axes ) );
        properties.Add( new FITSProperty( "PCOUNT", ( long )pcount, FITSSerializationOptions.Strict ) );
        properties.Add( new FITSProperty( "GCOUNT", ( long )gcount, FITSSerializationOptions.Strict ) );

        return properties;
    }

    /// <summary>
    /// Builds the <c>NAXISn</c> properties for a set of axis dimensions.
    /// </summary>
    /// <param name="axes">The axis dimensions, in order; axis <c>n</c> becomes <c>NAXISn</c>.</param>
    /// <returns>The <c>NAXISn</c> properties, in order.</returns>
    /// <exception cref="FITSException">Any error raised while building a property.</exception>
    private static IReadOnlyList< FITSProperty > NaxisProperties( IReadOnlyList< int > axes )
    {
        return axes.Select
        (
            ( axis, index ) => new FITSProperty( $"NAXIS{ ( index + 1 ).ToString( CultureInfo.InvariantCulture ) }", ( long )axis, FITSSerializationOptions.Strict )
        )
        .ToList();
    }

    /// <summary>
    /// Declares <c>EXTEND = T</c> in the primary header when it is not already present.
    /// </summary>
    /// <remarks>
    /// FITS 4.0 section 4.4.1.1 requires the primary header to carry <c>EXTEND = T</c>
    /// when the file contains extensions. The keyword is inserted immediately after the
    /// primary's <c>NAXISn</c> block.
    /// </remarks>
    /// <exception cref="FITSException">
    /// The file has no primary header, or any error raised while inserting the keyword.
    /// </exception>
    private void DeclareExtensionsInPrimary()
    {
        FITSSection? primary = this.SectionList.FirstOrDefault();

        if( primary is null || primary.SectionKind != FITSSection.Kind.Header )
        {
            throw FITSException.InvalidFileData( "Cannot append an extension without a primary header" );
        }

        if( primary[ "EXTEND" ] is not null )
        {
            return;
        }

        // Compute the position after the NAXISn block without trapping on a pathological
        // NAXIS: an addition that overflows can only denote a position past the end, so
        // it clamps to the property count (append).
        FITSProperty extend = new FITSProperty( "EXTEND", true, FITSSerializationOptions.Strict );
        int          count  = primary.Properties.Count;
        int          index;

        try
        {
            long position = checked( ( primary.Naxis ?? 0 ) + 3 );
            index         = ( int )Math.Max( 0, Math.Min( position, count ) );
        }
        catch( OverflowException )
        {
            index = count;
        }

        primary.Insert( extend, index );
    }

    /// <summary>
    /// Removes an extension HDU - its header and any following data segment.
    /// </summary>
    /// <remarks>
    /// Only the removed sections change; every other section keeps its bytes (a clean
    /// section still re-emits its retained bytes on write). The primary's <c>EXTEND</c>
    /// keyword is left untouched.
    /// </remarks>
    /// <param name="index">
    /// The 0-based index of the extension among the file's extensions (the primary HDU
    /// is not counted).
    /// </param>
    /// <exception cref="FITSException"><paramref name="index"/> is out of range.</exception>
    public void RemoveExtension( int index )
    {
        List< List< FITSSection > > units = this.HduUnits();

        // index >= units.Count - 1 rather than index + 1 >= units.Count so a pathological
        // index cannot overflow-trap the addition.
        if( index < 0 || index >= units.Count - 1 )
        {
            throw FITSException.InvalidFileData( $"Extension index { index.ToString( CultureInfo.InvariantCulture ) } out of range" );
        }

        units.RemoveAt( index + 1 );

        this.SectionList = units.SelectMany( unit => unit ).ToList();
    }

    /// <summary>
    /// Moves an extension HDU to a different position among the extensions.
    /// </summary>
    /// <remarks>
    /// Moves the whole HDU unit (its header and any following data segment). The primary
    /// HDU is never moved, and the moved sections keep their bytes.
    /// </remarks>
    /// <param name="from">The 0-based index of the extension to move.</param>
    /// <param name="to">The 0-based index it should occupy after the move.</param>
    /// <exception cref="FITSException">Either index is out of range.</exception>
    public void MoveExtension( int from, int to )
    {
        List< List< FITSSection > > units = this.HduUnits();
        int                         count = units.Count - 1;

        if( from < 0 || from >= count || to < 0 || to >= count )
        {
            throw FITSException.InvalidFileData( $"Extension index out of range (from { from.ToString( CultureInfo.InvariantCulture ) }, to { to.ToString( CultureInfo.InvariantCulture ) })" );
        }

        List< FITSSection > unit = units[ from + 1 ];

        units.RemoveAt( from + 1 );
        units.Insert( to + 1, unit );

        this.SectionList = units.SelectMany( group => group ).ToList();
    }

    /// <summary>
    /// Groups the file's sections into HDU units - each a header (or extension) section
    /// plus any immediately-following data segment.
    /// </summary>
    /// <returns>The HDU units in file order; the first is the primary HDU.</returns>
    private List< List< FITSSection > > HduUnits()
    {
        List< List< FITSSection > > units = new List< List< FITSSection > >();
        int                         index = 0;

        while( index < this.SectionList.Count )
        {
            FITSSection header = this.SectionList[ index ];
            index += 1;

            if( index < this.SectionList.Count && this.SectionList[ index ].SectionKind == FITSSection.Kind.Data )
            {
                units.Add( [ header, this.SectionList[ index ] ] );

                index += 1;
            }
            else
            {
                units.Add( [ header ] );
            }
        }

        return units;
    }

    /// <summary>
    /// Replaces the primary HDU's data, updating its geometry keywords to match.
    /// </summary>
    /// <remarks>
    /// Rewrites the primary header's <c>BITPIX</c>, <c>NAXIS</c> and <c>NAXISn</c> from
    /// the supplied dimensions and sets (or clears) its data segment, so header and data
    /// cannot drift out of sync. The result is still validated on write.
    /// </remarks>
    /// <param name="bitpix">The new <c>BITPIX</c> value.</param>
    /// <param name="axes">The new <c>NAXISn</c> dimensions, in order.</param>
    /// <param name="data">The new data-segment bytes, or <c>null</c> to remove the data segment.</param>
    /// <exception cref="FITSException">
    /// The file has no primary header, or any error raised while rewriting the keywords.
    /// </exception>
    public void SetPrimaryData( int bitpix, IReadOnlyList< int > axes, ReadOnlyMemory< byte >? data )
    {
        FITSSection? primary = this.SectionList.FirstOrDefault();

        if( primary is null || primary.SectionKind != FITSSection.Kind.Header )
        {
            throw FITSException.InvalidFileData( "The file has no primary header" );
        }

        ApplyGeometry( primary, bitpix, axes );
        this.SetDataSegment( 0, data );
    }

    /// <summary>
    /// Replaces an extension HDU's data, updating its geometry keywords to match.
    /// </summary>
    /// <remarks>
    /// Rewrites the extension header's <c>BITPIX</c>, <c>NAXIS</c> and <c>NAXISn</c> from
    /// the supplied dimensions (preserving <c>XTENSION</c>, <c>PCOUNT</c> and
    /// <c>GCOUNT</c>) and sets (or clears) its data segment. The result is still
    /// validated on write.
    /// </remarks>
    /// <param name="index">The 0-based index of the extension among the file's extensions.</param>
    /// <param name="bitpix">The new <c>BITPIX</c> value.</param>
    /// <param name="axes">The new <c>NAXISn</c> dimensions, in order.</param>
    /// <param name="data">The new data-segment bytes, or <c>null</c> to remove the data segment.</param>
    /// <exception cref="FITSException">
    /// <paramref name="index"/> is out of range, or any error raised while rewriting the
    /// keywords.
    /// </exception>
    public void SetExtensionData( int index, int bitpix, IReadOnlyList< int > axes, ReadOnlyMemory< byte >? data )
    {
        List< List< FITSSection > > units = this.HduUnits();

        // index >= units.Count - 1 rather than index + 1 >= units.Count so a pathological
        // index cannot overflow-trap the addition.
        if( index < 0 || index >= units.Count - 1 )
        {
            throw FITSException.InvalidFileData( $"Extension index { index.ToString( CultureInfo.InvariantCulture ) } out of range" );
        }

        FITSSection header = units[ index + 1 ][ 0 ];

        // Locate the header before mutating it, so a failure leaves the file untouched
        // (all-or-nothing).
        int headerIndex = this.SectionList.FindIndex( section => ReferenceEquals( section, header ) );

        if( headerIndex < 0 )
        {
            throw FITSException.InvalidFileData( "Extension header not found" );
        }

        ApplyGeometry( header, bitpix, axes );
        this.SetDataSegment( headerIndex, data );
    }

    /// <summary>
    /// Rewrites a header's geometry keywords (<c>BITPIX</c>, <c>NAXIS</c>,
    /// <c>NAXISn</c>) from a set of dimensions, preserving every other keyword.
    /// </summary>
    /// <remarks>
    /// <c>BITPIX</c> and <c>NAXIS</c> are replaced in place; the old <c>NAXISn</c>
    /// records are removed and the new set inserted immediately after <c>NAXIS</c>, so
    /// keywords that followed the old <c>NAXISn</c> block (such as <c>PCOUNT</c>/
    /// <c>GCOUNT</c>) keep their relative order.
    /// </remarks>
    /// <param name="header">The header or extension section to update.</param>
    /// <param name="bitpix">The new <c>BITPIX</c> value.</param>
    /// <param name="axes">The new <c>NAXISn</c> dimensions, in order.</param>
    /// <exception cref="FITSException">Any error raised while building or placing a keyword.</exception>
    private static void ApplyGeometry( FITSSection header, int bitpix, IReadOnlyList< int > axes )
    {
        header.SetProperty( new FITSProperty( "BITPIX", ( long )bitpix,     FITSSerializationOptions.Strict ) );
        header.SetProperty( new FITSProperty( "NAXIS",  ( long )axes.Count, FITSSerializationOptions.Strict ) );

        List< string > obsolete = header.Properties.Where( property => IsNaxisIndex( property.Name ) ).Select( property => property.Name ).ToList();

        foreach( string name in obsolete )
        {
            header.RemoveProperties( name );
        }

        List< FITSProperty > properties = header.Properties.ToList();
        int                  naxisIndex = properties.FindIndex( property => property.Name == "NAXIS" );
        int                  anchor     = ( naxisIndex >= 0 ? naxisIndex : properties.Count - 1 ) + 1;

        IReadOnlyList< FITSProperty > axisProperties = NaxisProperties( axes );

        for( int offset = 0; offset < axisProperties.Count; offset += 1 )
        {
            header.Insert( axisProperties[ offset ], anchor + offset );
        }
    }

    /// <summary>
    /// Reports whether a keyword name is an axis keyword (<c>NAXIS</c> followed by a
    /// number), as opposed to the <c>NAXIS</c> count keyword itself.
    /// </summary>
    /// <param name="name">The keyword name to test.</param>
    /// <returns><c>true</c> for <c>NAXIS1</c>, <c>NAXIS2</c>, ...; <c>false</c> otherwise.</returns>
    private static bool IsNaxisIndex( string name )
    {
        if( name.StartsWith( "NAXIS", StringComparison.Ordinal ) == false || name.Length <= 5 )
        {
            return false;
        }

        return name.Skip( 5 ).All( character => char.IsAsciiDigit( character ) );
    }

    /// <summary>
    /// Sets, replaces or removes the data segment following a header section.
    /// </summary>
    /// <param name="headerIndex">The section index of the header the data segment follows.</param>
    /// <param name="data">The new data-segment bytes, or <c>null</c> to remove any existing one.</param>
    /// <exception cref="FITSException">Any error raised while replacing an existing payload.</exception>
    private void SetDataSegment( int headerIndex, ReadOnlyMemory< byte >? data )
    {
        int  dataIndex = headerIndex + 1;
        bool hasData   = dataIndex < this.SectionList.Count && this.SectionList[ dataIndex ].SectionKind == FITSSection.Kind.Data;

        if( data is ReadOnlyMemory< byte > payload )
        {
            if( hasData )
            {
                this.SectionList[ dataIndex ].SetDataPayload( payload );
            }
            else
            {
                this.SectionList.Insert( dataIndex, new FITSSection( payload ) );
            }
        }
        else if( hasData )
        {
            this.SectionList.RemoveAt( dataIndex );
        }
    }

    /// <summary>
    /// The complete file contents, serialized and validated with the strict options.
    /// </summary>
    /// <remarks>
    /// A convenience for <see cref="SerializedData(FITSSerializationOptions)"/> with
    /// <see cref="FITSSerializationOptions.Strict"/>. An unmodified parsed file yields
    /// its original bytes byte-for-byte; a file whose sections were modified re-renders
    /// those sections from their model.
    /// </remarks>
    /// <exception cref="FITSException">
    /// Strict validation fails, or any error raised while rendering a modified section.
    /// </exception>
    public ReadOnlyMemory< byte > Data => this.SerializedData( FITSSerializationOptions.Strict );

    /// <summary>
    /// The complete file contents, validated then serialized.
    /// </summary>
    /// <remarks>
    /// Validates the file against the FITS 4.0 standard (mandatory keywords,
    /// <c>BITPIX</c>/<c>NAXIS</c> geometry, data-segment size, and primary-HDU-first
    /// ordering), then concatenates every section's serialized bytes. An unmodified
    /// parsed file reproduces its original bytes byte-for-byte; modified sections are
    /// re-rendered from their model.
    /// </remarks>
    /// <param name="options">
    /// The serialization options to apply. <see cref="FITSSerializationOptions.Strict"/>
    /// enforces the full standard; <see cref="FITSSerializationOptions.Lenient"/> relaxes
    /// the data-size check and coerces invalid keywords.
    /// </param>
    /// <returns>The complete file bytes, a whole number of blocks.</returns>
    /// <exception cref="FITSException">
    /// Validation fails, or any error raised while rendering a section.
    /// </exception>
    public ReadOnlyMemory< byte > SerializedData( FITSSerializationOptions options )
    {
        this.ValidateForSerialization( options );

        int                       size   = this.SectionList.Sum( section => section.SerializedByteCount );
        ArrayBufferWriter< byte > buffer = new ArrayBufferWriter< byte >( Math.Max( 1, size ) );

        foreach( FITSSection section in this.SectionList )
        {
            section.AppendSerializedData( buffer, options );
        }

        return buffer.WrittenMemory;
    }

    /// <summary>
    /// Serializes the file and writes it to a file path.
    /// </summary>
    /// <remarks>
    /// Serializes via <see cref="SerializedData(FITSSerializationOptions)"/> - which
    /// validates first - then writes the bytes atomically, replacing any existing file:
    /// the bytes go to a temporary file that is moved into place, so a reader never sees
    /// a partially-written file.
    /// </remarks>
    /// <param name="path">The location to write to.</param>
    /// <param name="options">The serialization options to apply.</param>
    /// <exception cref="FITSException">
    /// The bytes cannot be written (<see cref="FITSErrorKind.CannotWriteFile"/>), or any
    /// error raised while validating or serializing.
    /// </exception>
    public void Write( string path, FITSSerializationOptions options )
    {
        ReadOnlyMemory< byte > data = this.SerializedData( options );

        try
        {
            WriteBytesAtomically( path, data );
        }
        catch( Exception )
        {
            throw FITSException.CannotWriteFile( path );
        }
    }

    /// <summary>
    /// Writes bytes to a path atomically, by writing a temporary file and moving it into
    /// place.
    /// </summary>
    /// <remarks>
    /// The temporary file is created in the destination directory so the move is a
    /// same-volume rename, and it is removed if the write or move fails.
    /// </remarks>
    /// <param name="path">The destination file path.</param>
    /// <param name="data">The bytes to write.</param>
    private static void WriteBytesAtomically( string path, ReadOnlyMemory< byte > data )
    {
        string fullPath  = Path.GetFullPath( path );
        string directory = Path.GetDirectoryName( fullPath ) ?? Directory.GetCurrentDirectory();
        string temporary = Path.Combine( directory, $"{ Guid.NewGuid().ToString() }.tmp" );

        try
        {
            File.WriteAllBytes( temporary, data.ToArray() );
            File.Move( temporary, fullPath, overwrite: true );
        }
        catch( Exception )
        {
            TryDelete( temporary );

            throw;
        }
    }

    /// <summary>
    /// Deletes a file if it exists, ignoring any failure.
    /// </summary>
    /// <remarks>
    /// Cleans up the temporary file of a failed atomic write without masking the original
    /// failure.
    /// </remarks>
    /// <param name="path">The file path to delete.</param>
    private static void TryDelete( string path )
    {
        try
        {
            File.Delete( path );
        }
        catch( IOException )
        {
        }
        catch( UnauthorizedAccessException )
        {
        }
    }

    /// <summary>
    /// Validates the file's structure before serialization.
    /// </summary>
    /// <remarks>
    /// Walks the sections as header/extension units each optionally followed by a data
    /// segment: the first section must be the primary header; every header and extension
    /// must carry well-formed mandatory keywords (section 4.4.1); and each data segment's
    /// size must match the geometry, unless
    /// <see cref="FITSSerializationOptions.AllowDataSizeMismatch"/> is set.
    /// </remarks>
    /// <param name="options">The serialization options to apply.</param>
    /// <exception cref="FITSException">
    /// The file is empty, a section is out of place, a mandatory keyword is invalid, or a
    /// data segment's size does not match its geometry.
    /// </exception>
    private void ValidateForSerialization( FITSSerializationOptions options )
    {
        if( this.SectionList.FirstOrDefault()?.SectionKind != FITSSection.Kind.Header )
        {
            throw FITSException.InvalidFileData( "The first section must be the primary header" );
        }

        int index = 0;

        while( index < this.SectionList.Count )
        {
            FITSSection section = this.SectionList[ index ];

            if( section.SectionKind != FITSSection.Kind.Header && section.SectionKind != FITSSection.Kind.Extension )
            {
                throw FITSException.InvalidFileData( $"Unexpected data segment at index { index.ToString( CultureInfo.InvariantCulture ) }: a data segment can only follow a header or extension that declares data (a NAXIS = 0 HDU has none)" );
            }

            IReadOnlyList< FITSProperty > properties = section.Properties;

            ValidateMandatoryKeywords( properties, section.SectionKind == FITSSection.Kind.Extension );

            long expected = ExpectedDataSize( properties );
            index += 1;

            if( expected <= 0 )
            {
                continue;
            }

            if( index >= this.SectionList.Count || this.SectionList[ index ].SectionKind != FITSSection.Kind.Data )
            {
                if( options.HasFlag( FITSSerializationOptions.AllowDataSizeMismatch ) == false )
                {
                    throw FITSException.InvalidFileData( $"Missing data segment: expected { expected.ToString( CultureInfo.InvariantCulture ) } bytes" );
                }

                continue;
            }

            long actual = this.SectionList[ index ].SerializedByteCount;
            index += 1;

            if( actual != expected && options.HasFlag( FITSSerializationOptions.AllowDataSizeMismatch ) == false )
            {
                throw FITSException.InvalidFileData( $"Data size mismatch: expected { expected.ToString( CultureInfo.InvariantCulture ) } bytes but found { actual.ToString( CultureInfo.InvariantCulture ) }" );
            }
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
