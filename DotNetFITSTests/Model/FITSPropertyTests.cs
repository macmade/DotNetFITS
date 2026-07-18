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
using DotNetFITS;

namespace DotNetFITSTests;

/// <summary>
/// Unit tests for the construction, parsing and merge behavior of
/// <see cref="FITSProperty"/>. Serialization is covered separately.
/// </summary>
public class FITSPropertyTests
{
    /// <summary>
    /// Constructs a full-size record buffer filled with a repeated byte.
    /// </summary>
    /// <param name="fill">The byte to repeat.</param>
    /// <param name="count">The number of bytes.</param>
    /// <returns>The filled buffer.</returns>
    private static byte[] Bytes( byte fill, int count )
    {
        byte[] data = new byte[ count ];

        Array.Fill( data, fill );

        return data;
    }

    /// <summary>
    /// A record parsed from raw bytes rejects non-ASCII and wrong-length data, and
    /// a blank record yields an undefined, comment-less property.
    /// </summary>
    [ Fact ]
    public void ConstructsFromData()
    {
        Assert.Throws< FITSException >( () => new FITSProperty( Bytes( 0xFF, 80 ), FITSParsingOptions.Lenient ) );
        Assert.Throws< FITSException >( () => new FITSProperty( Bytes( 0x20, 79 ), FITSParsingOptions.Lenient ) );
        Assert.Throws< FITSException >( () => new FITSProperty( Bytes( 0x20, 81 ), FITSParsingOptions.Lenient ) );

        FITSProperty property = new FITSProperty( Bytes( 0x20, 80 ), FITSParsingOptions.Lenient );

        Assert.Equal( "", property.Name );
        Assert.Equal( FITSValue.Undefined, property.Value );
        Assert.Null( property.Comment );
        Assert.Equal( FITSValueKind.Undefined, property.Value.Kind );
    }

    /// <summary>
    /// A record parsed from a string rejects non-ASCII and wrong-length text, and a
    /// blank record yields an undefined, comment-less property.
    /// </summary>
    [ Fact ]
    public void ConstructsFromString()
    {
        Assert.Throws< FITSException >( () => new FITSProperty( new string( 'ÿ', 80 ), FITSParsingOptions.Lenient ) );
        Assert.Throws< FITSException >( () => new FITSProperty( new string( ' ', 79 ), FITSParsingOptions.Lenient ) );
        Assert.Throws< FITSException >( () => new FITSProperty( new string( ' ', 81 ), FITSParsingOptions.Lenient ) );

        FITSProperty property = new FITSProperty( new string( ' ', 80 ), FITSParsingOptions.Lenient );

        Assert.Equal( "", property.Name );
        Assert.Equal( FITSValue.Undefined, property.Value );
        Assert.Null( property.Comment );
        Assert.Equal( FITSValueKind.Undefined, property.Value.Kind );
    }

    /// <summary>
    /// A raw value field parses an unquoted string just like a full card.
    /// </summary>
    [ Fact ]
    public void ConstructsFromRawValueParsingAStringUnquoted()
    {
        FITSProperty property = new FITSProperty( "OBJECT", "'M 42'", FITSParsingOptions.Lenient );

        Assert.Equal( "OBJECT", property.Name );
        Assert.Equal( FITSValue.String( "M 42" ), property.Value );
        Assert.Equal( FITSValueKind.String, property.Value.Kind );
    }

    /// <summary>
    /// A raw value field classifies numbers and logicals as it would in a card.
    /// </summary>
    [ Fact ]
    public void ConstructsFromRawValueParsingNumbersAndLogicals()
    {
        Assert.Equal( FITSValue.Integer( 300 ),   new FITSProperty( "EXPTIME", "300",  FITSParsingOptions.Lenient ).Value );
        Assert.Equal( FITSValue.Float( 1.25 ),    new FITSProperty( "GAIN",    "1.25", FITSParsingOptions.Lenient ).Value );
        Assert.Equal( FITSValue.Logical( true ),  new FITSProperty( "SIMPLE",  "T",    FITSParsingOptions.Lenient ).Value );
        Assert.Equal( FITSValue.Logical( false ), new FITSProperty( "EXTEND",  "F",    FITSParsingOptions.Lenient ).Value );
    }

    /// <summary>
    /// A raw string value unescapes doubled interior quotes.
    /// </summary>
    [ Fact ]
    public void ConstructsFromRawValueUnescapingDoubledQuotes()
    {
        FITSProperty property = new FITSProperty( "OBSERVER", "'O''Brien'", FITSParsingOptions.Lenient );

        Assert.Equal( FITSValue.String( "O'Brien" ), property.Value );
    }

    /// <summary>
    /// A raw value field has no card-length limit, so a value far longer than a
    /// card survives intact.
    /// </summary>
    [ Fact ]
    public void ConstructsFromRawValueWithoutCardLengthLimit()
    {
        string text = new string( 'A', 200 );

        FITSProperty property = new FITSProperty( "OBJECT", $"'{ text }'", FITSParsingOptions.Lenient );

        Assert.Equal( FITSValue.String( text ), property.Value );
    }

    /// <summary>
    /// A <c>null</c> raw value yields an undefined value and keeps the explicit
    /// comment.
    /// </summary>
    [ Fact ]
    public void ConstructsFromNilRawValueAsUndefined()
    {
        FITSProperty property = new FITSProperty( "OBJECT", null, FITSParsingOptions.Lenient, "a note" );

        Assert.Equal( "OBJECT", property.Name );
        Assert.Equal( FITSValue.Undefined, property.Value );
        Assert.Equal( "a note", property.Comment );
    }

    /// <summary>
    /// An explicit comment argument takes precedence over one parsed from an
    /// unquoted value field; a <c>null</c> argument falls back to the parsed one.
    /// </summary>
    [ Fact ]
    public void ConstructsFromRawValuePreferringAnExplicitCommentOverAParsedOne()
    {
        FITSProperty explicitComment = new FITSProperty( "EXPTIME", "300 / seconds", FITSParsingOptions.Lenient, "given" );
        FITSProperty parsedComment   = new FITSProperty( "EXPTIME", "300 / seconds", FITSParsingOptions.Lenient );

        Assert.Equal( FITSValue.Integer( 300 ), explicitComment.Value );
        Assert.Equal( "given", explicitComment.Comment );
        Assert.Equal( FITSValue.Integer( 300 ), parsedComment.Value );
        Assert.Equal( "seconds", parsedComment.Comment );
    }

    /// <summary>
    /// A raw value field rejects an invalid keyword name.
    /// </summary>
    [ Fact ]
    public void ConstructsFromRawValueRejectingAnInvalidName()
    {
        Assert.Throws< FITSException >( () => new FITSProperty( "BAD NAME", "1", FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// A record with a non-ASCII character is rejected even when its character
    /// count is 80, while a valid 80-character ASCII record is accepted.
    /// </summary>
    [ Fact ]
    public void ConstructsFromStringRejectingNonAscii()
    {
        string record = "COMMENT é" + new string( ' ', 71 );

        Assert.Equal( 80, record.Length );
        Assert.Throws< FITSException >( () => new FITSProperty( record, FITSParsingOptions.Lenient ) );

        string ascii = "COMMENT Hello" + new string( ' ', 67 );

        Assert.Equal( 80, ascii.Length );

        FITSProperty property = new FITSProperty( ascii, FITSParsingOptions.Lenient );

        Assert.Equal( "COMMENT", property.Name );
    }

    /// <summary>
    /// A wrong-length record reports its length in the error message.
    /// </summary>
    [ Fact ]
    public void ConstructsFromStringReportingLengthInMessage()
    {
        FITSException exception = Assert.Throws< FITSException >( () => new FITSProperty( new string( ' ', 79 ), FITSParsingOptions.Lenient ) );

        Assert.Contains( "Invalid property data length (79)", exception.Message, StringComparison.Ordinal );
    }

    /// <summary>
    /// The keyword name is the trimmed, left-justified first eight characters.
    /// </summary>
    [ Fact ]
    public void ParsesTheKeywordName()
    {
        ( string Data, string Name )[] cases =
        [
            ( "ABCD        ", "ABCD"     ),
            ( "ABCDEFGH    ", "ABCDEFGH" ),
            ( "ABCDEFGHIJKL", "ABCDEFGH" ),
            ( "ABCDEFGH=   ", "ABCDEFGH" ),
        ];

        foreach( ( string data, string name ) in cases )
        {
            FITSProperty property = new FITSProperty( data.PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

            Assert.Equal( name, property.Name );
        }
    }

    /// <summary>
    /// The comment is parsed from the record's value field for every comment form.
    /// </summary>
    [ Fact ]
    public void ParsesTheComment()
    {
        ( string Data, string? Comment )[] cases =
        [
            ( "FOOBAR  = 0                              ", null ),
            ( "FOOBAR  = 0 / This is a comment          ", "This is a comment" ),
            ( "FOOBAR  = 0/ This is a comment           ", "This is a comment" ),
            ( "FOOBAR      / This is a comment          ", "This is a comment" ),
            ( "FOOBAR      /This is a comment           ", "This is a comment" ),
            ( "FOOBAR      /       This is a comment    ", "      This is a comment" ),
            ( "FOOBAR        This is a comment          ", "      This is a comment" ),
            ( "FOOBAR  =This is a comment               ", "This is a comment" ),
            ( "FOOBAR  =/ This is a comment             ", "/ This is a comment" ),
            ( "HISTORY       This is a comment          ", "      This is a comment" ),
            ( "HISTORY /     This is a comment          ", "/     This is a comment" ),
            ( "COMMENT       This is a comment          ", "      This is a comment" ),
            ( "COMMENT /     This is a comment          ", "/     This is a comment" ),
            ( "              This is a comment          ", "      This is a comment" ),
            ( "        /     This is a comment          ", "/     This is a comment" ),
        ];

        foreach( ( string data, string? comment ) in cases )
        {
            FITSProperty property = new FITSProperty( data.PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

            Assert.Equal( comment, property.Comment );
        }
    }

    /// <summary>
    /// A <c>T</c>/<c>F</c> value field is classified as a logical.
    /// </summary>
    [ Fact ]
    public void ParsesLogicalValues()
    {
        ( string Data, bool Value )[] cases =
        [
            ( "FOOBAR  = T", true ),
            ( "FOOBAR  = F", false ),
        ];

        foreach( ( string data, bool value ) in cases )
        {
            FITSProperty property = new FITSProperty( data.PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

            Assert.Equal( FITSValueKind.Logical, property.Value.Kind );
            Assert.Equal( value, property.Value.AsLogical );
        }
    }

    /// <summary>
    /// An integer value field is classified as an integer, tolerating signs and
    /// leading zeros.
    /// </summary>
    [ Fact ]
    public void ParsesIntegerValues()
    {
        ( string Data, long Value )[] cases =
        [
            ( "FOOBAR  = 0   ",   0 ),
            ( "FOOBAR  = 42  ",  42 ),
            ( "FOOBAR  = 042 ",  42 ),
            ( "FOOBAR  = +42 ",  42 ),
            ( "FOOBAR  = -42 ", -42 ),
            ( "FOOBAR  = +042",  42 ),
            ( "FOOBAR  = -042", -42 ),
        ];

        foreach( ( string data, long value ) in cases )
        {
            FITSProperty property = new FITSProperty( data.PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

            Assert.Equal( FITSValueKind.Integer, property.Value.Kind );
            Assert.Equal( value, property.Value.AsInteger );
        }
    }

    /// <summary>
    /// A floating-point value field is classified as a float across the full FITS
    /// real grammar: optional sign, optional integer or fractional part, and an
    /// optional <c>E</c>/<c>D</c> exponent with an optional sign, with or without
    /// leading field padding.
    /// </summary>
    [ Fact ]
    public void ParsesFloatValues()
    {
        ( string Data, double Value )[] cases =
        [
            ( "FOOBAR  = 42.         ",   42.0000 ), ( "FOOBAR  = +42.         ",   42.0000 ), ( "FOOBAR  = -42.         ",   -42.0000 ),
            ( "FOOBAR  = 42.0        ",   42.0000 ), ( "FOOBAR  = +42.0        ",   42.0000 ), ( "FOOBAR  = -42.0        ",   -42.0000 ),
            ( "FOOBAR  = 42.42       ",   42.4200 ), ( "FOOBAR  = +42.42       ",   42.4200 ), ( "FOOBAR  = -42.42       ",   -42.4200 ),
            ( "FOOBAR  = .42         ",    0.4200 ), ( "FOOBAR  = +.42         ",    0.4200 ), ( "FOOBAR  = -.42         ",    -0.4200 ),
            ( "FOOBAR  =     42.     ",   42.0000 ), ( "FOOBAR  =     +42.     ",   42.0000 ), ( "FOOBAR  =     -42.     ",   -42.0000 ),
            ( "FOOBAR  =     42.0    ",   42.0000 ), ( "FOOBAR  =     +42.0    ",   42.0000 ), ( "FOOBAR  =     -42.0    ",   -42.0000 ),
            ( "FOOBAR  =     42.42   ",   42.4200 ), ( "FOOBAR  =     +42.42   ",   42.4200 ), ( "FOOBAR  =     -42.42   ",   -42.4200 ),
            ( "FOOBAR  =     .42     ",    0.4200 ), ( "FOOBAR  =     +.42     ",    0.4200 ), ( "FOOBAR  =     -.42     ",    -0.4200 ),

            ( "FOOBAR  = 42.E2       ", 4200.0000 ), ( "FOOBAR  = +42.E2       ", 4200.0000 ), ( "FOOBAR  = -42.E2       ", -4200.0000 ),
            ( "FOOBAR  = 42.0E2      ", 4200.0000 ), ( "FOOBAR  = +42.0E2      ", 4200.0000 ), ( "FOOBAR  = -42.0E2      ", -4200.0000 ),
            ( "FOOBAR  = 42.42E2     ", 4242.0000 ), ( "FOOBAR  = +42.42E2     ", 4242.0000 ), ( "FOOBAR  = -42.42E2     ", -4242.0000 ),
            ( "FOOBAR  = .42E2       ",   42.0000 ), ( "FOOBAR  = +.42E2       ",   42.0000 ), ( "FOOBAR  = -.42E2       ",   -42.0000 ),
            ( "FOOBAR  =     42.E2   ", 4200.0000 ), ( "FOOBAR  =     +42.E2   ", 4200.0000 ), ( "FOOBAR  =     -42.E2   ", -4200.0000 ),
            ( "FOOBAR  =     42.0E2  ", 4200.0000 ), ( "FOOBAR  =     +42.0E2  ", 4200.0000 ), ( "FOOBAR  =     -42.0E2  ", -4200.0000 ),
            ( "FOOBAR  =     42.42E2 ", 4242.0000 ), ( "FOOBAR  =     +42.42E2 ", 4242.0000 ), ( "FOOBAR  =     -42.42E2 ", -4242.0000 ),
            ( "FOOBAR  =     .42E2   ",   42.0000 ), ( "FOOBAR  =     +.42E2   ",   42.0000 ), ( "FOOBAR  =     -.42E2   ",   -42.0000 ),

            ( "FOOBAR  = 42.E+2      ", 4200.0000 ), ( "FOOBAR  = +42.E+2      ", 4200.0000 ), ( "FOOBAR  = -42.E+2      ", -4200.0000 ),
            ( "FOOBAR  = 42.0E+2     ", 4200.0000 ), ( "FOOBAR  = +42.0E+2     ", 4200.0000 ), ( "FOOBAR  = -42.0E+2     ", -4200.0000 ),
            ( "FOOBAR  = 42.42E+2    ", 4242.0000 ), ( "FOOBAR  = +42.42E+2    ", 4242.0000 ), ( "FOOBAR  = -42.42E+2    ", -4242.0000 ),
            ( "FOOBAR  = .42E+2      ",   42.0000 ), ( "FOOBAR  = +.42E+2      ",   42.0000 ), ( "FOOBAR  = -.42E+2      ",   -42.0000 ),
            ( "FOOBAR  =     42.E+2  ", 4200.0000 ), ( "FOOBAR  =     +42.E+2  ", 4200.0000 ), ( "FOOBAR  =     -42.E+2  ", -4200.0000 ),
            ( "FOOBAR  =     42.0E+2 ", 4200.0000 ), ( "FOOBAR  =     +42.0E+2 ", 4200.0000 ), ( "FOOBAR  =     -42.0E+2 ", -4200.0000 ),
            ( "FOOBAR  =     42.42E+2", 4242.0000 ), ( "FOOBAR  =     +42.42E+2", 4242.0000 ), ( "FOOBAR  =     -42.42E+2", -4242.0000 ),
            ( "FOOBAR  =     .42E+2  ",   42.0000 ), ( "FOOBAR  =     +.42E+2  ",   42.0000 ), ( "FOOBAR  =     -.42E+2  ",   -42.0000 ),

            ( "FOOBAR  = 42.E-2      ",    0.4200 ), ( "FOOBAR  = +42.E-2      ",    0.4200 ), ( "FOOBAR  = -42.E-2      ",    -0.4200 ),
            ( "FOOBAR  = 42.0E-2     ",    0.4200 ), ( "FOOBAR  = +42.0E-2     ",    0.4200 ), ( "FOOBAR  = -42.0E-2     ",    -0.4200 ),
            ( "FOOBAR  = 42.42E-2    ",    0.4242 ), ( "FOOBAR  = +42.42E-2    ",    0.4242 ), ( "FOOBAR  = -42.42E-2    ",    -0.4242 ),
            ( "FOOBAR  = .42E-2      ",    0.0042 ), ( "FOOBAR  = +.42E-2      ",    0.0042 ), ( "FOOBAR  = -.42E-2      ",    -0.0042 ),
            ( "FOOBAR  =     42.E-2  ",    0.4200 ), ( "FOOBAR  =     +42.E-2  ",    0.4200 ), ( "FOOBAR  =     -42.E-2  ",    -0.4200 ),
            ( "FOOBAR  =     42.0E-2 ",    0.4200 ), ( "FOOBAR  =     +42.0E-2 ",    0.4200 ), ( "FOOBAR  =     -42.0E-2 ",    -0.4200 ),
            ( "FOOBAR  =     42.42E-2",    0.4242 ), ( "FOOBAR  =     +42.42E-2",    0.4242 ), ( "FOOBAR  =     -42.42E-2",    -0.4242 ),
            ( "FOOBAR  =     .42E-2  ",    0.0042 ), ( "FOOBAR  =     +.42E-2  ",    0.0042 ), ( "FOOBAR  =     -.42E-2  ",    -0.0042 ),

            ( "FOOBAR  = 42.D2       ", 4200.0000 ), ( "FOOBAR  = +42.D2       ", 4200.0000 ), ( "FOOBAR  = -42.D2       ", -4200.0000 ),
            ( "FOOBAR  = 42.0D2      ", 4200.0000 ), ( "FOOBAR  = +42.0D2      ", 4200.0000 ), ( "FOOBAR  = -42.0D2      ", -4200.0000 ),
            ( "FOOBAR  = 42.42D2     ", 4242.0000 ), ( "FOOBAR  = +42.42D2     ", 4242.0000 ), ( "FOOBAR  = -42.42D2     ", -4242.0000 ),
            ( "FOOBAR  = .42D2       ",   42.0000 ), ( "FOOBAR  = +.42D2       ",   42.0000 ), ( "FOOBAR  = -.42D2       ",   -42.0000 ),
            ( "FOOBAR  =     42.D2   ", 4200.0000 ), ( "FOOBAR  =     +42.D2   ", 4200.0000 ), ( "FOOBAR  =     -42.D2   ", -4200.0000 ),
            ( "FOOBAR  =     42.0D2  ", 4200.0000 ), ( "FOOBAR  =     +42.0D2  ", 4200.0000 ), ( "FOOBAR  =     -42.0D2  ", -4200.0000 ),
            ( "FOOBAR  =     42.42D2 ", 4242.0000 ), ( "FOOBAR  =     +42.42D2 ", 4242.0000 ), ( "FOOBAR  =     -42.42D2 ", -4242.0000 ),
            ( "FOOBAR  =     .42D2   ",   42.0000 ), ( "FOOBAR  =     +.42D2   ",   42.0000 ), ( "FOOBAR  =     -.42D2   ",   -42.0000 ),

            ( "FOOBAR  = 42.D+2      ", 4200.0000 ), ( "FOOBAR  = +42.D+2      ", 4200.0000 ), ( "FOOBAR  = -42.D+2      ", -4200.0000 ),
            ( "FOOBAR  = 42.0D+2     ", 4200.0000 ), ( "FOOBAR  = +42.0D+2     ", 4200.0000 ), ( "FOOBAR  = -42.0D+2     ", -4200.0000 ),
            ( "FOOBAR  = 42.42D+2    ", 4242.0000 ), ( "FOOBAR  = +42.42D+2    ", 4242.0000 ), ( "FOOBAR  = -42.42D+2    ", -4242.0000 ),
            ( "FOOBAR  = .42D+2      ",   42.0000 ), ( "FOOBAR  = +.42D+2      ",   42.0000 ), ( "FOOBAR  = -.42D+2      ",   -42.0000 ),
            ( "FOOBAR  =     42.D+2  ", 4200.0000 ), ( "FOOBAR  =     +42.D+2  ", 4200.0000 ), ( "FOOBAR  =     -42.D+2  ", -4200.0000 ),
            ( "FOOBAR  =     42.0D+2 ", 4200.0000 ), ( "FOOBAR  =     +42.0D+2 ", 4200.0000 ), ( "FOOBAR  =     -42.0D+2 ", -4200.0000 ),
            ( "FOOBAR  =     42.42D+2", 4242.0000 ), ( "FOOBAR  =     +42.42D+2", 4242.0000 ), ( "FOOBAR  =     -42.42D+2", -4242.0000 ),
            ( "FOOBAR  =     .42D+2  ",   42.0000 ), ( "FOOBAR  =     +.42D+2  ",   42.0000 ), ( "FOOBAR  =     -.42D+2  ",   -42.0000 ),

            ( "FOOBAR  = 42.D-2      ",    0.4200 ), ( "FOOBAR  = +42.D-2      ",    0.4200 ), ( "FOOBAR  = -42.D-2      ",    -0.4200 ),
            ( "FOOBAR  = 42.0D-2     ",    0.4200 ), ( "FOOBAR  = +42.0D-2     ",    0.4200 ), ( "FOOBAR  = -42.0D-2     ",    -0.4200 ),
            ( "FOOBAR  = 42.42D-2    ",    0.4242 ), ( "FOOBAR  = +42.42D-2    ",    0.4242 ), ( "FOOBAR  = -42.42D-2    ",    -0.4242 ),
            ( "FOOBAR  = .42D-2      ",    0.0042 ), ( "FOOBAR  = +.42D-2      ",    0.0042 ), ( "FOOBAR  = -.42D-2      ",    -0.0042 ),
            ( "FOOBAR  =     42.D-2  ",    0.4200 ), ( "FOOBAR  =     +42.D-2  ",    0.4200 ), ( "FOOBAR  =     -42.D-2  ",    -0.4200 ),
            ( "FOOBAR  =     42.0D-2 ",    0.4200 ), ( "FOOBAR  =     +42.0D-2 ",    0.4200 ), ( "FOOBAR  =     -42.0D-2 ",    -0.4200 ),
            ( "FOOBAR  =     42.42D-2",    0.4242 ), ( "FOOBAR  =     +42.42D-2",    0.4242 ), ( "FOOBAR  =     -42.42D-2",    -0.4242 ),
            ( "FOOBAR  =     .42D-2  ",    0.0042 ), ( "FOOBAR  =     +.42D-2  ",    0.0042 ), ( "FOOBAR  =     -.42D-2  ",    -0.0042 ),
        ];

        foreach( ( string data, double value ) in cases )
        {
            FITSProperty property = new FITSProperty( data.PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

            Assert.Equal( FITSValueKind.Float, property.Value.Kind );
            Assert.Equal( value, property.Value.AsFloat );
        }
    }

    /// <summary>
    /// A lowercase <c>e</c>/<c>d</c> exponent is kept out of the float grammar in
    /// strict mode, preserving the literal as an unknown value.
    /// </summary>
    [ Fact ]
    public void ParsesLowercaseExponentAsUnknownWhenStrict()
    {
        string[] cases = [ "1.5e3", "1.5d3", "+.42e-2", "-42.0d+2" ];

        foreach( string value in cases )
        {
            FITSProperty property = new FITSProperty( $"FOOBAR  = { value }".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Strict );

            Assert.Equal( FITSValueKind.Unknown, property.Value.Kind );
            Assert.Equal( FITSValue.Unknown( value ), property.Value );
        }
    }

    /// <summary>
    /// A lowercase <c>e</c>/<c>d</c> exponent is admitted into the float grammar
    /// when the leniency flag is set.
    /// </summary>
    [ Fact ]
    public void ParsesLowercaseExponentAsFloatWhenLenient()
    {
        ( string Data, double Value )[] cases =
        [
            ( "1.5e3",    1500.0 ),
            ( "1.5d3",    1500.0 ),
            ( "+.42e-2",  0.0042 ),
            ( "-42.0d+2", -4200.0 ),
        ];

        foreach( ( string data, double value ) in cases )
        {
            FITSProperty property = new FITSProperty( $"FOOBAR  = { data }".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

            Assert.Equal( FITSValueKind.Float, property.Value.Kind );
            Assert.Equal( value, property.Value.AsFloat );
        }
    }

    /// <summary>
    /// An uppercase <c>E</c>/<c>D</c> exponent classifies as a float in both strict
    /// and lenient modes.
    /// </summary>
    [ Fact ]
    public void ParsesUppercaseExponentAsFloatInBothModes()
    {
        ( string Data, double Value )[] cases =
        [
            ( "1.5E3", 1500.0 ),
            ( "1.5D3", 1500.0 ),
        ];

        foreach( ( string data, double value ) in cases )
        {
            FITSProperty strict  = new FITSProperty( $"FOOBAR  = { data }".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Strict );
            FITSProperty lenient = new FITSProperty( $"FOOBAR  = { data }".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

            Assert.Equal( FITSValueKind.Float, strict.Value.Kind );
            Assert.Equal( value, strict.Value.AsFloat );
            Assert.Equal( FITSValueKind.Float, lenient.Value.Kind );
            Assert.Equal( value, lenient.Value.AsFloat );
        }
    }

    /// <summary>
    /// An unknown value is trimmed of its field padding, consistently with the
    /// numeric cases, whether it matches no grammar or overflows its numeric type.
    /// </summary>
    [ Fact ]
    public void ParsesUnknownValueTrimmedConsistentlyWithNumericCases()
    {
        string[] cases = [ "bad", "99999999999999999999", "1E400" ];

        foreach( string value in cases )
        {
            FITSProperty property = new FITSProperty( $"FOOBAR  = { value } / comment".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

            Assert.Equal( FITSValueKind.Unknown, property.Value.Kind );
            Assert.Equal( FITSValue.Unknown( value ), property.Value );
        }
    }

    /// <summary>
    /// A comment normalizes identically whether it follows a string value or a
    /// non-string value.
    /// </summary>
    [ Fact ]
    public void ParsesStringAndNonStringValueCommentsIdentically()
    {
        FITSProperty stringValue  = new FITSProperty( "FOOBAR  = 'hi' /  spaced".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );
        FITSProperty integerValue = new FITSProperty( "FOOBAR  = 1 /  spaced".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

        Assert.Equal( FITSValueKind.String, stringValue.Value.Kind );
        Assert.Equal( FITSValueKind.Integer, integerValue.Value.Kind );
        Assert.Equal( integerValue.Comment, stringValue.Comment );
        Assert.Equal( " spaced", stringValue.Comment );
        Assert.Equal( " spaced", integerValue.Comment );
    }

    /// <summary>
    /// A quoted string value is unquoted, preserving a single significant space and
    /// trimming insignificant trailing spaces; a missing closing quote is rejected.
    /// </summary>
    [ Fact ]
    public void ParsesStringValues()
    {
        ( string Data, string Value )[] cases =
        [
            ( "FOOBAR  = 'hello, world'        ", "hello, world" ),
            ( "FOOBAR  = 'hello, world '       ", "hello, world" ),
            ( "FOOBAR  = '    hello, world'    ", "    hello, world" ),
            ( "FOOBAR  = '    hello, world    '", "    hello, world" ),
            ( "FOOBAR  = ''                    ", "" ),
            ( "FOOBAR  = ' '                   ", " " ),
            ( "FOOBAR  = '    '                ", " " ),
        ];

        foreach( ( string data, string value ) in cases )
        {
            FITSProperty property = new FITSProperty( data.PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

            Assert.Equal( FITSValueKind.String, property.Value.Kind );
            Assert.Equal( value, property.Value.AsString );
        }

        Assert.Throws< FITSException >( () => new FITSProperty( "FOOBAR  = 'hello, world".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// Non-blank characters between the closing quote and the comment delimiter are
    /// rejected in strict mode; a blank gap remains valid.
    /// </summary>
    [ Fact ]
    public void RejectsJunkAfterClosingQuoteWhenStrict()
    {
        Assert.Throws< FITSException >( () => new FITSProperty( "FOOBAR  = 'hi' junk / comment".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Strict ) );
        Assert.Throws< FITSException >( () => new FITSProperty( "FOOBAR  = 'hi' junk".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Strict ) );

        FITSProperty property = new FITSProperty( "FOOBAR  = 'hi'   / comment".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Strict );

        Assert.Equal( "hi", property.Value.AsString );
        Assert.Equal( "comment", property.Comment );
    }

    /// <summary>
    /// Non-blank characters between the closing quote and the comment delimiter are
    /// dropped in non-strict mode, still recovering the value and comment.
    /// </summary>
    [ Fact ]
    public void ToleratesJunkAfterClosingQuoteWhenNonStrict()
    {
        FITSProperty withComment = new FITSProperty( "FOOBAR  = 'hi' junk / comment".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

        Assert.Equal( "hi", withComment.Value.AsString );
        Assert.Equal( "comment", withComment.Comment );

        FITSProperty withoutComment = new FITSProperty( "FOOBAR  = 'hi' junk".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

        Assert.Equal( "hi", withoutComment.Value.AsString );
        Assert.Null( withoutComment.Comment );
    }

    /// <summary>
    /// A record with no value classifies as undefined, keeping any comment.
    /// </summary>
    [ Fact ]
    public void ParsesUndefinedValues()
    {
        ( string Data, string? Comment )[] cases =
        [
            ( "FOOBAR  =                    ", null ),
            ( "FOOBAR  =/ This is a comment ", "/ This is a comment" ),
            ( "FOOBAR  = / This is a comment", "This is a comment" ),
        ];

        foreach( ( string data, string? comment ) in cases )
        {
            FITSProperty property = new FITSProperty( data.PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

            Assert.Equal( FITSValueKind.Undefined, property.Value.Kind );
            Assert.Equal( FITSValue.Undefined, property.Value );
            Assert.Equal( comment, property.Comment );
        }
    }

    /// <summary>
    /// An unknown value is trimmed of its surrounding field padding, regardless of
    /// the spacing around the value or the slash that ends it.
    /// </summary>
    [ Fact ]
    public void ParsesUnknownValues()
    {
        ( string Data, string Value, string? Comment )[] cases =
        [
            ( "FOOBAR  = a",                      "a", null ),
            ( "FOOBAR  = a / This is a comment",  "a", "This is a comment" ),
            ( "FOOBAR  = a/ This is a comment",   "a", "This is a comment" ),
            ( "FOOBAR  =  a",                     "a", null ),
            ( "FOOBAR  =  a / This is a comment", "a", "This is a comment" ),
            ( "FOOBAR  =  a/ This is a comment",  "a", "This is a comment" ),
        ];

        foreach( ( string data, string value, string? comment ) in cases )
        {
            FITSProperty property = new FITSProperty( data.PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

            Assert.Equal( FITSValueKind.Unknown, property.Value.Kind );
            Assert.Equal( FITSValue.Unknown( value ), property.Value );
            Assert.Equal( comment, property.Comment );
        }
    }

    /// <summary>
    /// A literal matching the integer grammar but overflowing a 64-bit integer is
    /// kept verbatim as an unknown value, not reinterpreted as a lossy float.
    /// </summary>
    [ Fact ]
    public void ParsesIntegerOverflowingInt64AsUnknown()
    {
        FITSProperty positive = new FITSProperty( "FOOBAR  = 12345678901234567890".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );
        FITSProperty negative = new FITSProperty( "FOOBAR  = -12345678901234567890".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

        Assert.Equal( FITSValue.Unknown( "12345678901234567890" ), positive.Value );
        Assert.Equal( FITSValue.Unknown( "-12345678901234567890" ), negative.Value );
    }

    /// <summary>
    /// A literal matching the float grammar but overflowing a 64-bit float is kept
    /// verbatim as an unknown value, not turned into an infinity; a large-but-finite
    /// value still parses as a float.
    /// </summary>
    [ Fact ]
    public void ParsesFloatOverflowingDoubleAsUnknown()
    {
        FITSProperty positive = new FITSProperty( "FOOBAR  = 1E400".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );
        FITSProperty negative = new FITSProperty( "FOOBAR  = -1E400".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );
        FITSProperty finite   = new FITSProperty( "FOOBAR  = 1E300".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

        Assert.Equal( FITSValue.Unknown( "1E400" ), positive.Value );
        Assert.Equal( FITSValue.Unknown( "-1E400" ), negative.Value );
        Assert.Equal( FITSValue.Float( 1e300 ), finite.Value );
    }

    /// <summary>
    /// A value field padded with NUL bytes classifies normally only when the
    /// NUL-in-values leniency flag is set.
    /// </summary>
    [ Fact ]
    public void HonorsNulPaddedValueWithFlag()
    {
        string record = ( "FOO     = T" + "\u0000\u0000\u0000" ).PaddedOrTruncated( FITSFile.CardSize );

        FITSProperty withFlag    = new FITSProperty( record, FITSParsingOptions.Strict | FITSParsingOptions.AllowNulPaddingInValues );
        FITSProperty withoutFlag = new FITSProperty( record, FITSParsingOptions.Strict );

        Assert.Equal( FITSValue.Logical( true ), withFlag.Value );
        Assert.Equal( FITSValueKind.Unknown, withoutFlag.Value.Kind );
    }

    /// <summary>
    /// A commentary record's trailing NUL bytes are trimmed only when the
    /// NUL-in-values leniency flag is set.
    /// </summary>
    [ Fact ]
    public void TrimsNulPaddedCommentWithFlag()
    {
        string record = ( "COMMENT Hello" + "\u0000\u0000" ).PaddedOrTruncated( FITSFile.CardSize );

        FITSProperty withFlag    = new FITSProperty( record, FITSParsingOptions.Strict | FITSParsingOptions.AllowNulPaddingInValues );
        FITSProperty withoutFlag = new FITSProperty( record, FITSParsingOptions.Strict );

        Assert.Equal( "Hello", withFlag.Comment );
        Assert.Equal( "Hello\u0000\u0000", withoutFlag.Comment );
    }

    /// <summary>
    /// A value indicator without its mandatory following space is rejected in
    /// strict mode.
    /// </summary>
    [ Fact ]
    public void RejectsMissingValueIndicatorSpaceWhenStrict()
    {
        string record = "FOOBAR  =T".PaddedOrTruncated( FITSFile.CardSize );

        Assert.Throws< FITSException >( () => new FITSProperty( record, FITSParsingOptions.Strict ) );
    }

    /// <summary>
    /// A value indicator without its mandatory following space is tolerated in
    /// lenient mode, reclassifying the remainder as a comment.
    /// </summary>
    [ Fact ]
    public void ToleratesMissingValueIndicatorSpaceWhenLenient()
    {
        string record = "FOOBAR  =T".PaddedOrTruncated( FITSFile.CardSize );

        FITSProperty property = new FITSProperty( record, FITSParsingOptions.Lenient );

        Assert.Equal( FITSValueKind.Undefined, property.Value.Kind );
        Assert.Equal( "T", property.Comment );
    }

    /// <summary>
    /// A comment-only record and an empty value still parse cleanly in strict mode;
    /// only a present-but-spaceless value indicator is rejected.
    /// </summary>
    [ Fact ]
    public void ParsesCommentOnlyRecordUnaffectedByValueIndicatorCheck()
    {
        FITSProperty commentOnly = new FITSProperty( "FOOBAR  = / This is a comment".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Strict );
        FITSProperty emptyValue  = new FITSProperty( "FOOBAR  = ".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Strict );

        Assert.Equal( FITSValueKind.Undefined, commentOnly.Value.Kind );
        Assert.Equal( "This is a comment", commentOnly.Comment );
        Assert.Equal( FITSValueKind.Undefined, emptyValue.Value.Kind );
    }

    /// <summary>
    /// Two <c>HISTORY</c> records merge their comments with a newline.
    /// </summary>
    [ Fact ]
    public void MergesHistory()
    {
        FITSProperty p1 = new FITSProperty( "HISTORY hello".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );
        FITSProperty p2 = new FITSProperty( "HISTORY world".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

        Assert.Equal( "hello", p1.Comment );
        Assert.Equal( "world", p2.Comment );

        p1.Merge( p2 );

        Assert.Equal( "hello\nworld", p1.Comment );
        Assert.Equal( "world", p2.Comment );
    }

    /// <summary>
    /// Merging a <c>HISTORY</c> record with an incompatible record is rejected.
    /// </summary>
    [ Fact ]
    public void RejectsMergingIncompatibleHistory()
    {
        FITSProperty p1 = new FITSProperty( "SIMPLE  = T  ".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );
        FITSProperty p2 = new FITSProperty( "HISTORY world".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

        Assert.Throws< FITSException >( () => p1.Merge( p2 ) );
        Assert.Throws< FITSException >( () => p2.Merge( p1 ) );
    }

    /// <summary>
    /// Two <c>COMMENT</c> records merge their comments with a newline.
    /// </summary>
    [ Fact ]
    public void MergesComment()
    {
        FITSProperty p1 = new FITSProperty( "COMMENT hello".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );
        FITSProperty p2 = new FITSProperty( "COMMENT world".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

        Assert.Equal( "hello", p1.Comment );
        Assert.Equal( "world", p2.Comment );

        p1.Merge( p2 );

        Assert.Equal( "hello\nworld", p1.Comment );
        Assert.Equal( "world", p2.Comment );
    }

    /// <summary>
    /// Merging a <c>COMMENT</c> record with an incompatible record is rejected.
    /// </summary>
    [ Fact ]
    public void RejectsMergingIncompatibleComment()
    {
        FITSProperty p1 = new FITSProperty( "SIMPLE  = T  ".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );
        FITSProperty p2 = new FITSProperty( "COMMENT world".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

        Assert.Throws< FITSException >( () => p1.Merge( p2 ) );
        Assert.Throws< FITSException >( () => p2.Merge( p1 ) );
    }

    /// <summary>
    /// A long string continued across <c>CONTINUE</c> records merges into a single
    /// value, joining comments with newlines and honoring the continuation flag.
    /// </summary>
    [ Fact ]
    public void MergesString()
    {
        FITSProperty p1 = new FITSProperty( "FOOBAR  = 'hello&' / This is".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );
        FITSProperty p2 = new FITSProperty( "CONTINUE  ', &   ' / a      ".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );
        FITSProperty p3 = new FITSProperty( "CONTINUE  'world ' / comment".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

        Assert.Equal( FITSValueKind.String, p1.Value.Kind );
        Assert.Equal( FITSValueKind.String, p2.Value.Kind );
        Assert.Equal( FITSValueKind.String, p3.Value.Kind );

        Assert.Equal( "hello&", p1.Value.AsString );
        Assert.Equal( ", &", p2.Value.AsString );
        Assert.Equal( "world", p3.Value.AsString );

        Assert.Equal( "This is", p1.Comment );
        Assert.Equal( "a", p2.Comment );
        Assert.Equal( "comment", p3.Comment );

        p1.Merge( p2 );
        p1.Merge( p3 );

        Assert.Equal( "hello, world", p1.Value.AsString );
        Assert.Equal( ", &", p2.Value.AsString );
        Assert.Equal( "world", p3.Value.AsString );

        Assert.Equal( "This is\na\ncomment", p1.Comment );
        Assert.Equal( "a", p2.Comment );
        Assert.Equal( "comment", p3.Comment );
    }

    /// <summary>
    /// Merging a <c>CONTINUE</c> record into a value without a continuation flag, or
    /// merging two non-continuable records, is rejected.
    /// </summary>
    [ Fact ]
    public void RejectsMergingUncontinuableString()
    {
        FITSProperty p1 = new FITSProperty( "FOOBAR  = 'hello&' ".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );
        FITSProperty p2 = new FITSProperty( "FOOBAR  = 'hello'  ".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );
        FITSProperty p3 = new FITSProperty( "CONTINUE  ', world'".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

        Assert.Equal( FITSValueKind.String, p1.Value.Kind );
        Assert.Equal( FITSValueKind.String, p2.Value.Kind );
        Assert.Equal( FITSValueKind.String, p3.Value.Kind );

        Assert.Equal( "hello&", p1.Value.AsString );
        Assert.Equal( "hello", p2.Value.AsString );
        Assert.Equal( ", world", p3.Value.AsString );

        Assert.Throws< FITSException >( () => p1.Merge( p2 ) );
        Assert.Throws< FITSException >( () => p2.Merge( p3 ) );
    }

    /// <summary>
    /// A keyword with neither a value nor a comment yields a <c>null</c> comment,
    /// not an empty string.
    /// </summary>
    [ Fact ]
    public void ValuelessRecordHasNullComment()
    {
        FITSProperty property = new FITSProperty( "FOOBAR".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

        Assert.Equal( FITSValueKind.Undefined, property.Value.Kind );
        Assert.Null( property.Comment );
    }

    /// <summary>
    /// Merging into a <c>HISTORY</c> record whose comment is <c>null</c> produces no
    /// leading newline.
    /// </summary>
    [ Fact ]
    public void MergesHistoryWithNullLeftCommentWithoutLeadingNewline()
    {
        FITSProperty p1 = new FITSProperty( "HISTORY".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );
        FITSProperty p2 = new FITSProperty( "HISTORY world".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

        Assert.Null( p1.Comment );
        Assert.Equal( "world", p2.Comment );

        p1.Merge( p2 );

        Assert.Equal( "world", p1.Comment );
    }

    /// <summary>
    /// Merging a <c>CONTINUE</c> record whose comment is <c>null</c> produces no
    /// trailing newline.
    /// </summary>
    [ Fact ]
    public void MergesStringWithNullRightCommentWithoutTrailingNewline()
    {
        FITSProperty p1 = new FITSProperty( "FOOBAR  = 'hello&' / This is".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );
        FITSProperty p2 = new FITSProperty( "CONTINUE  'world '".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

        Assert.Equal( "This is", p1.Comment );
        Assert.Null( p2.Comment );

        p1.Merge( p2 );

        Assert.Equal( "helloworld", p1.Value.AsString );
        Assert.Equal( "This is", p1.Comment );
    }

    /// <summary>
    /// The textual summary is non-empty, differs from the default type-name
    /// representation, and includes the keyword, kind, value and comment.
    /// </summary>
    [ Fact ]
    public void ToStringSummarizesTheProperty()
    {
        ( string Field, string[] Contains )[] cases =
        [
            ( "FOOBAR  = T       / This is a comment", [ "FOOBAR", "Logical",   "True",  "This is a comment" ] ),
            ( "FOOBAR  = 42      / This is a comment", [ "FOOBAR", "Integer",   "42",    "This is a comment" ] ),
            ( "FOOBAR  = 42.42   / This is a comment", [ "FOOBAR", "Float",     "42.42", "This is a comment" ] ),
            ( "FOOBAR  = 'hello' / This is a comment", [ "FOOBAR", "String",    "hello", "This is a comment" ] ),
            ( "FOOBAR  =         / This is a comment", [ "FOOBAR", "Undefined",          "This is a comment" ] ),
            ( "FOOBAR  = xyz     / This is a comment", [ "FOOBAR", "Unknown",   "xyz",   "This is a comment" ] ),
        ];

        foreach( ( string field, string[] contains ) in cases )
        {
            FITSProperty property = new FITSProperty( field.PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

            Assert.NotEmpty( property.ToString() );
            Assert.NotEqual( typeof( FITSProperty ).ToString(), property.ToString() );

            foreach( string substring in contains )
            {
                Assert.Contains( substring, property.ToString(), StringComparison.Ordinal );
            }
        }
    }

    /// <summary>
    /// A malformed <c>CONTINUE</c> record is rejected.
    /// </summary>
    [ Fact ]
    public void RejectsInvalidContinue()
    {
        Assert.Throws< FITSException >( () => new FITSProperty( "CONTINUE=   ".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient ) );
        Assert.Throws< FITSException >( () => new FITSProperty( "CONTINUE='' ".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient ) );
        Assert.Throws< FITSException >( () => new FITSProperty( "CONTINUE= ''".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient ) );
        Assert.Throws< FITSException >( () => new FITSProperty( "CONTINUE=  0".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient ) );
    }

    /// <summary>
    /// A string value with doubled interior quotes is unescaped to single quotes.
    /// </summary>
    [ Fact ]
    public void ParsesQuotesInString()
    {
        FITSProperty property = new FITSProperty( "FOOBAR  = '''hello''world'''".PaddedOrTruncated( FITSFile.CardSize ), FITSParsingOptions.Lenient );

        Assert.Equal( FITSValueKind.String, property.Value.Kind );
        Assert.Equal( "'hello'world'", property.Value.AsString );
    }

    /// <summary>
    /// Keyword normalization passes valid names through, coerces case only under a
    /// lenient option, and rejects names that cannot be made valid or that overflow
    /// the keyword field.
    /// </summary>
    [ Fact ]
    public void NormalizesKeyword()
    {
        Assert.Equal( "SIMPLE", FITSProperty.NormalizedKeyword( "SIMPLE", FITSSerializationOptions.Strict ) );
        Assert.Equal( "NAXIS1", FITSProperty.NormalizedKeyword( "NAXIS1", FITSSerializationOptions.Strict ) );
        Assert.Equal( "", FITSProperty.NormalizedKeyword( "", FITSSerializationOptions.Strict ) );

        Assert.Throws< FITSException >( () => FITSProperty.NormalizedKeyword( "foo", FITSSerializationOptions.Strict ) );
        Assert.Equal( "FOO", FITSProperty.NormalizedKeyword( "foo", FITSSerializationOptions.Lenient ) );

        Assert.Throws< FITSException >( () => FITSProperty.NormalizedKeyword( "foo bar", FITSSerializationOptions.Lenient ) );

        Assert.Throws< FITSException >( () => FITSProperty.NormalizedKeyword( "TOOLONGNAME", FITSSerializationOptions.Strict ) );
        Assert.Throws< FITSException >( () => FITSProperty.NormalizedKeyword( "TOOLONGNAME", FITSSerializationOptions.Lenient ) );
    }

    /// <summary>
    /// The designated model initializer sets the name, value and comment, defaulting
    /// the comment to <c>null</c>.
    /// </summary>
    [ Fact ]
    public void ConstructsWithDesignatedInitializer()
    {
        FITSProperty property = new FITSProperty( "OBJECT", FITSValue.String( "M42" ), FITSSerializationOptions.Strict, "the target" );

        Assert.Equal( "OBJECT", property.Name );
        Assert.Equal( FITSValue.String( "M42" ), property.Value );
        Assert.Equal( "the target", property.Comment );

        FITSProperty bare = new FITSProperty( "NAXIS", FITSValue.Integer( 0 ), FITSSerializationOptions.Strict );

        Assert.Null( bare.Comment );
    }

    /// <summary>
    /// The typed convenience initializers wrap their value in the matching
    /// <see cref="FITSValue"/> kind.
    /// </summary>
    [ Fact ]
    public void ConstructsWithConvenienceInitializers()
    {
        FITSProperty logical       = new FITSProperty( "SIMPLE", true,  FITSSerializationOptions.Strict );
        FITSProperty integer       = new FITSProperty( "NAXIS",  2L,    FITSSerializationOptions.Strict );
        FITSProperty floatingPoint = new FITSProperty( "BSCALE", 1.5,   FITSSerializationOptions.Strict );
        FITSProperty text          = new FITSProperty( "OBJECT", "M42", FITSSerializationOptions.Strict );

        Assert.Equal( "SIMPLE", logical.Name );
        Assert.Equal( "NAXIS", integer.Name );
        Assert.Equal( "BSCALE", floatingPoint.Name );
        Assert.Equal( "OBJECT", text.Name );

        Assert.Equal( FITSValue.Logical( true ), logical.Value );
        Assert.Equal( FITSValue.Integer( 2 ), integer.Value );
        Assert.Equal( FITSValue.Float( 1.5 ), floatingPoint.Value );
        Assert.Equal( FITSValue.String( "M42" ), text.Value );

        Assert.Null( logical.Comment );
        Assert.Null( integer.Comment );
        Assert.Null( floatingPoint.Comment );
        Assert.Null( text.Comment );
    }

    /// <summary>
    /// Constructing from a model rejects an invalid keyword in strict mode.
    /// </summary>
    [ Fact ]
    public void ConstructRejectsInvalidKeywordWhenStrict()
    {
        Assert.Throws< FITSException >( () => new FITSProperty( "foo",         FITSValue.Integer( 1 ), FITSSerializationOptions.Strict ) );
        Assert.Throws< FITSException >( () => new FITSProperty( "FOO BAR",     FITSValue.Integer( 1 ), FITSSerializationOptions.Strict ) );
        Assert.Throws< FITSException >( () => new FITSProperty( "TOOLONGNAME", FITSValue.Integer( 1 ), FITSSerializationOptions.Strict ) );

        FITSProperty simple    = new FITSProperty( "SIMPLE",  true,              FITSSerializationOptions.Strict );
        FITSProperty blank     = new FITSProperty( "",        FITSValue.Undefined, FITSSerializationOptions.Strict );
        FITSProperty comment   = new FITSProperty( "COMMENT", FITSValue.Undefined, FITSSerializationOptions.Strict, "hi" );

        Assert.Equal( "SIMPLE", simple.Name );
        Assert.Equal( "", blank.Name );
        Assert.Equal( "COMMENT", comment.Name );
    }

    /// <summary>
    /// Constructing from a model coerces an otherwise-valid name to upper case in
    /// lenient mode, but still rejects names that cannot be coerced or that overflow
    /// the keyword field.
    /// </summary>
    [ Fact ]
    public void ConstructCoercesKeywordWhenLenient()
    {
        FITSProperty property = new FITSProperty( "foo", FITSValue.Integer( 1 ), FITSSerializationOptions.Lenient );

        Assert.Equal( "FOO", property.Name );

        Assert.Throws< FITSException >( () => new FITSProperty( "foo bar",     FITSValue.Integer( 1 ), FITSSerializationOptions.Lenient ) );
        Assert.Throws< FITSException >( () => new FITSProperty( "toolongname", FITSValue.Integer( 1 ), FITSSerializationOptions.Lenient ) );
    }

    /// <summary>
    /// The value and comment can be edited in place after construction.
    /// </summary>
    [ Fact ]
    public void ValueAndCommentAreSettable()
    {
        FITSProperty property = new FITSProperty( "OBJECT", FITSValue.String( "M42" ), FITSSerializationOptions.Strict, "first" );

        property.Value   = FITSValue.Integer( 7 );
        property.Comment = "second";

        Assert.Equal( FITSValue.Integer( 7 ), property.Value );
        Assert.Equal( "second", property.Comment );

        property.Comment = null;

        Assert.Null( property.Comment );
    }

    /// <summary>
    /// Editing the value or comment to a different one marks the owning section as
    /// needing re-serialization; construction and reassigning an equal one do not.
    /// </summary>
    [ Fact ]
    public void EditingMarksTheOwningSectionForReserialization()
    {
        FITSSection  section  = new FITSSection( FITSSection.Kind.Header, block: null );
        FITSProperty property = new FITSProperty( "OBJECT", FITSValue.String( "M42" ), FITSSerializationOptions.Strict, "note" ) { Section = section };

        Assert.False( section.NeedsSerialization );

        property.Value   = FITSValue.String( "M42" );
        property.Comment = "note";

        Assert.False( section.NeedsSerialization );

        property.Value = FITSValue.Integer( 7 );

        Assert.True( section.NeedsSerialization );
    }
}
