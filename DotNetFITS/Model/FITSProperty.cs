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
using System.Text;
using System.Text.RegularExpressions;

namespace DotNetFITS;

/// <summary>
/// A single FITS header record: a keyword, its optional value and comment.
/// </summary>
/// <remarks>
/// Each property corresponds to one 80-byte header record. Special keywords
/// (<c>COMMENT</c>, <c>HISTORY</c>, <c>CONTINUE</c>) and the blank keyword are
/// handled during parsing, and related records can be merged together via
/// <see cref="Merge(FITSProperty)"/>.
/// </remarks>
public sealed partial class FITSProperty
{
    /// <summary>The active <see cref="Value"/> payload backing store.</summary>
    private FITSValue StoredValue { get; set; }

    /// <summary>The active <see cref="Comment"/> payload backing store.</summary>
    private string? StoredComment { get; set; }

    /// <summary>
    /// The keyword name, with trailing padding removed.
    /// </summary>
    /// <remarks>
    /// The name is fixed at construction: it is validated (and, under a lenient
    /// serialization option, coerced) when the property is created, and cannot be
    /// changed afterwards. To use a different keyword, build a new property.
    /// </remarks>
    public string Name { get; private set; }

    /// <summary>
    /// The value of the record.
    /// </summary>
    /// <remarks>
    /// Settable so a constructed or parsed property can be edited in place. Any
    /// <see cref="FITSValue"/> is accepted; a value that cannot be rendered (for
    /// example a non-finite float) is rejected later, on serialization. Editing it
    /// to a different value marks the owning <see cref="FITSSection"/> (if any) as
    /// needing re-serialization; reassigning an equal value does not, so an
    /// otherwise-untouched parsed section keeps re-emitting its retained bytes
    /// byte-for-byte.
    /// </remarks>
    public FITSValue Value
    {
        get => this.StoredValue;
        set
        {
            if( this.StoredValue == value )
            {
                return;
            }

            this.StoredValue = value;
            this.Section?.MarkNeedsSerialization();
        }
    }

    /// <summary>
    /// The record's comment, or <c>null</c> when there is none.
    /// </summary>
    /// <remarks>
    /// Settable so a constructed or parsed property can be edited in place. For a
    /// commentary keyword (<c>COMMENT</c>, <c>HISTORY</c> or the blank keyword) the
    /// comment is the record's only payload, and embedded newlines render as one
    /// card per line on serialization. Editing it to a different comment marks the
    /// owning <see cref="FITSSection"/> (if any) as needing re-serialization.
    /// </remarks>
    public string? Comment
    {
        get => this.StoredComment;
        set
        {
            if( this.StoredComment == value )
            {
                return;
            }

            this.StoredComment = value;
            this.Section?.MarkNeedsSerialization();
        }
    }

    /// <summary>
    /// The section that owns this property, or <c>null</c> if it belongs to none.
    /// </summary>
    /// <remarks>
    /// Set when the property is added to a <see cref="FITSSection"/> and used so
    /// that editing <see cref="Value"/> or <see cref="Comment"/> in place marks that
    /// section as needing re-serialization. A plain (non-weak) reference is correct:
    /// the garbage collector reclaims the property/section cycle.
    /// </remarks>
    internal FITSSection? Section { get; set; }

    /// <summary>
    /// Creates a property from one 80-byte record of ASCII data.
    /// </summary>
    /// <param name="data">The 80 bytes of the record. Must be valid ASCII.</param>
    /// <param name="options">The parsing options to apply.</param>
    /// <exception cref="FITSException">
    /// The data is not ASCII or the record is malformed.
    /// </exception>
    public FITSProperty( ReadOnlyMemory< byte > data, FITSParsingOptions options ) : this( DecodeRecord( data ), options )
    {}

    /// <summary>
    /// Creates a property by parsing one 80-character ASCII header record.
    /// </summary>
    /// <remarks>
    /// The first eight characters are the keyword name; the remainder holds the
    /// value and/or comment, parsed according to the keyword and
    /// <paramref name="options"/>. <c>COMMENT</c>, <c>HISTORY</c> and the blank
    /// keyword carry only a comment. The record must be ASCII, so its character
    /// count coincides with the 80-byte record length.
    /// </remarks>
    /// <param name="record">The record text. Must be exactly 80 ASCII characters.</param>
    /// <param name="options">The parsing options to apply.</param>
    /// <exception cref="FITSException">
    /// The record is not 80 characters, is not ASCII, or cannot be parsed.
    /// </exception>
    public FITSProperty( string record, FITSParsingOptions options )
    {
        if( record.Length != FITSFile.CardSize )
        {
            throw FITSException.InvalidPropertyData( $"Invalid property data length ({ record.Length.ToString( CultureInfo.InvariantCulture ) })" );
        }

        if( record.All( char.IsAscii ) == false )
        {
            throw FITSException.InvalidPropertyData( "Record must be ASCII" );
        }

        string name = ParseName( record.Substring( 0, FITSFile.KeywordLength ), options );

        if( name == "HISTORY" || name == "COMMENT" || name.Length == 0 )
        {
            this.Name          = name;
            this.StoredValue   = FITSValue.Undefined;
            this.StoredComment = ParseCommentOnly( record.Substring( FITSFile.KeywordLength ), options );
        }
        else
        {
            ( FITSValue value, string? comment ) = ParseValueAndComment( name, record.Substring( FITSFile.KeywordLength ), options );

            this.Name          = name;
            this.StoredValue   = value;
            this.StoredComment = comment;
        }
    }

    /// <summary>
    /// Creates a property by parsing a keyword's raw FITS value field.
    /// </summary>
    /// <remarks>
    /// Interprets an already-formatted value field - a quoted string, a number, or a
    /// logical <c>T</c>/<c>F</c> - with the same value parser the full-card
    /// initializer uses, without reconstructing an 80-character card, and so with no
    /// length limit on the field. No <c>=</c> value indicator is expected. A
    /// <c>null</c> field yields an undefined value.
    /// </remarks>
    /// <param name="name">
    /// The keyword name, validated against the FITS keyword character set.
    /// </param>
    /// <param name="rawValue">
    /// The raw FITS value field, or <c>null</c> for a value-less keyword.
    /// </param>
    /// <param name="options">The parsing options governing value interpretation.</param>
    /// <param name="comment">
    /// The keyword's comment. When <c>null</c>, a comment parsed from the field is
    /// kept instead; when given, it takes precedence.
    /// </param>
    /// <exception cref="FITSException">
    /// The name is not a valid keyword or the value field is malformed.
    /// </exception>
    public FITSProperty( string name, string? rawValue, FITSParsingOptions options, string? comment = null )
    {
        string parsedName = ParseName( name, options );

        if( rawValue is null )
        {
            this.Name          = parsedName;
            this.StoredValue   = FITSValue.Undefined;
            this.StoredComment = comment;

            return;
        }

        ( FITSValue value, string? parsedComment ) = ParseValueAndComment( parsedName, $"= { rawValue }", options );

        this.Name          = parsedName;
        this.StoredValue   = value;
        this.StoredComment = comment ?? parsedComment;
    }

    /// <summary>
    /// Creates a property from a keyword, a value and an optional comment.
    /// </summary>
    /// <remarks>
    /// The building block for constructing header records from scratch and for
    /// editing parsed ones. The keyword is validated via
    /// <see cref="NormalizedKeyword(string, FITSSerializationOptions)"/>: a strict
    /// option rejects an out-of-charset or over-length name, while a lenient option
    /// upper-cases an otherwise-valid name. The blank and commentary keywords are
    /// accepted.
    /// </remarks>
    /// <param name="name">The keyword name.</param>
    /// <param name="value">
    /// The record's value; use <see cref="FITSValue.Undefined"/> for a keyword that
    /// carries no value.
    /// </param>
    /// <param name="options">The serialization options governing keyword validation.</param>
    /// <param name="comment">The record's comment, or <c>null</c> for none.</param>
    /// <exception cref="FITSException">
    /// The keyword is invalid and cannot be coerced.
    /// </exception>
    public FITSProperty( string name, FITSValue value, FITSSerializationOptions options, string? comment = null )
    {
        this.Name          = NormalizedKeyword( name, options );
        this.StoredValue   = value;
        this.StoredComment = comment;
    }

    /// <summary>
    /// Creates a property holding a logical (boolean) value.
    /// </summary>
    /// <param name="name">The keyword name.</param>
    /// <param name="logical">The boolean value, rendered <c>T</c> or <c>F</c>.</param>
    /// <param name="options">The serialization options governing keyword validation.</param>
    /// <param name="comment">The record's comment, or <c>null</c> for none.</param>
    /// <exception cref="FITSException">
    /// The keyword is invalid and cannot be coerced.
    /// </exception>
    public FITSProperty( string name, bool logical, FITSSerializationOptions options, string? comment = null ) : this( name, FITSValue.Logical( logical ), options, comment )
    {}

    /// <summary>
    /// Creates a property holding an integer value.
    /// </summary>
    /// <param name="name">The keyword name.</param>
    /// <param name="integer">The integer value.</param>
    /// <param name="options">The serialization options governing keyword validation.</param>
    /// <param name="comment">The record's comment, or <c>null</c> for none.</param>
    /// <exception cref="FITSException">
    /// The keyword is invalid and cannot be coerced.
    /// </exception>
    public FITSProperty( string name, long integer, FITSSerializationOptions options, string? comment = null ) : this( name, FITSValue.Integer( integer ), options, comment )
    {}

    /// <summary>
    /// Creates a property holding a floating-point value.
    /// </summary>
    /// <param name="name">The keyword name.</param>
    /// <param name="floatingPoint">
    /// The floating-point value. A non-finite value is accepted here but rejected on
    /// serialization.
    /// </param>
    /// <param name="options">The serialization options governing keyword validation.</param>
    /// <param name="comment">The record's comment, or <c>null</c> for none.</param>
    /// <exception cref="FITSException">
    /// The keyword is invalid and cannot be coerced.
    /// </exception>
    public FITSProperty( string name, double floatingPoint, FITSSerializationOptions options, string? comment = null ) : this( name, FITSValue.Float( floatingPoint ), options, comment )
    {}

    /// <summary>
    /// Creates a property holding a string value.
    /// </summary>
    /// <param name="name">The keyword name.</param>
    /// <param name="text">
    /// The string value; it is single-quoted and, when too long for one card, split
    /// across <c>CONTINUE</c> records on serialization.
    /// </param>
    /// <param name="options">The serialization options governing keyword validation.</param>
    /// <param name="comment">The record's comment, or <c>null</c> for none.</param>
    /// <exception cref="FITSException">
    /// The keyword is invalid and cannot be coerced.
    /// </exception>
    public FITSProperty( string name, string text, FITSSerializationOptions options, string? comment = null ) : this( name, FITSValue.String( text ), options, comment )
    {}

    /// <summary>
    /// Merges a continuation record into this property in place.
    /// </summary>
    /// <remarks>
    /// Supports the three multi-record FITS conventions: appending another
    /// <c>HISTORY</c> or <c>COMMENT</c> record's text, and continuing a long string
    /// value via a <c>CONTINUE</c> record (which requires this property's string to
    /// end with the <c>&amp;</c> continuation flag). Comments are joined with
    /// newlines.
    /// </remarks>
    /// <param name="property">
    /// The follow-on record to merge in. Its name must be compatible with this
    /// property's name.
    /// </param>
    /// <exception cref="FITSException">
    /// The two records cannot be merged (mismatched names, wrong value type, or a
    /// missing continuation flag).
    /// </exception>
    internal void Merge( FITSProperty property )
    {
        if( property.Name == "HISTORY" )
        {
            if( this.Name != "HISTORY" )
            {
                throw FITSException.InvalidPropertyData( $"Cannot merge a { this.Name } property with a { property.Name } property" );
            }

            this.Comment = MergedComment( this.Comment, property.Comment );
        }
        else if( property.Name == "COMMENT" )
        {
            if( this.Name != "COMMENT" )
            {
                throw FITSException.InvalidPropertyData( $"Cannot merge a { this.Name } property with a { property.Name } property" );
            }

            this.Comment = MergedComment( this.Comment, property.Comment );
        }
        else if( property.Name == "CONTINUE" )
        {
            string? current  = this.Value.AsString;
            string? addition = property.Value.AsString;

            if( current is null || addition is null )
            {
                throw FITSException.InvalidPropertyData( $"Cannot merge a { this.Name } property with a { property.Name } property - Invalid type" );
            }

            if( current.Length == 0 || current[ ^1 ] != '&' )
            {
                throw FITSException.InvalidPropertyData( $"Cannot merge a { this.Name } property with a { property.Name } property - No continue flag" );
            }

            this.Value   = FITSValue.String( current[ ..^1 ] + addition );
            this.Comment = MergedComment( this.Comment, property.Comment );
        }
        else
        {
            throw FITSException.InvalidPropertyData( $"Cannot merge a { this.Name } property with a { property.Name } property" );
        }
    }

    /// <summary>
    /// Normalizes and validates a keyword name for serialization.
    /// </summary>
    /// <remarks>
    /// A name already within the FITS keyword character set (including the empty
    /// blank keyword) is returned unchanged. Otherwise, if
    /// <see cref="FITSSerializationOptions.CoerceInvalidKeywords"/> is set, the name
    /// is upper-cased and re-checked; a name still outside the set, or longer than
    /// <see cref="FITSFile.KeywordLength"/>, is rejected.
    /// </remarks>
    /// <param name="name">The keyword name to normalize.</param>
    /// <param name="options">The serialization options to apply.</param>
    /// <returns>The normalized keyword name.</returns>
    /// <exception cref="FITSException">
    /// The name is too long or cannot be made valid.
    /// </exception>
    internal static string NormalizedKeyword( string name, FITSSerializationOptions options )
    {
        if( name.Length > FITSFile.KeywordLength )
        {
            throw FITSException.CannotSerialize( $"Keyword name exceeds { FITSFile.KeywordLength.ToString( CultureInfo.InvariantCulture ) } characters: { name }" );
        }

        if( name.All( FITSCharacterSet.IsKeyword ) )
        {
            return name;
        }

        if( options.HasFlag( FITSSerializationOptions.CoerceInvalidKeywords ) == false )
        {
            throw FITSException.CannotSerialize( $"Invalid keyword name: { name }" );
        }

        string coerced = name.ToUpperInvariant();

        if( coerced.Length <= FITSFile.KeywordLength && coerced.All( FITSCharacterSet.IsKeyword ) )
        {
            return coerced;
        }

        throw FITSException.CannotSerialize( $"Invalid keyword name: { name }" );
    }

    /// <summary>
    /// Joins two optional comments with a newline, ignoring <c>null</c> sides.
    /// </summary>
    /// <param name="lhs">The first comment, or <c>null</c>.</param>
    /// <param name="rhs">The second comment, or <c>null</c>.</param>
    /// <returns>The joined comment, or <c>null</c> if both sides are <c>null</c>.</returns>
    private static string? MergedComment( string? lhs, string? rhs )
    {
        string[] parts = new[] { lhs, rhs }.OfType< string >().ToArray();

        return parts.Length == 0 ? null : string.Join( "\n", parts );
    }

    /// <summary>
    /// Decodes an 80-byte record into an ASCII string.
    /// </summary>
    /// <param name="data">The record bytes.</param>
    /// <returns>The decoded ASCII string.</returns>
    /// <exception cref="FITSException"><paramref name="data"/> is not ASCII.</exception>
    private static string DecodeRecord( ReadOnlyMemory< byte > data )
    {
        if( data.ContainsOnlyASCII() == false )
        {
            throw FITSException.InvalidPropertyData( "Invalid ASCII data" );
        }

        return Encoding.ASCII.GetString( data.Span );
    }

    /// <summary>
    /// Parses and validates a keyword name.
    /// </summary>
    /// <remarks>
    /// Only base FITS 4.0 keywords are recognized. Names must be left-justified: a
    /// leading space is not a keyword character, so a non-left-justified name is
    /// rejected.
    /// </remarks>
    /// <param name="field">The keyword field.</param>
    /// <param name="options">The parsing options to apply.</param>
    /// <returns>The keyword name with trailing padding removed.</returns>
    /// <exception cref="FITSException">
    /// The name contains characters outside the FITS keyword set.
    /// </exception>
    private static string ParseName( string field, FITSParsingOptions options )
    {
        Func< char, bool > padding = options.HasFlag( FITSParsingOptions.AllowNulPadding ) ? FITSCharacterSet.IsPaddingWithNul : FITSCharacterSet.IsPadding;
        string             name    = field.RightTrimming( padding );

        if( name.All( FITSCharacterSet.IsKeyword ) == false )
        {
            throw FITSException.InvalidPropertyData( "Invalid property name" );
        }

        return name;
    }

    /// <summary>
    /// Extracts the comment text of a value-less record.
    /// </summary>
    /// <param name="field">The record text following the keyword.</param>
    /// <param name="options">The parsing options to apply.</param>
    /// <returns>The trimmed comment, or <c>null</c> if it is blank.</returns>
    private static string? ParseCommentOnly( string field, FITSParsingOptions options )
    {
        Func< char, bool > padding = options.HasFlag( FITSParsingOptions.AllowNulPaddingInValues ) ? FITSCharacterSet.IsPaddingWithNul : FITSCharacterSet.IsPadding;
        string             trimmed = field.RightTrimming( padding );

        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>
    /// Normalizes the comment text following the <c>/</c> delimiter.
    /// </summary>
    /// <remarks>
    /// A single space conventionally separates <c>/</c> from the comment; it is
    /// dropped while any further leading spaces are kept.
    /// </remarks>
    /// <param name="text">The record text following the <c>/</c> delimiter.</param>
    /// <returns>The comment with one leading delimiter space removed.</returns>
    private static string ParsedComment( string text )
    {
        return text.Length > 0 && text[ 0 ] == ' ' ? text.Substring( 1 ) : text;
    }

    /// <summary>
    /// Parses the value and comment portion of a keyword record.
    /// </summary>
    /// <remarks>
    /// Handles <c>CONTINUE</c> records, the <c>= </c> value indicator (string,
    /// commented and bare values), and records carrying only a comment.
    /// </remarks>
    /// <param name="name">The keyword name, used to special-case <c>CONTINUE</c>.</param>
    /// <param name="field">The record text following the keyword.</param>
    /// <param name="options">The parsing options to apply.</param>
    /// <returns>The parsed value and its optional comment.</returns>
    /// <exception cref="FITSException">The value field is malformed.</exception>
    private static ( FITSValue Value, string? Comment ) ParseValueAndComment( string name, string field, FITSParsingOptions options )
    {
        Func< char, bool > padding = options.HasFlag( FITSParsingOptions.AllowNulPaddingInValues ) ? FITSCharacterSet.IsPaddingWithNul : FITSCharacterSet.IsPadding;
        string             value   = field.RightTrimming( padding );

        if( name == "CONTINUE" )
        {
            if( value.Length < 3 || value[ 0 ] != ' ' || value[ 1 ] != ' ' || value[ 2 ] != '\'' )
            {
                throw FITSException.InvalidPropertyData( "Invalid CONTINUE property" );
            }

            ( string continued, string? continuedComment ) = ParseStringValueAndComment( value.Substring( 2 ), options );

            return ( FITSValue.String( continued ), continuedComment );
        }

        if( value.Length >= 1 && value[ 0 ] == '=' )
        {
            return ParseIndicatedValueAndComment( value, options );
        }

        int slash = value.IndexOf( '/' );

        if( slash >= 0 )
        {
            return ( FITSValue.Undefined, ParsedComment( value.Substring( slash + 1 ) ) );
        }

        return ( FITSValue.Undefined, value.Length == 0 ? null : value );
    }

    /// <summary>
    /// Parses a value field that begins with the <c>=</c> value indicator.
    /// </summary>
    /// <param name="value">The right-trimmed value field, starting with <c>=</c>.</param>
    /// <param name="options">The parsing options to apply.</param>
    /// <returns>The parsed value and its optional comment.</returns>
    /// <exception cref="FITSException">The value field is malformed.</exception>
    private static ( FITSValue Value, string? Comment ) ParseIndicatedValueAndComment( string value, FITSParsingOptions options )
    {
        if( value.Length < 2 || value[ 1 ] != ' ' )
        {
            // "=" not followed by a space. A bare "=" (trimmed from "= ") is a
            // value-less record; "=x" means the mandatory space is missing, which
            // strict parsing rejects.
            if( value.Length >= 2 && options.HasFlag( FITSParsingOptions.AllowMissingValueIndicatorSpace ) == false )
            {
                throw FITSException.InvalidPropertyData( "Missing space after value indicator" );
            }

            string comment = value.Substring( 1 );

            return ( FITSValue.Undefined, comment.Length == 0 ? null : comment );
        }

        // The field is right-trimmed and has a space at index 1 that is not the last
        // character, so dropping the leading "= " always leaves a non-empty value.
        string data = value.Substring( 2 );

        if( data.Length > 0 && data[ 0 ] == '\'' )
        {
            ( string text, string? comment ) = ParseStringValueAndComment( data, options );

            return ( FITSValue.String( text ), comment );
        }

        int slash = data.IndexOf( '/' );

        if( slash >= 0 )
        {
            FITSValue commented = ParseNonStringValue( data.Substring( 0, slash ), options );

            return ( commented, ParsedComment( data.Substring( slash + 1 ) ) );
        }

        return ( ParseNonStringValue( data, options ), null );
    }

    /// <summary>
    /// Parses a quoted string value and its trailing comment.
    /// </summary>
    /// <remarks>
    /// Handles FITS string escaping (a doubled <c>''</c> denotes a literal quote),
    /// preserves a single significant space, and trims insignificant trailing
    /// spaces. In strict mode the characters between the closing quote and the
    /// optional <c>/</c> comment delimiter must be blank;
    /// <see cref="FITSParsingOptions.AllowTrailingQuoteJunk"/> relaxes this.
    /// </remarks>
    /// <param name="data">The value field beginning with the opening quote.</param>
    /// <param name="options">The parsing options to apply.</param>
    /// <returns>The unescaped string value and its optional comment.</returns>
    /// <exception cref="FITSException">
    /// A quote is missing or, in strict mode, unexpected characters follow the
    /// closing quote.
    /// </exception>
    private static ( string Value, string? Comment ) ParseStringValueAndComment( string data, FITSParsingOptions options )
    {
        if( data.Length == 0 || data[ 0 ] != '\'' )
        {
            throw FITSException.InvalidPropertyData( "Missing start quote" );
        }

        StringBuilder builder = new StringBuilder();
        int           index   = 1;

        while( index < data.Length )
        {
            if( data[ index ] == '\'' )
            {
                int next = index + 1;

                if( next < data.Length && data[ next ] == '\'' )
                {
                    builder.Append( '\'' );

                    index = next + 1;
                }
                else
                {
                    break;
                }
            }
            else
            {
                builder.Append( data[ index ] );

                index += 1;
            }
        }

        if( index == data.Length )
        {
            throw FITSException.InvalidPropertyData( "Missing end quote" );
        }

        string raw        = builder.ToString();
        string afterQuote = data.Substring( index + 1 );
        string value      = raw.Length == 0 ? "" : raw.All( character => character == ' ' ) ? " " : raw.RightTrimming( character => character == ' ' );
        bool   allowJunk  = options.HasFlag( FITSParsingOptions.AllowTrailingQuoteJunk );
        int    slash      = afterQuote.IndexOf( '/' );

        if( slash >= 0 )
        {
            if( allowJunk == false && afterQuote.Substring( 0, slash ).All( character => character == ' ' ) == false )
            {
                throw FITSException.InvalidPropertyData( "Unexpected characters after closing quote" );
            }

            return ( value, ParsedComment( afterQuote.Substring( slash + 1 ) ) );
        }

        if( allowJunk == false && afterQuote.All( character => character == ' ' ) == false )
        {
            throw FITSException.InvalidPropertyData( "Unexpected characters after closing quote" );
        }

        return ( value, null );
    }

    /// <summary>
    /// Classifies a non-string value field into a <see cref="FITSValue"/>.
    /// </summary>
    /// <remarks>
    /// Tries, in order: logical (<c>T</c>/<c>F</c>), integer, then floating point. An
    /// empty field becomes undefined; anything matching no grammar (or a numeric
    /// literal that overflows its type) becomes an unknown value preserving the
    /// original literal.
    /// </remarks>
    /// <param name="data">The value field with surrounding padding.</param>
    /// <param name="options">The parsing options to apply.</param>
    /// <returns>The classified value.</returns>
    private static FITSValue ParseNonStringValue( string data, FITSParsingOptions options )
    {
        string trimmed = data.Trim( ' ' );

        if( trimmed.Length == 0 )
        {
            return FITSValue.Undefined;
        }

        bool? logical = AsLogical( trimmed );

        if( logical is not null )
        {
            return FITSValue.Logical( logical.Value );
        }

        if( MatchesInteger( trimmed ) )
        {
            if( long.TryParse( trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer ) )
            {
                return FITSValue.Integer( integer );
            }

            // Matches the integer grammar but overflows: keep the exact literal.
            return FITSValue.Unknown( trimmed );
        }

        double? floatingPoint = AsFloatingPoint( trimmed, options );

        if( floatingPoint is not null )
        {
            // Matches the float grammar but overflows to an infinity: keep the exact
            // literal rather than a meaningless infinity.
            if( double.IsFinite( floatingPoint.Value ) == false )
            {
                return FITSValue.Unknown( trimmed );
            }

            return FITSValue.Float( floatingPoint.Value );
        }

        return FITSValue.Unknown( trimmed );
    }

    /// <summary>
    /// Interprets a value field as a FITS logical.
    /// </summary>
    /// <param name="data">The value field, already trimmed of surrounding padding.</param>
    /// <returns>
    /// <c>true</c> for <c>T</c>, <c>false</c> for <c>F</c>, or <c>null</c> otherwise.
    /// </returns>
    private static bool? AsLogical( string data )
    {
        if( data == "T" )
        {
            return true;
        }

        if( data == "F" )
        {
            return false;
        }

        return null;
    }

    /// <summary>
    /// Reports whether a value field matches the FITS integer grammar.
    /// </summary>
    /// <param name="data">The value field, already trimmed of surrounding padding.</param>
    /// <returns><c>true</c> if the field is a well-formed integer literal.</returns>
    private static bool MatchesInteger( string data )
    {
        return IntegerRegex().IsMatch( data );
    }

    /// <summary>
    /// Interprets a value field as a FITS floating-point number.
    /// </summary>
    /// <remarks>
    /// Accepts the FITS real grammar including <c>E</c>/<c>D</c> exponents, plus
    /// lowercase <c>e</c>/<c>d</c> when
    /// <see cref="FITSParsingOptions.AllowLowercaseExponents"/> is set. The
    /// <c>D</c>/<c>d</c> exponent marker is normalized to <c>E</c> before conversion.
    /// </remarks>
    /// <param name="data">The value field, already trimmed of surrounding padding.</param>
    /// <param name="options">The parsing options to apply.</param>
    /// <returns>The parsed value, or <c>null</c> if it is not a floating-point literal.</returns>
    private static double? AsFloatingPoint( string data, FITSParsingOptions options )
    {
        Regex regex = options.HasFlag( FITSParsingOptions.AllowLowercaseExponents ) ? FloatingPointLowercaseExponentRegex() : FloatingPointRegex();

        if( regex.IsMatch( data ) == false )
        {
            return null;
        }

        string normalized = data.Replace( 'd', 'e' ).Replace( 'D', 'E' );

        if( double.TryParse( normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double value ) )
        {
            return value;
        }

        return null;
    }

    /// <summary>The compiled FITS integer-literal pattern: an optional sign and digits.</summary>
    /// <returns>The shared compiled pattern.</returns>
    [ GeneratedRegex( @"^[+-]?\d+$" ) ]
    private static partial Regex IntegerRegex();

    /// <summary>The compiled FITS floating-point-literal pattern with uppercase exponents.</summary>
    /// <returns>The shared compiled pattern.</returns>
    [ GeneratedRegex( @"^[+-]?(?:\d+\.?\d*|\.\d+)([ED][+-]?\d+)?$" ) ]
    private static partial Regex FloatingPointRegex();

    /// <summary>
    /// The compiled FITS floating-point-literal pattern that also admits lowercase
    /// <c>e</c>/<c>d</c> exponent markers.
    /// </summary>
    /// <returns>The shared compiled pattern.</returns>
    [ GeneratedRegex( @"^[+-]?(?:\d+\.?\d*|\.\d+)([EeDd][+-]?\d+)?$" ) ]
    private static partial Regex FloatingPointLowercaseExponentRegex();

    /// <summary>
    /// Renders this property to one or more standards-compliant 80-byte cards - the
    /// inverse of the record parser.
    /// </summary>
    /// <remarks>
    /// A value keyword yields a single fixed-format card: the keyword left-justified
    /// in the eight-byte field, the <c>= </c> value indicator, the value literal
    /// (scalars right-justified to byte 30, strings opening at byte 11), an optional
    /// <c>/</c> comment, blank-padded to <see cref="FITSFile.CardSize"/> bytes.
    /// <c>COMMENT</c>, <c>HISTORY</c> and the blank keyword yield one commentary card
    /// per line of their comment, and a string value too long for a single card is
    /// split across <c>CONTINUE</c> records.
    /// </remarks>
    /// <param name="options">The serialization options to apply.</param>
    /// <returns>
    /// The rendered cards, each exactly <see cref="FITSFile.CardSize"/> bytes.
    /// </returns>
    /// <exception cref="FITSException">
    /// The keyword is invalid, a record would exceed the card width, or the value
    /// cannot be rendered (for example a non-finite float).
    /// </exception>
    public IReadOnlyList< string > Serialized( FITSSerializationOptions options )
    {
        string name = NormalizedKeyword( this.Name, options );

        if( name == "COMMENT" || name == "HISTORY" || name.Length == 0 )
        {
            return this.SerializedCommentaryCards( name );
        }

        if( name == "CONTINUE" )
        {
            if( this.Value.Kind != FITSValueKind.String )
            {
                throw FITSException.CannotSerialize( "A CONTINUE record requires a string value" );
            }

            return [ PadCard( $"CONTINUE  { this.Value.Serialized() }{ this.SerializedComment() }" ) ];
        }

        if( this.Value.AsString is string text )
        {
            return this.SerializedStringCards( name, text );
        }

        return [ this.SerializedScalarCard( name ) ];
    }

    /// <summary>
    /// Renders a commentary property (<c>COMMENT</c>, <c>HISTORY</c> or the blank
    /// keyword) to one card per line of its comment.
    /// </summary>
    /// <param name="name">The already-normalized keyword name.</param>
    /// <returns>
    /// The commentary cards. A property with no comment yields one card holding just
    /// the keyword field.
    /// </returns>
    /// <exception cref="FITSException">A line does not fit the card width.</exception>
    private IReadOnlyList< string > SerializedCommentaryCards( string name )
    {
        string   field = name.PaddedOrTruncated( FITSFile.KeywordLength );
        string[] lines = this.Comment?.Split( '\n' ) ?? [ "" ];

        return lines.Select( line => PadCard( field + line ) ).ToArray();
    }

    /// <summary>
    /// Renders a non-string value keyword to a single fixed-format card.
    /// </summary>
    /// <param name="name">The already-normalized keyword name.</param>
    /// <returns>The rendered card.</returns>
    /// <exception cref="FITSException">
    /// The record exceeds the card width, or the value cannot be rendered.
    /// </exception>
    private string SerializedScalarCard( string name )
    {
        string field = name.PaddedOrTruncated( FITSFile.KeywordLength );
        string value = this.Value.Kind == FITSValueKind.Undefined ? "" : RightJustified( this.Value.Serialized() );
        string body  = $"{ field }= { value }{ this.SerializedComment() }";

        return PadCard( body );
    }

    /// <summary>
    /// Renders a string value keyword, splitting a value too long for one card across
    /// <c>CONTINUE</c> records per the FITS long-string convention.
    /// </summary>
    /// <param name="name">The already-normalized keyword name.</param>
    /// <param name="text">The string value to render.</param>
    /// <returns>The rendered cards.</returns>
    /// <exception cref="FITSException">
    /// A record exceeds the card width, or the value cannot be rendered.
    /// </exception>
    private IReadOnlyList< string > SerializedStringCards( string name, string text )
    {
        // The XTENSION value must be padded to eight characters (FITS 4.0 section 4.2.1).
        string content = name == "XTENSION" ? text.PaddedOrTruncated( Math.Max( FITSFile.KeywordLength, text.Length ) ) : text;
        string field   = name.PaddedOrTruncated( FITSFile.KeywordLength );
        string literal = FITSValue.String( content ).Serialized();
        string single  = $"{ field }= { literal }{ this.SerializedComment() }";

        if( single.Length <= FITSFile.CardSize )
        {
            return [ PadCard( single ) ];
        }

        return this.SerializedContinuedString( name, content );
    }

    /// <summary>
    /// Splits a long string value into a first value card plus <c>CONTINUE</c>
    /// records, per the FITS long-string convention (FITS 4.0 section 4.2.1).
    /// </summary>
    /// <remarks>
    /// Each substring, once its interior quotes are doubled and it is enclosed in
    /// quotes, fits the value field; every substring but the last carries a trailing
    /// <c>&amp;</c> continuation flag. The comment is placed on the last value card
    /// when it fits there; otherwise every value card is flagged and the comment
    /// trails on its own <c>CONTINUE</c> card carrying an empty string, so a long
    /// value that also has a comment still serializes.
    /// </remarks>
    /// <param name="name">The already-normalized keyword name.</param>
    /// <param name="content">The full string value to split.</param>
    /// <returns>The rendered cards.</returns>
    /// <exception cref="FITSException">
    /// A record exceeds the card width, or the value cannot be rendered.
    /// </exception>
    private IReadOnlyList< string > SerializedContinuedString( string name, string content )
    {
        string                  field    = name.PaddedOrTruncated( FITSFile.KeywordLength );
        string                  comment  = this.SerializedComment();
        IReadOnlyList< string > pieces   = ChunkedStringContent( content );
        string                  lastBody = ContinuationBody( field, pieces.Count - 1, pieces[ pieces.Count - 1 ], false, comment );

        if( lastBody.Length <= FITSFile.CardSize )
        {
            return pieces.Select
            (
                ( piece, index ) =>
                {
                    bool isLast = index == pieces.Count - 1;

                    return PadCard( ContinuationBody( field, index, piece, isLast == false, isLast ? comment : "" ) );
                }
            )
            .ToArray();
        }

        string[] cards = pieces.Select( ( piece, index ) => PadCard( ContinuationBody( field, index, piece, true, "" ) ) ).ToArray();

        return [ ..cards, PadCard( $"CONTINUE  ''{ comment }" ) ];
    }

    /// <summary>
    /// Splits string content into substrings that each fit the value field once
    /// escaped, quoted and flagged.
    /// </summary>
    /// <param name="content">
    /// The string value to split. An empty value yields a single empty substring.
    /// </param>
    /// <returns>The substrings, in order.</returns>
    private static IReadOnlyList< string > ChunkedStringContent( string content )
    {
        // The 10-byte prefix (keyword + "= ", or "CONTINUE" plus two spaces) leaves
        // bytes 11-80 = 70 columns; reserve two for the enclosing quotes and one for
        // the trailing "&" flag, giving 67 content characters.
        int capacity = FITSFile.CardSize - ( FITSFile.KeywordLength + 2 ) - 3;

        if( content.Length == 0 )
        {
            return [ "" ];
        }

        List< string > pieces = new List< string >();

        foreach( char character in content )
        {
            string candidate = ( pieces.Count == 0 ? "" : pieces[ ^1 ] ) + character;
            int    escaped   = candidate.Length + candidate.Count( quote => quote == '\'' );

            if( pieces.Count != 0 && escaped <= capacity )
            {
                pieces[ ^1 ] = candidate;
            }
            else
            {
                pieces.Add( character.ToString() );
            }
        }

        return pieces;
    }

    /// <summary>
    /// Builds one continuation record body from a substring.
    /// </summary>
    /// <param name="field">The padded keyword field used on the first record.</param>
    /// <param name="index">
    /// The substring's position; index 0 is the value card, the rest are
    /// <c>CONTINUE</c> records.
    /// </param>
    /// <param name="piece">The substring content.</param>
    /// <param name="flagged">
    /// Whether to append the <c>&amp;</c> continuation flag before the closing quote.
    /// </param>
    /// <param name="comment">A trailing comment suffix, or the empty string for none.</param>
    /// <returns>The unpadded record body.</returns>
    /// <exception cref="FITSException">The value cannot be rendered.</exception>
    private static string ContinuationBody( string field, int index, string piece, bool flagged, string comment )
    {
        string prefix  = index == 0 ? $"{ field }= " : "CONTINUE  ";
        string literal = FITSValue.String( piece ).Serialized();
        string value   = flagged ? $"{ literal[ ..^1 ] }&'" : literal;

        return $"{ prefix }{ value }{ comment }";
    }

    /// <summary>
    /// Right-justifies a scalar value literal within the fixed-format value field. A
    /// literal already at least <see cref="FITSFile.FixedValueFieldWidth"/> long is
    /// returned unchanged (free-format overflow).
    /// </summary>
    /// <param name="literal">The value literal to place.</param>
    /// <returns>The literal padded on the left to the fixed field width.</returns>
    private static string RightJustified( string literal )
    {
        if( literal.Length >= FITSFile.FixedValueFieldWidth )
        {
            return literal;
        }

        return literal.PadLeft( FITSFile.FixedValueFieldWidth );
    }

    /// <summary>
    /// Renders this property's comment as a card suffix.
    /// </summary>
    /// <remarks>
    /// The single space after the <c>/</c> mirrors the one dropped by the parser, so
    /// the comment round-trips.
    /// </remarks>
    /// <returns>
    /// <c> / &lt;comment&gt;</c> when a comment is present, otherwise the empty string.
    /// </returns>
    private string SerializedComment()
    {
        return this.Comment is null ? "" : $" / { this.Comment }";
    }

    /// <summary>
    /// Pads a rendered record to the full card width, or fails if it is too long.
    /// </summary>
    /// <param name="body">The record text.</param>
    /// <returns>The space-padded <see cref="FITSFile.CardSize"/>-byte card.</returns>
    /// <exception cref="FITSException"><paramref name="body"/> exceeds the card width.</exception>
    private static string PadCard( string body )
    {
        if( body.Length > FITSFile.CardSize )
        {
            throw FITSException.CannotSerialize( $"Record exceeds { FITSFile.CardSize.ToString( CultureInfo.InvariantCulture ) } characters: { body }" );
        }

        return body.PaddedOrTruncated( FITSFile.CardSize );
    }

    /// <summary>Returns a single-line, human-readable summary of the property.</summary>
    /// <returns>The summary string.</returns>
    public override string ToString()
    {
        string name    = this.Name.PaddedOrTruncated( FITSFile.KeywordLength );
        string comment = this.Comment?.Replace( "\n", "\\n", StringComparison.Ordinal ) ?? "<nil>";
        string value   = this.DescribeValue();

        return $"FITSProperty {{ name: { name }, kind: { this.Value.Kind }, value: { value }, comment: { comment } }}";
    }

    /// <summary>
    /// Renders this property's value for its textual summary.
    /// </summary>
    /// <returns>A readable rendering of the value.</returns>
    private string DescribeValue()
    {
        return this.Value.Kind switch
        {
            FITSValueKind.Logical   => ( this.Value.AsLogical ?? false ).ToString(),
            FITSValueKind.Integer   => ( this.Value.AsInteger ?? 0L ).ToString( CultureInfo.InvariantCulture ),
            FITSValueKind.Float     => ( this.Value.AsFloat ?? 0.0 ).ToString( CultureInfo.InvariantCulture ),
            FITSValueKind.String    => this.Value.AsString ?? "",
            FITSValueKind.Unknown   => this.Value.Serialized(),
            FITSValueKind.Undefined => "<nil>",
            _                       => "<nil>",
        };
    }
}
