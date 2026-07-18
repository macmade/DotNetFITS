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
/// Unit tests for the serialization (card-rendering) behavior of
/// <see cref="FITSProperty"/>: the inverse of the record parser, covered
/// separately from its construction, parsing and merge behavior.
/// </summary>
public class FITSPropertySerializationTests
{
    /// <summary>
    /// Pads a record to the full card width with trailing spaces.
    /// </summary>
    /// <param name="value">The record text, at most <see cref="FITSFile.CardSize"/> long.</param>
    /// <returns>The space-padded 80-character card.</returns>
    private static string Pad80( string value )
    {
        return value.PaddedOrTruncated( FITSFile.CardSize );
    }

    /// <summary>
    /// Right-justifies a value literal within the fixed-format value field (bytes
    /// 11-30), i.e. a 20-character field.
    /// </summary>
    /// <param name="literal">The value literal to place.</param>
    /// <returns>
    /// The literal padded on the left to a width of 20, or unchanged if it is
    /// already at least that long.
    /// </returns>
    private static string RightJustified( string literal )
    {
        return literal.Length >= FITSFile.FixedValueFieldWidth ? literal : literal.PadLeft( FITSFile.FixedValueFieldWidth );
    }

    /// <summary>
    /// A card already in the canonical fixed-format layout re-serializes to itself,
    /// byte-for-byte, after being parsed.
    /// </summary>
    [ Fact ]
    public void FixedFormatCardsAreIdempotent()
    {
        string[] cards =
        [
            Pad80( "SIMPLE  = " + RightJustified( "T" )   + " / Standard FITS format" ),
            Pad80( "BITPIX  = " + RightJustified( "-32" ) + " / 32 bit" ),
            Pad80( "NAXIS   = " + RightJustified( "2" ) ),
            Pad80( "SOMEFLT = " + RightJustified( "1.5" ) + " / a float" ),
            Pad80( "OBJECT  = 'M42'" ),
            Pad80( "OBJECT  = 'M42' / the observed object" ),
            Pad80( "COMMENT   FITS is a data format" ),
            Pad80( "HISTORY processed on 2026-07-11" ),
        ];

        foreach( string card in cards )
        {
            FITSProperty property = new FITSProperty( card, FITSParsingOptions.Strict );
            string[]     expected = [ card ];

            Assert.Equal( expected, property.Serialized( FITSSerializationOptions.Strict ) );
        }
    }

    /// <summary>
    /// A logical value is right-justified so it lands in byte 30 (index 29).
    /// </summary>
    [ Fact ]
    public void SerializesLogicalRightJustifiedToColumn30()
    {
        FITSProperty            property = new FITSProperty( Pad80( "SIMPLE  = " + RightJustified( "T" ) ), FITSParsingOptions.Strict );
        IReadOnlyList< string > cards    = property.Serialized( FITSSerializationOptions.Strict );

        Assert.Single( cards );
        Assert.Equal( FITSFile.CardSize, cards[ 0 ].Length );

        Assert.Equal( 'T', cards[ 0 ][ 29 ] );
        Assert.Equal( ' ', cards[ 0 ][ 28 ] );
    }

    /// <summary>
    /// The <c>XTENSION</c> value is padded to a minimum of eight characters.
    /// </summary>
    [ Fact ]
    public void SerializesXtensionValuePaddedToEight()
    {
        FITSProperty property = new FITSProperty( Pad80( "XTENSION= 'IMAGE   '" ), FITSParsingOptions.Strict );
        string[]     expected = [ Pad80( "XTENSION= 'IMAGE   '" ) ];

        Assert.Equal( expected, property.Serialized( FITSSerializationOptions.Strict ) );
    }

    /// <summary>
    /// A merged commentary property renders one card per line of its comment.
    /// </summary>
    [ Fact ]
    public void SerializesMergedCommentaryAsOneCardPerLine()
    {
        FITSProperty property = new FITSProperty( Pad80( "COMMENT line one" ), FITSParsingOptions.Strict );

        property.Merge( new FITSProperty( Pad80( "COMMENT line two" ), FITSParsingOptions.Strict ) );

        string[] expected = [ Pad80( "COMMENT line one" ), Pad80( "COMMENT line two" ) ];

        Assert.Equal( expected, property.Serialized( FITSSerializationOptions.Strict ) );
    }

    /// <summary>
    /// A string value too long for one card splits into a value card plus
    /// <c>CONTINUE</c> records that re-parse into the same value.
    /// </summary>
    [ Fact ]
    public void SerializesLongStringAsContinuedRecords()
    {
        string head = new string( 'A', 60 );
        string tail = new string( 'B', 50 );

        FITSProperty property = new FITSProperty( Pad80( $"LONGSTR = '{ head }&'" ), FITSParsingOptions.Strict );

        property.Merge( new FITSProperty( Pad80( $"CONTINUE  '{ tail }'" ), FITSParsingOptions.Strict ) );

        IReadOnlyList< string > cards = property.Serialized( FITSSerializationOptions.Strict );

        Assert.True( cards.Count >= 2 );
        Assert.All( cards, card => Assert.Equal( FITSFile.CardSize, card.Length ) );
        Assert.StartsWith( "LONGSTR = ", cards[ 0 ], StringComparison.Ordinal );
        Assert.All( cards.Skip( 1 ), card => Assert.StartsWith( "CONTINUE  ", card, StringComparison.Ordinal ) );

        FITSProperty reparsed = new FITSProperty( cards[ 0 ], FITSParsingOptions.Strict );

        foreach( string card in cards.Skip( 1 ) )
        {
            reparsed.Merge( new FITSProperty( card, FITSParsingOptions.Strict ) );
        }

        Assert.Equal( property.Value, reparsed.Value );
        Assert.Equal( head + tail, reparsed.Value.AsString );
    }

    /// <summary>
    /// When a long value fills the last value card, its comment spills onto its own
    /// trailing <c>CONTINUE</c> record, and both the value and comment round-trip.
    /// </summary>
    [ Fact ]
    public void SerializesLongStringWithCommentSpillingToOwnCard()
    {
        string head = new string( 'A', 66 );
        string tail = new string( 'B', 66 );

        FITSProperty property = new FITSProperty( Pad80( $"LONGSTR = '{ head }&'" ), FITSParsingOptions.Strict );

        property.Merge( new FITSProperty( Pad80( $"CONTINUE  '{ tail }&'" ), FITSParsingOptions.Strict ) );
        property.Merge( new FITSProperty( Pad80( "CONTINUE  '' / a trailing note" ), FITSParsingOptions.Strict ) );

        Assert.Equal( head + tail, property.Value.AsString );
        Assert.Equal( "a trailing note", property.Comment );

        IReadOnlyList< string > cards = property.Serialized( FITSSerializationOptions.Strict );

        Assert.All( cards, card => Assert.Equal( FITSFile.CardSize, card.Length ) );

        FITSProperty reparsed = new FITSProperty( cards[ 0 ], FITSParsingOptions.Strict );

        foreach( string card in cards.Skip( 1 ) )
        {
            reparsed.Merge( new FITSProperty( card, FITSParsingOptions.Strict ) );
        }

        Assert.Equal( head + tail, reparsed.Value.AsString );
        Assert.Equal( "a trailing note", reparsed.Comment );
    }

    /// <summary>
    /// When a long value's comment fits on the last value card, it is placed there
    /// rather than spilling onto its own trailing <c>CONTINUE ''</c> record, and both
    /// the value and comment round-trip.
    /// </summary>
    [ Fact ]
    public void SerializesLongStringWithCommentOnLastCard()
    {
        string text = new string( 'A', 70 );

        FITSProperty            property = new FITSProperty( "LONGKEY", text, FITSSerializationOptions.Strict, "hi" );
        IReadOnlyList< string > cards    = property.Serialized( FITSSerializationOptions.Strict );

        // Two cards - a value card plus one CONTINUE - not three: the comment rides
        // on the last value card instead of spilling onto its own record.
        Assert.Equal( 2, cards.Count );
        Assert.All( cards, card => Assert.Equal( FITSFile.CardSize, card.Length ) );
        Assert.StartsWith( "LONGKEY = ", cards[ 0 ], StringComparison.Ordinal );
        Assert.Contains( "/ hi", cards[ cards.Count - 1 ], StringComparison.Ordinal );

        FITSProperty reparsed = new FITSProperty( cards[ 0 ], FITSParsingOptions.Strict );

        foreach( string card in cards.Skip( 1 ) )
        {
            reparsed.Merge( new FITSProperty( card, FITSParsingOptions.Strict ) );
        }

        Assert.Equal( text, reparsed.Value.AsString );
        Assert.Equal( "hi", reparsed.Comment );
    }

    /// <summary>
    /// A long value carrying an interior quote and an ampersand near a chunk
    /// boundary survives escaping and the <c>&amp;</c> continuation flag.
    /// </summary>
    [ Fact ]
    public void SerializesLongStringWithQuotesAndAmpersands()
    {
        string head     = new string( 'A', 55 );
        string tail     = new string( 'B', 55 );
        string expected = head + "'" + "&" + tail;

        FITSProperty property = new FITSProperty( Pad80( $"QANDA   = '{ head }''&&'" ), FITSParsingOptions.Strict );

        property.Merge( new FITSProperty( Pad80( $"CONTINUE  '{ tail }'" ), FITSParsingOptions.Strict ) );

        Assert.Equal( expected, property.Value.AsString );

        IReadOnlyList< string > cards    = property.Serialized( FITSSerializationOptions.Strict );
        FITSProperty            reparsed = new FITSProperty( cards[ 0 ], FITSParsingOptions.Strict );

        foreach( string card in cards.Skip( 1 ) )
        {
            reparsed.Merge( new FITSProperty( card, FITSParsingOptions.Strict ) );
        }

        Assert.Equal( expected, reparsed.Value.AsString );
    }

    /// <summary>
    /// A scalar literal longer than the 20-character fixed field is placed starting
    /// at byte 11 (free-format) rather than right-justified.
    /// </summary>
    [ Fact ]
    public void SerializesLongScalarInFreeFormat()
    {
        FITSProperty property = new FITSProperty( Pad80( "BIGFLOAT= 1.7976931348623157E+308" ), FITSParsingOptions.Strict );

        Assert.Equal( FITSValueKind.Float, property.Value.Kind );

        IReadOnlyList< string > cards = property.Serialized( FITSSerializationOptions.Strict );

        Assert.Single( cards );
        Assert.Equal( FITSFile.CardSize, cards[ 0 ].Length );
        Assert.NotEqual( ' ', cards[ 0 ][ 10 ] );

        FITSProperty reparsed = new FITSProperty( cards[ 0 ], FITSParsingOptions.Strict );

        Assert.Equal( property.Value, reparsed.Value );
    }

    /// <summary>
    /// The null string (<c>''</c>) and the empty string (<c>' '</c>) keep their
    /// distinct representations through serialization.
    /// </summary>
    [ Fact ]
    public void SerializesNullAndEmptyStrings()
    {
        FITSProperty nullString  = new FITSProperty( Pad80( "NULLSTR = ''" ), FITSParsingOptions.Strict );
        FITSProperty emptyString = new FITSProperty( Pad80( "EMPTYSTR= ' '" ), FITSParsingOptions.Strict );

        Assert.Equal( FITSValue.String( "" ), nullString.Value );
        Assert.Equal( FITSValue.String( " " ), emptyString.Value );

        string[] nullExpected  = [ Pad80( "NULLSTR = ''" ) ];
        string[] emptyExpected = [ Pad80( "EMPTYSTR= ' '" ) ];

        Assert.Equal( nullExpected,  nullString.Serialized( FITSSerializationOptions.Strict ) );
        Assert.Equal( emptyExpected, emptyString.Serialized( FITSSerializationOptions.Strict ) );
    }

    /// <summary>
    /// A value-less keyword renders to a single card that re-parses as undefined.
    /// </summary>
    [ Fact ]
    public void SerializesUndefinedValueKeyword()
    {
        FITSProperty property = new FITSProperty( Pad80( "FOO     = " ), FITSParsingOptions.Strict );

        Assert.Equal( FITSValueKind.Undefined, property.Value.Kind );

        IReadOnlyList< string > cards = property.Serialized( FITSSerializationOptions.Strict );

        Assert.Single( cards );

        FITSProperty reparsed = new FITSProperty( cards[ 0 ], FITSParsingOptions.Strict );

        Assert.Equal( "FOO", reparsed.Name );
        Assert.Equal( FITSValueKind.Undefined, reparsed.Value.Kind );
    }

    /// <summary>
    /// A property built from a typed model serializes to its expected fixed-format
    /// card for each value kind.
    /// </summary>
    [ Fact ]
    public void ConstructedPropertySerializesToCard()
    {
        FITSProperty logical       = new FITSProperty( "SIMPLE", true,  FITSSerializationOptions.Strict, "Standard FITS format" );
        FITSProperty integer       = new FITSProperty( "NAXIS",  2L,    FITSSerializationOptions.Strict );
        FITSProperty floatingPoint = new FITSProperty( "BSCALE", 1.5,   FITSSerializationOptions.Strict, "a float" );
        FITSProperty text          = new FITSProperty( "OBJECT", "M42", FITSSerializationOptions.Strict );

        string[] logicalExpected       = [ Pad80( "SIMPLE  = " + RightJustified( "T" )   + " / Standard FITS format" ) ];
        string[] integerExpected       = [ Pad80( "NAXIS   = " + RightJustified( "2" ) ) ];
        string[] floatingPointExpected = [ Pad80( "BSCALE  = " + RightJustified( "1.5" ) + " / a float" ) ];
        string[] textExpected          = [ Pad80( "OBJECT  = 'M42'" ) ];

        Assert.Equal( logicalExpected,       logical.Serialized( FITSSerializationOptions.Strict ) );
        Assert.Equal( integerExpected,       integer.Serialized( FITSSerializationOptions.Strict ) );
        Assert.Equal( floatingPointExpected, floatingPoint.Serialized( FITSSerializationOptions.Strict ) );
        Assert.Equal( textExpected,          text.Serialized( FITSSerializationOptions.Strict ) );
    }

    /// <summary>
    /// Every value kind survives a serialize-then-reparse round trip with its name,
    /// value and comment intact.
    /// </summary>
    [ Fact ]
    public void ConstructedPropertyRoundTripsThroughSerialization()
    {
        FITSProperty[] properties =
        [
            new FITSProperty( "SIMPLE", FITSValue.Logical( true ), FITSSerializationOptions.Strict, "conforms" ),
            new FITSProperty( "NAXIS",  FITSValue.Integer( 2 ),    FITSSerializationOptions.Strict ),
            new FITSProperty( "BSCALE", FITSValue.Float( 1.5 ),    FITSSerializationOptions.Strict ),
            new FITSProperty( "OBJECT", FITSValue.String( "M42" ), FITSSerializationOptions.Strict, "target" ),
            new FITSProperty( "FOO",    FITSValue.Undefined,       FITSSerializationOptions.Strict, "note" ),
        ];

        foreach( FITSProperty property in properties )
        {
            IReadOnlyList< string > cards    = property.Serialized( FITSSerializationOptions.Strict );
            FITSProperty            reparsed = new FITSProperty( cards[ 0 ], FITSParsingOptions.Strict );

            Assert.Equal( property.Name,    reparsed.Name );
            Assert.Equal( property.Value,   reparsed.Value );
            Assert.Equal( property.Comment, reparsed.Comment );
        }
    }

    /// <summary>
    /// A non-finite float is accepted at construction but rejected on serialization,
    /// since FITS has no literal for the IEEE special values.
    /// </summary>
    [ Fact ]
    public void ConstructsNonFiniteFloatButRejectsItOnSerialization()
    {
        double[] values = [ double.PositiveInfinity, double.NegativeInfinity, double.NaN ];

        foreach( double value in values )
        {
            FITSProperty property = new FITSProperty( "BADFLOAT", value, FITSSerializationOptions.Strict );

            Assert.False( double.IsFinite( property.Value.AsFloat ?? 0.0 ) );
            Assert.Throws< FITSException >( () => property.Serialized( FITSSerializationOptions.Strict ) );
        }
    }

    /// <summary>
    /// An unknown value serializes to its retained literal verbatim and re-parses
    /// back to the same value.
    /// </summary>
    [ Fact ]
    public void ConstructsWithUnknownValueThroughDesignatedInitializer()
    {
        FITSProperty property = new FITSProperty( "WEIRD", FITSValue.Unknown( "0x1F" ), FITSSerializationOptions.Strict, "raw" );

        Assert.Equal( FITSValue.Unknown( "0x1F" ), property.Value );

        IReadOnlyList< string > cards    = property.Serialized( FITSSerializationOptions.Strict );
        FITSProperty            reparsed = new FITSProperty( cards[ 0 ], FITSParsingOptions.Strict );

        Assert.Single( cards );
        Assert.Equal( "WEIRD", reparsed.Name );
        Assert.Equal( FITSValue.Unknown( "0x1F" ), reparsed.Value );
        Assert.Equal( "raw", reparsed.Comment );
    }

    /// <summary>
    /// A serialized float card is culture-invariant: under a non-invariant current
    /// culture the value literal keeps its period decimal separator, not the
    /// locale's separator.
    /// </summary>
    [ Fact ]
    public void SerializesFloatCardIsCultureInvariant()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo( "fr-FR" );

            FITSProperty property = new FITSProperty( "BSCALE", 1.5, FITSSerializationOptions.Strict, "a float" );
            string[]     expected = [ Pad80( "BSCALE  = " + RightJustified( "1.5" ) + " / a float" ) ];

            Assert.Equal( expected, property.Serialized( FITSSerializationOptions.Strict ) );
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
