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
using System.Globalization;

namespace DotNetFITS;

/// <summary>
/// The typed value of a FITS header property.
/// </summary>
/// <remarks>
/// FITS values are one of a small set of scalar types. Two extra kinds cover
/// records that carry no value (<see cref="FITSValueKind.Undefined"/>) and values
/// that match no known type or cannot be represented exactly
/// (<see cref="FITSValueKind.Unknown"/>), the latter preserving the original
/// literal text.
/// <para>
/// A lean value type: a single 8-byte numeric slot reinterprets the bits of the
/// <c>bool</c>/<see cref="long"/>/<see cref="double"/> payloads, one string
/// reference holds the string/unknown payload, and a <see cref="FITSValueKind"/>
/// discriminator selects the active case. The default value
/// (<c>default(FITSValue)</c>) is an undefined value.
/// </para>
/// <para>
/// Equality treats two float <c>NaN</c> payloads as equal, departing from IEEE
/// 754, so comparing or diffing headers does not report a spurious change.
/// <see cref="GetHashCode"/> is kept consistent, hashing every <c>NaN</c> to one
/// constant so equal-<c>NaN</c> values share a bucket.
/// </para>
/// </remarks>
public readonly struct FITSValue : IEquatable<FITSValue>
{
    /// <summary>The active case discriminator.</summary>
    private readonly FITSValueKind kind;

    /// <summary>
    /// The numeric payload slot, reinterpreted per <see cref="kind"/>: a boolean
    /// (0 or 1), a <see cref="long"/>, or the bit pattern of a
    /// <see cref="double"/>. Unused for the string, undefined and unknown kinds.
    /// </summary>
    private readonly long numeric;

    /// <summary>
    /// The string payload for the <see cref="FITSValueKind.String"/> and
    /// <see cref="FITSValueKind.Unknown"/> kinds; <c>null</c> otherwise.
    /// </summary>
    private readonly string? text;

    /// <summary>
    /// The characters whose presence marks a rendered float as a float rather
    /// than an integer: the decimal point and the either-case exponent letter.
    /// </summary>
    private static readonly char[] FloatNotationMarkers = [ '.', 'e', 'E' ];

    /// <summary>
    /// Initializes a value with the given kind and payload slots.
    /// </summary>
    /// <param name="kind">The active case discriminator.</param>
    /// <param name="numeric">The numeric payload slot.</param>
    /// <param name="text">The string payload, or <c>null</c>.</param>
    private FITSValue( FITSValueKind kind, long numeric, string? text )
    {
        this.kind    = kind;
        this.numeric = numeric;
        this.text    = text;
    }

    /// <summary>Creates a logical (boolean) value, written <c>T</c> or <c>F</c> in the record.</summary>
    /// <param name="value">The boolean payload.</param>
    /// <returns>The created value.</returns>
    public static FITSValue Logical( bool value ) => new FITSValue( FITSValueKind.Logical, value ? 1L : 0L, null );

    /// <summary>Creates an integer value.</summary>
    /// <param name="value">The integer payload.</param>
    /// <returns>The created value.</returns>
    public static FITSValue Integer( long value ) => new FITSValue( FITSValueKind.Integer, value, null );

    /// <summary>Creates a floating-point value.</summary>
    /// <param name="value">The floating-point payload.</param>
    /// <returns>The created value.</returns>
    public static FITSValue Float( double value ) => new FITSValue( FITSValueKind.Float, BitConverter.DoubleToInt64Bits( value ), null );

    /// <summary>Creates a character-string value.</summary>
    /// <param name="value">The string payload.</param>
    /// <returns>The created value.</returns>
    public static FITSValue String( string value ) => new FITSValue( FITSValueKind.String, 0L, value );

    /// <summary>
    /// Creates a value that matches no known FITS type, retaining its literal text.
    /// </summary>
    /// <param name="value">The retained literal, trimmed of its field padding.</param>
    /// <returns>The created value.</returns>
    public static FITSValue Unknown( string value ) => new FITSValue( FITSValueKind.Unknown, 0L, value );

    /// <summary>
    /// A value-less value, for records that carry no value field.
    /// </summary>
    public static FITSValue Undefined => new FITSValue( FITSValueKind.Undefined, 0L, null );

    /// <summary>The <see cref="FITSValueKind"/> matching this value's case.</summary>
    public FITSValueKind Kind => this.kind;

    /// <summary>The boolean payload, or <c>null</c> if this is not a logical value.</summary>
    public bool? AsLogical => this.kind == FITSValueKind.Logical ? this.numeric != 0L : null;

    /// <summary>The integer payload, or <c>null</c> if this is not an integer value.</summary>
    public long? AsInteger => this.kind == FITSValueKind.Integer ? this.numeric : null;

    /// <summary>The floating-point payload, or <c>null</c> if this is not a float value.</summary>
    public double? AsFloat => this.kind == FITSValueKind.Float ? BitConverter.Int64BitsToDouble( this.numeric ) : null;

    /// <summary>The string payload, or <c>null</c> if this is not a string value.</summary>
    public string? AsString => this.kind == FITSValueKind.String ? this.text : null;

    /// <summary>Returns whether two values are equal.</summary>
    /// <param name="lhs">A value to compare.</param>
    /// <param name="rhs">Another value to compare.</param>
    /// <returns><c>true</c> if the two values are equal.</returns>
    public static bool operator ==( FITSValue lhs, FITSValue rhs ) => lhs.Equals( rhs );

    /// <summary>Returns whether two values are not equal.</summary>
    /// <param name="lhs">A value to compare.</param>
    /// <param name="rhs">Another value to compare.</param>
    /// <returns><c>true</c> if the two values are not equal.</returns>
    public static bool operator !=( FITSValue lhs, FITSValue rhs ) => lhs.Equals( rhs ) == false;

    /// <summary>Returns whether this value equals another.</summary>
    /// <remarks>
    /// Matching kinds compare their payloads, except two float <c>NaN</c> payloads
    /// are treated as equal (unlike IEEE 754). Differing kinds are never equal.
    /// </remarks>
    /// <param name="other">The value to compare with.</param>
    /// <returns><c>true</c> if the two values are equal.</returns>
    public bool Equals( FITSValue other )
    {
        if( this.kind != other.kind )
        {
            return false;
        }

        return this.kind switch
        {
            FITSValueKind.Logical   => this.numeric == other.numeric,
            FITSValueKind.Integer   => this.numeric == other.numeric,
            FITSValueKind.Float     => FloatEquals( BitConverter.Int64BitsToDouble( this.numeric ), BitConverter.Int64BitsToDouble( other.numeric ) ),
            FITSValueKind.String    => this.text == other.text,
            FITSValueKind.Unknown   => this.text == other.text,
            FITSValueKind.Undefined => true,
            _                       => false,
        };
    }

    /// <summary>Returns whether this value equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><c>true</c> if <paramref name="obj"/> is an equal <see cref="FITSValue"/>.</returns>
    public override bool Equals( object? obj ) => obj is FITSValue other && this.Equals( other );

    /// <summary>Returns a hash code consistent with <see cref="Equals(FITSValue)"/>.</summary>
    /// <remarks>
    /// Each kind mixes in its discriminator before its payload. Because equality
    /// treats any two <c>NaN</c> floats as equal, every <c>NaN</c> is hashed to a
    /// single constant so equal-<c>NaN</c> values share a bucket.
    /// </remarks>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return this.kind switch
        {
            FITSValueKind.Logical   => HashCode.Combine( this.kind, this.numeric != 0L ),
            FITSValueKind.Integer   => HashCode.Combine( this.kind, this.numeric ),
            FITSValueKind.Float     => FloatHashCode( BitConverter.Int64BitsToDouble( this.numeric ) ),
            FITSValueKind.String    => HashCode.Combine( this.kind, this.text ),
            FITSValueKind.Unknown   => HashCode.Combine( this.kind, this.text ),
            FITSValueKind.Undefined => HashCode.Combine( this.kind ),
            _                       => 0,
        };
    }

    /// <summary>
    /// Renders this value to its FITS card-literal text - the inverse of the value
    /// parser.
    /// </summary>
    /// <remarks>
    /// The result is the minimal free-format literal. Column placement and
    /// keyword-aware padding (right-justification of numeric and logical values,
    /// and the eight-character minimum required only for the <c>XTENSION</c> value)
    /// are applied by the card renderer, not here. A logical renders as <c>T</c> or
    /// <c>F</c>; an integer as a signed decimal; a float as the shortest decimal
    /// that parses back to the same value, with an upper-case <c>E</c> exponent; a
    /// string single-quoted with interior quotes doubled (an empty string as
    /// <c>''</c>, a single space as <c>' '</c>); an undefined value as the empty
    /// string; an unknown value as its retained literal, verbatim.
    /// </remarks>
    /// <returns>The value-literal text.</returns>
    /// <exception cref="FITSException">
    /// A float value is not finite; FITS has no keyword-value literal for the IEEE
    /// special values.
    /// </exception>
    public string Serialized()
    {
        return this.kind switch
        {
            FITSValueKind.Logical   => this.numeric != 0L ? "T" : "F",
            FITSValueKind.Integer   => this.numeric.ToString( CultureInfo.InvariantCulture ),
            FITSValueKind.Float     => SerializedFloat( BitConverter.Int64BitsToDouble( this.numeric ) ),
            FITSValueKind.String    => SerializedString( this.text ?? "" ),
            FITSValueKind.Unknown   => this.text ?? "",
            FITSValueKind.Undefined => "",
            _                       => "",
        };
    }

    /// <summary>Returns whether two floats are equal under the <c>NaN</c>-equal rule.</summary>
    /// <param name="a">A float.</param>
    /// <param name="b">Another float.</param>
    /// <returns><c>true</c> if equal, treating <c>NaN</c> as equal to <c>NaN</c>.</returns>
    private static bool FloatEquals( double a, double b ) => a == b || ( double.IsNaN( a ) && double.IsNaN( b ) );

    /// <summary>Returns a hash code for a float payload, hashing every <c>NaN</c> alike.</summary>
    /// <param name="value">The float payload.</param>
    /// <returns>The hash code.</returns>
    private static int FloatHashCode( double value ) => double.IsNaN( value ) ? HashCode.Combine( FITSValueKind.Float, double.NaN ) : HashCode.Combine( FITSValueKind.Float, value );

    /// <summary>Renders a floating-point value to its FITS literal text.</summary>
    /// <param name="value">The value to render.</param>
    /// <returns>
    /// The shortest decimal that round-trips to <paramref name="value"/>, with an
    /// upper-case exponent and a guaranteed decimal point or exponent.
    /// </returns>
    /// <exception cref="FITSException"><paramref name="value"/> is not finite.</exception>
    private static string SerializedFloat( double value )
    {
        if( double.IsFinite( value ) == false )
        {
            throw FITSException.InvalidValueForSerialization( $"Cannot represent the non-finite floating-point value {value.ToString( CultureInfo.InvariantCulture )}" );
        }

        // .NET's default formatting is the shortest round-trippable decimal, and
        // already upper-cases the exponent letter to the FITS-required "E".
        string text = value.ToString( CultureInfo.InvariantCulture );

        // Guarantee a decimal point or exponent so a whole-valued float (which
        // .NET renders as e.g. "2") re-parses as a float, not an integer.
        if( text.IndexOfAny( FloatNotationMarkers ) < 0 )
        {
            text += ".0";
        }

        return text;
    }

    /// <summary>Renders a string value to its FITS single-quoted literal text.</summary>
    /// <param name="value">The string to render.</param>
    /// <returns>
    /// The value enclosed in single quotes, with every interior single quote
    /// doubled. An empty <paramref name="value"/> yields the null string <c>''</c>.
    /// </returns>
    private static string SerializedString( string value )
    {
        // A single quote inside the value is written as two successive quotes.
        string escaped = value.Replace( "'", "''", StringComparison.Ordinal );

        return "'" + escaped + "'";
    }
}
