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

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DotNetFITS;

namespace DotNetFITSTests;

/// <summary>
/// Unit tests for <see cref="FITSValue"/> and <see cref="FITSValueKind"/>.
/// </summary>
/// <remarks>
/// A round-trip test is deferred to the FITSProperty milestone (it parses through
/// <c>FITSProperty</c>), and a value-type thread-safety assertion has no C#
/// equivalent and is omitted. A culture-swap test covers the culture-sensitive
/// float formatting, plus explicit coverage of the equality operators.
/// </remarks>
public class FITSValueTests
{
    /// <summary>
    /// Each typed accessor returns the payload when the value is of the matching
    /// kind.
    /// </summary>
    [ Fact ]
    public void AccessorReturnsPayloadForMatchingCase()
    {
        Assert.True( FITSValue.Logical( true ).AsLogical );
        Assert.Equal( 42L,   FITSValue.Integer( 42 ).AsInteger );
        Assert.Equal( 42.5,  FITSValue.Float( 42.5 ).AsFloat );
        Assert.Equal( "hi",  FITSValue.String( "hi" ).AsString );
    }

    /// <summary>
    /// Each typed accessor returns <c>null</c> when the value is of a different
    /// kind, including the <c>unknown</c> literal, which no accessor exposes.
    /// </summary>
    [ Fact ]
    public void AccessorReturnsNullForNonMatchingCase()
    {
        Assert.Null( FITSValue.Integer( 42 ).AsLogical );
        Assert.Null( FITSValue.Integer( 42 ).AsFloat );
        Assert.Null( FITSValue.Integer( 42 ).AsString );
        Assert.Null( FITSValue.String( "hi" ).AsInteger );
        Assert.Null( FITSValue.Undefined.AsInteger );
        Assert.Null( FITSValue.Unknown( "x" ).AsString );
    }

    /// <summary>
    /// <see cref="FITSValue.Kind"/> reflects the value's case.
    /// </summary>
    [ Fact ]
    public void KindDerivesFromCase()
    {
        Assert.Equal( FITSValueKind.Logical,   FITSValue.Logical( true ).Kind );
        Assert.Equal( FITSValueKind.Integer,   FITSValue.Integer( 42 ).Kind );
        Assert.Equal( FITSValueKind.Float,     FITSValue.Float( 42.5 ).Kind );
        Assert.Equal( FITSValueKind.String,    FITSValue.String( "hi" ).Kind );
        Assert.Equal( FITSValueKind.Undefined, FITSValue.Undefined.Kind );
        Assert.Equal( FITSValueKind.Unknown,   FITSValue.Unknown( "x" ).Kind );
    }

    /// <summary>
    /// Each <see cref="FITSValueKind"/> renders to its human-readable name.
    /// </summary>
    [ Fact ]
    public void KindDescriptionMatchesTheKindName()
    {
        Assert.Equal( "Logical",   FITSValueKind.Logical.ToString() );
        Assert.Equal( "Integer",   FITSValueKind.Integer.ToString() );
        Assert.Equal( "Float",     FITSValueKind.Float.ToString() );
        Assert.Equal( "String",    FITSValueKind.String.ToString() );
        Assert.Equal( "Undefined", FITSValueKind.Undefined.ToString() );
        Assert.Equal( "Unknown",   FITSValueKind.Unknown.ToString() );
    }

    /// <summary>
    /// The default value is an undefined value - <see cref="FITSValueKind.Undefined"/>
    /// is the enum's zero value - so a zeroed or uninitialized value reads as "no
    /// value" rather than a logical false.
    /// </summary>
    [ Fact ]
    public void DefaultValueIsUndefined()
    {
        FITSValue value = default;

        Assert.Equal( FITSValueKind.Undefined, value.Kind );
        Assert.Equal( "", value.Serialized() );
    }

    /// <summary>
    /// Values of the same kind and payload are equal; a differing payload or kind
    /// is not.
    /// </summary>
    [ Fact ]
    public void Equality()
    {
        Assert.Equal( FITSValue.Integer( 42 ), FITSValue.Integer( 42 ) );
        Assert.NotEqual( FITSValue.Integer( 42 ), FITSValue.Integer( 43 ) );
        Assert.NotEqual( FITSValue.Integer( 42 ), FITSValue.Float( 42 ) );
        Assert.Equal( FITSValue.Undefined, FITSValue.Undefined );
        Assert.NotEqual( FITSValue.Unknown( "a" ), FITSValue.Unknown( "b" ) );
    }

    /// <summary>
    /// The <c>==</c> and <c>!=</c> operators agree with value equality.
    /// </summary>
    [ Fact ]
    public void EqualityOperators()
    {
        Assert.True( FITSValue.Integer( 42 ) == FITSValue.Integer( 42 ) );
        Assert.False( FITSValue.Integer( 42 ) == FITSValue.Integer( 43 ) );
        Assert.True( FITSValue.Integer( 42 ) != FITSValue.Float( 42 ) );
        Assert.False( FITSValue.Undefined != FITSValue.Undefined );
    }

    /// <summary>
    /// <see cref="object.Equals(object)"/> rejects a <c>null</c> or a value of a
    /// different type.
    /// </summary>
    [ Fact ]
    public void EqualsWithDifferentTypeOrNullIsFalse()
    {
        Assert.False( FITSValue.Integer( 42 ).Equals( null ) );
        Assert.False( FITSValue.Integer( 42 ).Equals( "42" ) );
    }

    /// <summary>
    /// Two <c>NaN</c> floats compare equal (departing from IEEE 754) so diffing
    /// headers reports no spurious change; ordinary finite values keep normal
    /// equality.
    /// </summary>
    [ Fact ]
    public void NanFloatValuesAreEqual()
    {
        Assert.Equal( FITSValue.Float( double.NaN ), FITSValue.Float( double.NaN ) );

        Assert.Equal( FITSValue.Float( 42.5 ), FITSValue.Float( 42.5 ) );
        Assert.NotEqual( FITSValue.Float( 42.5 ), FITSValue.Float( 42.0 ) );
        Assert.NotEqual( FITSValue.Float( double.NaN ), FITSValue.Float( 42.5 ) );
    }

    /// <summary>
    /// Hashing is consistent with the <c>NaN</c>-equal equality: two equal
    /// <c>NaN</c> values hash alike.
    /// </summary>
    [ Fact ]
    public void NanFloatValuesHashEqual()
    {
        Assert.Equal( FITSValue.Float( double.NaN ).GetHashCode(), FITSValue.Float( double.NaN ).GetHashCode() );
    }

    /// <summary>
    /// Representative values across every kind hash distinctly - a sanity check
    /// that the kind discriminator participates in the hash.
    /// </summary>
    [ Fact ]
    public void DistinctValuesHashDistinctly()
    {
        FITSValue[] values =
        [
            FITSValue.Logical( true ),
            FITSValue.Integer( 1 ),
            FITSValue.Float( 1.0 ),
            FITSValue.String( "x" ),
            FITSValue.Undefined,
            FITSValue.Unknown( "y" ),
        ];

        HashSet<int> hashes = [ .. values.Select( value => value.GetHashCode() ) ];

        Assert.Equal( values.Length, hashes.Count );
    }

    /// <summary>
    /// A <see cref="HashSet{T}"/> of values collapses duplicates and equal
    /// <c>NaN</c>s, and membership honors the custom equality.
    /// </summary>
    [ Fact ]
    public void ValueSetRoundTrips()
    {
        HashSet<FITSValue> set =
        [
            FITSValue.Integer( 1 ),
            FITSValue.Integer( 1 ),
            FITSValue.Float( double.NaN ),
            FITSValue.Float( double.NaN ),
            FITSValue.String( "a" ),
        ];

        Assert.Equal( 3, set.Count );
        Assert.Contains( FITSValue.Integer( 1 ), set );
        Assert.Contains( FITSValue.Float( double.NaN ), set );
        Assert.Contains( FITSValue.String( "a" ), set );
    }

    /// <summary>
    /// A logical value serializes to <c>T</c> or <c>F</c>.
    /// </summary>
    [ Fact ]
    public void SerializesLogical()
    {
        Assert.Equal( "T", FITSValue.Logical( true ).Serialized() );
        Assert.Equal( "F", FITSValue.Logical( false ).Serialized() );
    }

    /// <summary>
    /// An integer value serializes to a signed decimal literal.
    /// </summary>
    [ Fact ]
    public void SerializesInteger()
    {
        Assert.Equal( "42", FITSValue.Integer( 42 ).Serialized() );
        Assert.Equal( "-7", FITSValue.Integer( -7 ).Serialized() );
        Assert.Equal( "0",  FITSValue.Integer( 0 ).Serialized() );
    }

    /// <summary>
    /// A float serializes to the shortest round-trippable decimal, with an
    /// upper-case exponent and a guaranteed decimal point or exponent so it
    /// re-parses as a float, not an integer.
    /// </summary>
    [ Fact ]
    public void SerializesFloat()
    {
        Assert.Equal( "3.14",    FITSValue.Float( 3.14 ).Serialized() );
        Assert.Equal( "-0.5",    FITSValue.Float( -0.5 ).Serialized() );
        Assert.Equal( "2.0",     FITSValue.Float( 2.0 ).Serialized() );
        Assert.Equal( "1.5E-10", FITSValue.Float( 1.5e-10 ).Serialized() );
        Assert.Equal( "1E+20",   FITSValue.Float( 1e20 ).Serialized() );
    }

    /// <summary>
    /// Float serialization is culture-invariant: under a non-invariant current
    /// culture the decimal separator stays a period, not the locale's separator.
    /// </summary>
    [ Fact ]
    public void SerializesFloatIsCultureInvariant()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo( "fr-FR" );

            Assert.Equal( "3.14",    FITSValue.Float( 3.14 ).Serialized() );
            Assert.Equal( "1.5E-10", FITSValue.Float( 1.5e-10 ).Serialized() );
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// Every float the renderer emits is a valid FITS floating-point literal per
    /// FITS 4.0 section 4.2.4 - an optionally-signed decimal number with an
    /// optional upper-case E/D exponent - and carries a decimal point or exponent,
    /// so it re-parses as a float rather than an integer.
    /// </summary>
    [ Fact ]
    public void SerializesFloatProducesStandardCompliantLiterals()
    {
        double[] values =
        [
            0.0, -0.0, 1.0, 2.0, -0.5, 3.14, 100.0, 123456.0, -1234.5678,
            1e-5, 1e-4, 1e15, 1e16, 1e17, 1e20, 1.5e-10,
            double.Epsilon, double.MaxValue, double.MinValue,
        ];

        foreach( double value in values )
        {
            string literal = FITSValue.Float( value ).Serialized();

            Assert.Matches( @"^[+-]?(?:\d+\.\d*|\.\d+|\d+)(?:[ED][+-]?\d+)?$", literal );
            Assert.True( literal.IndexOfAny( [ '.', 'E', 'D', 'e', 'd' ] ) >= 0, $"literal '{literal}' must carry a decimal point or exponent" );
        }
    }

    /// <summary>
    /// Serializing a non-finite float throws, since FITS has no literal for the
    /// IEEE special values.
    /// </summary>
    [ Fact ]
    public void SerializesNonFiniteFloatThrows()
    {
        Assert.Throws<FITSException>( () => FITSValue.Float( double.PositiveInfinity ).Serialized() );
        Assert.Throws<FITSException>( () => FITSValue.Float( double.NegativeInfinity ).Serialized() );
        Assert.Throws<FITSException>( () => FITSValue.Float( double.NaN ).Serialized() );
    }

    /// <summary>
    /// A string serializes single-quoted with interior quotes doubled; the null
    /// string and the empty (single-space) string keep their distinct forms.
    /// </summary>
    [ Fact ]
    public void SerializesString()
    {
        Assert.Equal( "'M42'",     FITSValue.String( "M42" ).Serialized() );
        Assert.Equal( "'O''HARA'", FITSValue.String( "O'HARA" ).Serialized() );
        Assert.Equal( "''",        FITSValue.String( "" ).Serialized() );
        Assert.Equal( "' '",       FITSValue.String( " " ).Serialized() );
    }

    /// <summary>
    /// An undefined value serializes to the empty string.
    /// </summary>
    [ Fact ]
    public void SerializesUndefined()
    {
        Assert.Equal( "", FITSValue.Undefined.Serialized() );
    }

    /// <summary>
    /// An unknown value serializes its retained literal verbatim.
    /// </summary>
    [ Fact ]
    public void SerializesUnknown()
    {
        Assert.Equal( "0xFF", FITSValue.Unknown( "0xFF" ).Serialized() );
    }
}
