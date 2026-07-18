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
using System.Linq;
using System.Text;

namespace DotNetFITS;

/// <summary>
/// A contiguous run of FITS blocks forming one logical unit of a file.
/// </summary>
/// <remarks>
/// A section is either the primary header, an extension header, or a data segment.
/// Header and extension sections parse their blocks into <see cref="Properties"/>;
/// data sections retain their raw blocks. Blocks are appended as the file is
/// scanned, then <see cref="FinalizeSection(FITSParsingOptions)"/> parses and
/// validates the accumulated content.
/// <para>
/// A section holds mutable parsing state and composes <see cref="FITSBlock"/>, so
/// it is not thread-safe.
/// </para>
/// </remarks>
public sealed class FITSSection
{
    /// <summary>
    /// The role a <see cref="FITSSection"/> plays in the file.
    /// </summary>
    /// <remarks>
    /// The member names double as the human-readable descriptions
    /// (<see cref="object.ToString"/> returns them verbatim), so the section's
    /// textual summary needs no separate mapping.
    /// </remarks>
    public enum Kind
    {
        /// <summary>The primary header (the first section of the file).</summary>
        Header,

        /// <summary>An extension header introduced by an <c>XTENSION</c> block.</summary>
        Extension,

        /// <summary>A data segment following a header or extension.</summary>
        Data,
    }

    /// <summary>The role this section plays in the file.</summary>
    public Kind SectionKind { get; }

    /// <summary>The raw blocks making up the section, in file order.</summary>
    private List< FITSBlock > Blocks { get; } = new List< FITSBlock >();

    /// <summary>
    /// Whether <see cref="FinalizeSection(FITSParsingOptions)"/> has completed,
    /// after which structural blocks can no longer be appended (so
    /// <see cref="Properties"/> and the blocks cannot disagree).
    /// </summary>
    private bool IsFinalized { get; set; }

    /// <summary>
    /// A value indicating whether the section's model has diverged from its
    /// retained blocks and must be re-rendered on serialization instead of
    /// re-emitting those bytes.
    /// </summary>
    /// <remarks>
    /// A freshly-parsed section is clean (<c>false</c>); building one from a model,
    /// or mutating one, sets this. It is independent of finalization: a section can
    /// be both finalized (locked against block appends) and in need of
    /// re-serialization.
    /// </remarks>
    public bool NeedsSerialization { get; private set; }

    /// <summary>
    /// The data payload of a built or edited data section, or <c>null</c> for a
    /// parsed section that has not been re-assigned a payload, whose bytes live in
    /// the retained blocks.
    /// </summary>
    private ReadOnlyMemory< byte >? Payload { get; set; }

    /// <summary>The mutable backing store of <see cref="Properties"/>.</summary>
    private List< FITSProperty > PropertyList { get; set; } = new List< FITSProperty >();

    /// <summary>
    /// The parsed header records. Empty for data sections, and until
    /// <see cref="FinalizeSection(FITSParsingOptions)"/> has run.
    /// </summary>
    /// <remarks>
    /// The <c>END</c> marker is excluded from this list, but it round-trips through
    /// <see cref="Data"/> since the raw block bytes are retained.
    /// </remarks>
    public IReadOnlyList< FITSProperty > Properties => this.PropertyList;

    /// <summary>
    /// The first property whose keyword name matches, or <c>null</c> if none does.
    /// </summary>
    /// <remarks>
    /// A thin, read-only convenience over <see cref="Properties"/>. When a keyword
    /// appears more than once it returns the first occurrence, matching the
    /// first-wins resolution the parser uses for geometry keywords.
    /// </remarks>
    /// <param name="keyword">The keyword name to look up.</param>
    /// <returns>The first matching property, or <c>null</c>.</returns>
    public FITSProperty? this[ string keyword ] => this.PropertyList.FirstOrDefault( property => property.Name == keyword );

    /// <summary>
    /// The <c>BITPIX</c> value, or <c>null</c> if the keyword is absent or not an
    /// integer.
    /// </summary>
    public long? Bitpix => this[ "BITPIX" ]?.Value.AsInteger;

    /// <summary>
    /// The <c>NAXIS</c> value, or <c>null</c> if the keyword is absent or not an
    /// integer.
    /// </summary>
    public long? Naxis => this[ "NAXIS" ]?.Value.AsInteger;

    /// <summary>
    /// The <c>NAXISn</c> value for axis <paramref name="n"/>, or <c>null</c> if the
    /// keyword is absent or not an integer.
    /// </summary>
    /// <remarks>
    /// Named <c>NaxisAt</c> rather than <c>Naxis</c> because C# does not allow a
    /// property and a method to share a name.
    /// </remarks>
    /// <param name="n">The 1-based axis index.</param>
    /// <returns>The parsed <c>NAXISn</c> value, or <c>null</c>.</returns>
    public long? NaxisAt( int n ) => this[ $"NAXIS{ n.ToString( CultureInfo.InvariantCulture ) }" ]?.Value.AsInteger;

    /// <summary>
    /// Creates a section of the given kind, optionally seeded with a first block.
    /// </summary>
    /// <param name="kind">The role this section plays.</param>
    /// <param name="block">An initial block to append, or <c>null</c> to start empty.</param>
    /// <exception cref="FITSException">
    /// The initial block is not valid for the section kind.
    /// </exception>
    public FITSSection( Kind kind, FITSBlock? block )
    {
        this.SectionKind = kind;
        this.Payload     = null;

        if( block is not null )
        {
            this.Append( block );
        }
    }

    /// <summary>
    /// Creates a data section from a raw payload.
    /// </summary>
    /// <remarks>
    /// The section is marked as needing serialization, so
    /// <see cref="SerializedData(FITSSerializationOptions)"/> renders the payload
    /// (zero-padded to the block boundary) rather than re-emitting retained blocks,
    /// of which it has none.
    /// </remarks>
    /// <param name="dataPayload">The data-segment bytes, of any length.</param>
    public FITSSection( ReadOnlyMemory< byte > dataPayload )
    {
        this.SectionKind        = Kind.Data;
        this.Payload            = dataPayload;
        this.NeedsSerialization = true;
        this.IsFinalized        = true;
    }

    /// <summary>
    /// Creates a header or extension section from a model of properties.
    /// </summary>
    /// <remarks>
    /// The section is marked as needing serialization, so
    /// <see cref="SerializedData(FITSSerializationOptions)"/> renders it from
    /// <see cref="Properties"/> - appending the <c>END</c> marker and padding to the
    /// block boundary, both of which the library manages - rather than from retained
    /// blocks, of which it has none.
    /// <para>
    /// The caller owns the mandatory keywords and their order (<c>SIMPLE</c>/
    /// <c>XTENSION</c>, <c>BITPIX</c>, <c>NAXIS</c>, <c>NAXISn</c>, ...); use
    /// <see cref="Insert(FITSProperty, int)"/> to place them.
    /// </para>
    /// </remarks>
    /// <param name="kind">
    /// The section kind; must be <see cref="Kind.Header"/> or
    /// <see cref="Kind.Extension"/>.
    /// </param>
    /// <param name="properties">
    /// The header records, in order. None may be named <c>END</c>.
    /// </param>
    /// <exception cref="FITSException">
    /// <paramref name="kind"/> is <see cref="Kind.Data"/>, a property is the reserved
    /// <c>END</c> keyword, or a property already belongs to another section.
    /// </exception>
    public FITSSection( Kind kind, IReadOnlyList< FITSProperty > properties ) : this( kind, block: null )
    {
        RequireHeaderKind( kind );

        foreach( FITSProperty property in properties )
        {
            RejectReservedKeyword( property );
        }

        foreach( FITSProperty property in properties )
        {
            RequireAttachable( property, this );
        }

        this.PropertyList       = properties.ToList();
        this.NeedsSerialization = true;
        this.IsFinalized        = true;

        foreach( FITSProperty property in this.PropertyList )
        {
            property.Section = this;
        }
    }

    /// <summary>
    /// Appends a property to a header or extension section.
    /// </summary>
    /// <param name="property">The property to append. It must not be named <c>END</c>.</param>
    /// <exception cref="FITSException">
    /// The section is a data section, <paramref name="property"/> is the reserved
    /// <c>END</c> keyword, or it already belongs to another section.
    /// </exception>
    public void Append( FITSProperty property )
    {
        RequireHeaderKind( this.SectionKind );
        RejectReservedKeyword( property );
        RequireAttachable( property, this );

        property.Section = this;

        this.PropertyList.Add( property );
        this.MarkNeedsSerialization();
    }

    /// <summary>
    /// Inserts a property at a given position in a header or extension section.
    /// </summary>
    /// <param name="property">The property to insert. It must not be named <c>END</c>.</param>
    /// <param name="index">The position to insert at, in <c>0..Properties.Count</c>.</param>
    /// <exception cref="FITSException">
    /// The section is a data section, <paramref name="property"/> is the reserved
    /// <c>END</c> keyword, it already belongs to another section, or
    /// <paramref name="index"/> is out of range.
    /// </exception>
    public void Insert( FITSProperty property, int index )
    {
        RequireHeaderKind( this.SectionKind );
        RejectReservedKeyword( property );
        RequireAttachable( property, this );

        if( index < 0 || index > this.PropertyList.Count )
        {
            throw FITSException.InvalidSectionData( $"Insert index { index.ToString( CultureInfo.InvariantCulture ) } out of range 0...{ this.PropertyList.Count.ToString( CultureInfo.InvariantCulture ) }" );
        }

        property.Section = this;

        this.PropertyList.Insert( index, property );
        this.MarkNeedsSerialization();
    }

    /// <summary>
    /// Replaces the first property with the same keyword, or appends it.
    /// </summary>
    /// <remarks>
    /// Finds the first existing property whose name matches
    /// <paramref name="property"/>'s and replaces it in place, keeping its position;
    /// if none matches, <paramref name="property"/> is appended.
    /// </remarks>
    /// <param name="property">The property to set. It must not be named <c>END</c>.</param>
    /// <exception cref="FITSException">
    /// The section is a data section, <paramref name="property"/> is the reserved
    /// <c>END</c> keyword, or it already belongs to another section.
    /// </exception>
    public void SetProperty( FITSProperty property )
    {
        RequireHeaderKind( this.SectionKind );
        RejectReservedKeyword( property );
        RequireAttachable( property, this );

        int index = this.PropertyList.FindIndex( existing => existing.Name == property.Name );

        if( index >= 0 )
        {
            FITSProperty replaced = this.PropertyList[ index ];

            if( ReferenceEquals( replaced.Section, this ) )
            {
                replaced.Section = null;
            }

            this.PropertyList[ index ] = property;
        }
        else
        {
            this.PropertyList.Add( property );
        }

        property.Section = this;

        this.MarkNeedsSerialization();
    }

    /// <summary>
    /// Removes every property with the given keyword from a header or extension
    /// section.
    /// </summary>
    /// <remarks>
    /// A no-op - leaving the section clean - when no property matches.
    /// </remarks>
    /// <param name="keyword">The keyword name of the properties to remove.</param>
    /// <exception cref="FITSException">
    /// The section is a data section, which carries no properties.
    /// </exception>
    public void RemoveProperties( string keyword )
    {
        RequireHeaderKind( this.SectionKind );

        List< FITSProperty > removed = this.PropertyList.Where( property => property.Name == keyword ).ToList();

        if( removed.Count == 0 )
        {
            return;
        }

        foreach( FITSProperty property in removed )
        {
            if( ReferenceEquals( property.Section, this ) )
            {
                property.Section = null;
            }
        }

        this.PropertyList.RemoveAll( property => property.Name == keyword );
        this.MarkNeedsSerialization();
    }

    /// <summary>
    /// Replaces a data section's payload.
    /// </summary>
    /// <param name="payload">The new data-segment bytes, of any length.</param>
    /// <exception cref="FITSException">The section is not a data section.</exception>
    public void SetDataPayload( ReadOnlyMemory< byte > payload )
    {
        if( this.SectionKind != Kind.Data )
        {
            throw FITSException.InvalidSectionData( "Only a data section has a data payload" );
        }

        this.Payload = payload;

        this.MarkNeedsSerialization();
    }

    /// <summary>
    /// Requires a section kind that carries properties.
    /// </summary>
    /// <param name="kind">The kind to check.</param>
    /// <exception cref="FITSException"><paramref name="kind"/> is <see cref="Kind.Data"/>.</exception>
    private static void RequireHeaderKind( Kind kind )
    {
        if( kind != Kind.Header && kind != Kind.Extension )
        {
            throw FITSException.InvalidSectionData( "Only a header or extension section has properties" );
        }
    }

    /// <summary>
    /// Rejects a property carrying a keyword the library manages itself.
    /// </summary>
    /// <remarks>
    /// The <c>END</c> marker is appended automatically on serialization, so it must
    /// not appear among a section's properties (that would emit two <c>END</c>
    /// markers).
    /// </remarks>
    /// <param name="property">The property to check.</param>
    /// <exception cref="FITSException"><paramref name="property"/> is named <c>END</c>.</exception>
    private static void RejectReservedKeyword( FITSProperty property )
    {
        if( property.Name == "END" )
        {
            throw FITSException.InvalidSectionData( "The END marker is managed by the library and cannot be added as a property" );
        }
    }

    /// <summary>
    /// Requires that a property can be attached to a section - that it belongs to no
    /// section, or already to this one.
    /// </summary>
    /// <remarks>
    /// Enforces single ownership so a property's owning section back-reference is
    /// never silently re-pointed away from a section that still holds it (which would
    /// leave that section unable to detect in-place edits). To move a property
    /// between sections, remove it from the first - which clears its back-reference -
    /// before adding it to the second.
    /// </remarks>
    /// <param name="property">The property to check.</param>
    /// <param name="section">The section it is about to be attached to.</param>
    /// <exception cref="FITSException">
    /// <paramref name="property"/> already belongs to a different section.
    /// </exception>
    private static void RequireAttachable( FITSProperty property, FITSSection section )
    {
        if( property.Section is not null && ReferenceEquals( property.Section, section ) == false )
        {
            throw FITSException.InvalidSectionData( $"The property { property.Name } already belongs to another section" );
        }
    }

    /// <summary>
    /// Marks this section as needing re-serialization from its model, called when
    /// one of its properties is edited in place, or when its own model is mutated.
    /// </summary>
    internal void MarkNeedsSerialization()
    {
        this.NeedsSerialization = true;
    }

    /// <summary>
    /// The total size, in bytes, of the section's retained blocks.
    /// </summary>
    /// <remarks>
    /// This reflects the bytes as parsed; a section pending re-serialization may
    /// render to a different size.
    /// </remarks>
    public int DataSize => this.Blocks.Sum( block => block.Data.Length );

    /// <summary>
    /// The number of bytes this section serializes to, computed without
    /// materializing the bytes.
    /// </summary>
    /// <remarks>
    /// Exact for a clean section (its retained <see cref="DataSize"/>) and for a data
    /// section pending serialization (its payload padded to the next block boundary).
    /// For a header or extension pending serialization it returns the retained
    /// <see cref="DataSize"/> as a capacity estimate only, since the exact rendered
    /// size would require serializing the section.
    /// </remarks>
    internal int SerializedByteCount
    {
        get
        {
            if( this.NeedsSerialization == false || this.SectionKind != Kind.Data )
            {
                return this.DataSize;
            }

            int count     = this.Payload?.Length ?? this.DataSize;
            int remainder = count % FITSFile.BlockSize;

            return remainder == 0 ? count : count + FITSFile.BlockSize - remainder;
        }
    }

    /// <summary>
    /// The section's retained raw bytes, exactly as parsed.
    /// </summary>
    private ReadOnlyMemory< byte > RetainedBytes
    {
        get
        {
            byte[] bytes  = new byte[ this.DataSize ];
            int    offset = 0;

            foreach( FITSBlock block in this.Blocks )
            {
                block.Data.Span.CopyTo( bytes.AsSpan( offset ) );

                offset += block.Data.Length;
            }

            return bytes;
        }
    }

    /// <summary>
    /// The section's serialized bytes, rendered with the strict options.
    /// </summary>
    /// <remarks>
    /// A convenience for <see cref="SerializedData(FITSSerializationOptions)"/> with
    /// <see cref="FITSSerializationOptions.Strict"/>. A clean parsed section yields
    /// its retained bytes byte-for-byte; a section pending re-serialization is
    /// rendered from its model, which can fail.
    /// </remarks>
    /// <exception cref="FITSException">
    /// Any error raised while rendering a section that needs serialization.
    /// </exception>
    public ReadOnlyMemory< byte > Data => this.SerializedData( FITSSerializationOptions.Strict );

    /// <summary>
    /// The section's serialized bytes.
    /// </summary>
    /// <remarks>
    /// Returns the retained blocks unchanged when the section is clean (so an
    /// unmodified parsed section round-trips byte-for-byte), and renders from the
    /// model when the section needs serialization.
    /// </remarks>
    /// <param name="options">The serialization options to apply when rendering.</param>
    /// <returns>The section's bytes, a whole number of blocks.</returns>
    /// <exception cref="FITSException">Any error raised while rendering.</exception>
    public ReadOnlyMemory< byte > SerializedData( FITSSerializationOptions options )
    {
        ArrayBufferWriter< byte > buffer = new ArrayBufferWriter< byte >( Math.Max( 1, this.DataSize ) );

        this.AppendSerializedData( buffer, options );

        return buffer.WrittenMemory;
    }

    /// <summary>
    /// Appends the section's serialized bytes to an existing buffer.
    /// </summary>
    /// <remarks>
    /// Lets a caller assemble several sections into one buffer without an
    /// intermediate copy per section. Routes to the retained blocks when clean and
    /// to the model renderer when the section needs serialization.
    /// </remarks>
    /// <param name="buffer">The buffer to append to.</param>
    /// <param name="options">The serialization options to apply when rendering.</param>
    /// <exception cref="FITSException">Any error raised while rendering.</exception>
    internal void AppendSerializedData( ArrayBufferWriter< byte > buffer, FITSSerializationOptions options )
    {
        if( this.NeedsSerialization == false )
        {
            buffer.Write( this.RetainedBytes.Span );

            return;
        }

        switch( this.SectionKind )
        {
            case Kind.Header:
            case Kind.Extension:

                buffer.Write( this.RenderedHeader( options ).Span );

                break;

            case Kind.Data:

                buffer.Write( this.RenderedDataSegment().Span );

                break;

            default:

                throw FITSException.InvalidSectionData( $"Unknown section kind: { this.SectionKind }" );
        }
    }

    /// <summary>
    /// Renders a header or extension section from its <see cref="Properties"/>.
    /// </summary>
    /// <remarks>
    /// Serializes every property to its card(s), appends the <c>END</c> marker, and
    /// blank-pads the result to a whole number of blocks (FITS 4.0 section 3.3.1).
    /// </remarks>
    /// <param name="options">The serialization options to apply.</param>
    /// <returns>The rendered header bytes.</returns>
    /// <exception cref="FITSException">
    /// The rendered text is not ASCII, or any error raised while rendering a
    /// property.
    /// </exception>
    private ReadOnlyMemory< byte > RenderedHeader( FITSSerializationOptions options )
    {
        IEnumerable< string > cards = this.PropertyList.SelectMany( property => property.Serialized( options ) );
        string                end   = "END".PaddedOrTruncated( FITSFile.CardSize );
        string                text  = string.Concat( cards ) + end;

        if( text.Any( character => character > '\u007F' ) )
        {
            throw FITSException.CannotSerialize( "Header contains non-ASCII characters" );
        }

        return PaddedToBlockBoundary( Encoding.ASCII.GetBytes( text ), 0x20 );
    }

    /// <summary>
    /// Renders a data section from its payload, zero-padded to the block boundary
    /// (FITS 4.0 section 3.3.2).
    /// </summary>
    /// <returns>The rendered data-segment bytes.</returns>
    private ReadOnlyMemory< byte > RenderedDataSegment() => PaddedToBlockBoundary( this.Payload ?? this.RetainedBytes, 0x00 );

    /// <summary>
    /// Pads a buffer to a whole number of blocks.
    /// </summary>
    /// <param name="data">The bytes to pad.</param>
    /// <param name="fill">
    /// The byte to pad with (ASCII space for headers, zero for data).
    /// </param>
    /// <returns>
    /// <paramref name="data"/> padded to the next block boundary, or unchanged if it
    /// is already block-aligned.
    /// </returns>
    private static ReadOnlyMemory< byte > PaddedToBlockBoundary( ReadOnlyMemory< byte > data, byte fill )
    {
        int remainder = data.Length % FITSFile.BlockSize;

        if( remainder == 0 )
        {
            return data;
        }

        int    padding = FITSFile.BlockSize - remainder;
        byte[] result  = new byte[ data.Length + padding ];

        data.Span.CopyTo( result );
        Array.Fill( result, fill, data.Length, padding );

        return result;
    }

    /// <summary>
    /// Appends a block to the section.
    /// </summary>
    /// <remarks>
    /// For header and extension sections this enforces structural rules: the block
    /// must be ASCII, must not follow an <c>END</c> marker, and must not introduce a
    /// new extension mid-section.
    /// </remarks>
    /// <param name="block">The block to append.</param>
    /// <exception cref="FITSException">
    /// The section is already finalized, or appending the block would violate the
    /// structural rules.
    /// </exception>
    internal void Append( FITSBlock block )
    {
        if( this.IsFinalized )
        {
            throw FITSException.InvalidSectionData( "Cannot append to a finalized section" );
        }

        if( this.SectionKind == Kind.Header || this.SectionKind == Kind.Extension )
        {
            if( block.ContainsOnlyASCII == false )
            {
                throw FITSException.InvalidBlockData( "Headers or extensions must contain only ASCII data" );
            }

            FITSBlock? last = this.Blocks.LastOrDefault();

            if( last is not null )
            {
                if( last.HasEndMarker )
                {
                    throw FITSException.InvalidBlockData( "Cannot append data to a section with an end marker" );
                }

                if( block.HasExtensionMarker )
                {
                    throw FITSException.InvalidBlockData( "Cannot append an extension to a header or extension with existing data" );
                }
            }
        }

        this.Blocks.Add( block );
    }

    /// <summary>
    /// Appends a trailing padding block as-is.
    /// </summary>
    /// <remarks>
    /// Bypasses the header structural rules so blank end-of-file padding round-trips
    /// through <see cref="Data"/> without being parsed as content.
    /// </remarks>
    /// <param name="block">The padding block to retain.</param>
    internal void AppendPadding( FITSBlock block )
    {
        this.Blocks.Add( block );
    }

    /// <summary>
    /// Parses and validates a header or extension section's accumulated blocks.
    /// </summary>
    /// <remarks>
    /// Verifies printability, reads and merges the 80-byte records into
    /// <see cref="Properties"/>, enforces a single <c>END</c> marker, optionally
    /// rejects unknown-typed records, and trims trailing blank records up to the
    /// <c>END</c> marker. Has no effect on data sections. Named
    /// <c>FinalizeSection</c> rather than <c>Finalize</c>, which C# reserves for the
    /// object finalizer.
    /// </remarks>
    /// <param name="options">The parsing options to apply.</param>
    /// <exception cref="FITSException">
    /// The section is already finalized, non-printable, has no or multiple <c>END</c>
    /// markers, or contains an unknown property when not permitted.
    /// </exception>
    internal void FinalizeSection( FITSParsingOptions options )
    {
        if( this.IsFinalized )
        {
            throw FITSException.InvalidSectionData( "Section already finalized" );
        }

        if( this.SectionKind == Kind.Header || this.SectionKind == Kind.Extension )
        {
            ReadOnlyMemory< byte > data = this.RetainedBytes;

            if( options.HasFlag( FITSParsingOptions.AllowNonPrintableHeaderText ) == false && data.ContainsOnlyFITSPrintable() == false )
            {
                throw FITSException.InvalidSectionData( "Header contains non-printable characters" );
            }

            List< FITSProperty > parsed = ReadAndMergeProperties( data, options );

            if( parsed.Count( property => property.Name == "END" ) > 1 )
            {
                throw FITSException.InvalidSectionData( "Multiple end markers found" );
            }

            int endIndex = parsed.FindIndex( property => property.Name == "END" );

            if( endIndex < 0 )
            {
                throw FITSException.InvalidSectionData( "No end marker found" );
            }

            if( options.HasFlag( FITSParsingOptions.AllowUnknownProperties ) == false )
            {
                FITSProperty? unknown = parsed.FirstOrDefault( property => property.Value.Kind == FITSValueKind.Unknown );

                if( unknown is not null )
                {
                    throw FITSException.InvalidSectionData( $"Unknown property found: { unknown.Name }" );
                }
            }

            // FITS 4.0 allows only blank padding after END. AllowContentAfterEnd
            // keeps the noncompliant records out of the properties but tolerates them.
            if( options.HasFlag( FITSParsingOptions.AllowContentAfterEnd ) == false && parsed.Skip( endIndex + 1 ).Any( IsNonBlank ) )
            {
                throw FITSException.InvalidSectionData( "Non-blank content found after END marker" );
            }

            // Keep everything up to the last non-blank record, dropping trailing
            // blanks before END. With no non-blank record the section is empty.
            List< FITSProperty > beforeEnd    = parsed.Take( endIndex ).ToList();
            int                  lastNonEmpty = beforeEnd.FindLastIndex( IsNonBlank );

            this.PropertyList = beforeEnd.Take( lastNonEmpty + 1 ).ToList();

            // Attach each parsed property to this section so that editing its value
            // or comment in place marks the section as needing re-serialization.
            // Setting the back-reference does not itself dirty the section, so a
            // parsed-but-unmodified section stays clean.
            foreach( FITSProperty property in this.PropertyList )
            {
                property.Section = this;
            }
        }

        this.IsFinalized = true;
    }

    /// <summary>
    /// Splits header data into 80-byte records and merges continuation records.
    /// </summary>
    /// <remarks>
    /// Each 80-byte chunk becomes a <see cref="FITSProperty"/>; <c>CONTINUE</c>,
    /// <c>HISTORY</c> and <c>COMMENT</c> records are folded into their predecessor
    /// when the corresponding merge option is enabled.
    /// </remarks>
    /// <param name="data">The full ASCII bytes of the header or extension.</param>
    /// <param name="options">The parsing options to apply.</param>
    /// <returns>The parsed (and merged) properties in file order.</returns>
    /// <exception cref="FITSException">
    /// A record cannot be parsed, or a continuation record has no predecessor to
    /// merge into.
    /// </exception>
    private static List< FITSProperty > ReadAndMergeProperties( ReadOnlyMemory< byte > data, FITSParsingOptions options )
    {
        List< FITSProperty > merged = new List< FITSProperty >();

        foreach( ReadOnlyMemory< byte > record in data.Chunked( FITSFile.CardSize ) )
        {
            FITSProperty  property = new FITSProperty( record, options );
            FITSProperty? last     = merged.LastOrDefault();

            if( property.Name == "CONTINUE" && options.HasFlag( FITSParsingOptions.MergeStringProperties ) )
            {
                MergeContinue( merged, last, property, options );
            }
            else if( last is not null && last.Name == "HISTORY" && property.Name == "HISTORY" && options.HasFlag( FITSParsingOptions.MergeHistoryProperties ) )
            {
                last.Merge( property );
            }
            else if( last is not null && last.Name == "COMMENT" && property.Name == "COMMENT" && options.HasFlag( FITSParsingOptions.MergeCommentProperties ) )
            {
                last.Merge( property );
            }
            else
            {
                merged.Add( property );
            }
        }

        return merged;
    }

    /// <summary>
    /// Folds a <c>CONTINUE</c> record into its predecessor, or keeps it standalone.
    /// </summary>
    /// <remarks>
    /// A <c>CONTINUE</c> with no predecessor, or one whose predecessor is not a
    /// <c>&amp;</c>-terminated string, cannot be merged.
    /// <see cref="FITSParsingOptions.AllowOrphanedContinue"/> keeps it as a
    /// standalone property rather than rejecting the section.
    /// </remarks>
    /// <param name="merged">The accumulated properties, appended to when orphaned.</param>
    /// <param name="last">The predecessor to merge into, or <c>null</c> if there is none.</param>
    /// <param name="continuation">The <c>CONTINUE</c> record to merge.</param>
    /// <param name="options">The parsing options to apply.</param>
    /// <exception cref="FITSException">
    /// The record cannot be merged and orphaned continuations are not permitted.
    /// </exception>
    private static void MergeContinue( List< FITSProperty > merged, FITSProperty? last, FITSProperty continuation, FITSParsingOptions options )
    {
        try
        {
            if( last is null )
            {
                throw FITSException.InvalidSectionData( "No previous property to continue" );
            }

            last.Merge( continuation );
        }
        catch( FITSException )
        {
            if( options.HasFlag( FITSParsingOptions.AllowOrphanedContinue ) == false )
            {
                throw;
            }

            merged.Add( continuation );
        }
    }

    /// <summary>
    /// Reports whether a property carries any content, for the trailing-blank
    /// trimming and the content-after-<c>END</c> check.
    /// </summary>
    /// <param name="property">The property to inspect.</param>
    /// <returns>
    /// <c>true</c> when the property has a name, a defined value, or a comment.
    /// </returns>
    private static bool IsNonBlank( FITSProperty property ) => property.Name.Length != 0 || property.Value.Kind != FITSValueKind.Undefined || property.Comment is not null;

    /// <summary>Returns a multi-line, human-readable summary of the section.</summary>
    /// <returns>The formatted description.</returns>
    public override string ToString() => this.ToString( 0 );

    /// <summary>
    /// Returns a multi-line, human-readable summary of the section, indented for
    /// nesting.
    /// </summary>
    /// <param name="indent">The indentation depth, in units of four spaces.</param>
    /// <returns>The formatted description.</returns>
    public string ToString( int indent )
    {
        string pad        = new string( ' ', indent * 4 );
        int    dataSize   = this.DataSize;
        string properties = this.PropertyList.Count == 0 ? "" : this.DescribeProperties( pad );

        return $"{ pad }FITSSection \n"
             + $"{ pad }{{\n"
             + $"{ pad }    Kind:       { this.SectionKind }\n"
             + $"{ pad }    Chunks:     { this.Blocks.Count.ToString( CultureInfo.InvariantCulture ) }\n"
             + $"{ pad }    Data Size:  { dataSize.ToString( CultureInfo.InvariantCulture ) }{ properties }\n"
             + $"{ pad }}}";
    }

    /// <summary>
    /// Renders the section's properties as an indented, bracketed block for
    /// <see cref="ToString(int)"/>.
    /// </summary>
    /// <param name="pad">The base indentation of the enclosing section summary.</param>
    /// <returns>The formatted properties block, beginning with a leading newline.</returns>
    private string DescribeProperties( string pad )
    {
        string joined = string.Join( $"\n{ pad }        ", this.PropertyList.Select( property => property.ToString() ) );

        return $"\n{ pad }    Properties:\n{ pad }    [\n{ pad }        { joined }\n{ pad }    ]";
    }
}
