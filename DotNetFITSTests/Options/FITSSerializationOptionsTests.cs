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

using DotNetFITS;

namespace DotNetFITSTests;

/// <summary>
/// Unit tests for <see cref="FITSSerializationOptions"/>.
/// </summary>
public class FITSSerializationOptionsTests
{
    /// <summary>
    /// A raw bitmask round-trips through the option set unchanged.
    /// </summary>
    [ Fact ]
    public void RawValueRoundTrips()
    {
        foreach( int rawValue in new[] { 0, 1, 42 } )
        {
            Assert.Equal( rawValue, ( int )( FITSSerializationOptions )rawValue );
        }
    }

    /// <summary>
    /// The strict and lenient presets are usable option-set values that
    /// round-trip through their raw bitmask like any other option set.
    /// </summary>
    [ Fact ]
    public void StrictAndLenientPresetsExist()
    {
        FITSSerializationOptions strict  = FITSSerializationOptions.Strict;
        FITSSerializationOptions lenient = FITSSerializationOptions.Lenient;

        Assert.Equal( strict,  ( FITSSerializationOptions )( int )strict );
        Assert.Equal( lenient, ( FITSSerializationOptions )( int )lenient );
    }

    /// <summary>
    /// Forming the union with the strict preset leaves it a member of the result.
    /// </summary>
    [ Fact ]
    public void OptionSetAlgebra()
    {
        FITSSerializationOptions options = FITSSerializationOptions.None;

        options |= FITSSerializationOptions.Strict;

        Assert.True( options.HasFlag( FITSSerializationOptions.Strict ) );
    }

    /// <summary>
    /// Keyword coercion is present only in the lenient preset.
    /// </summary>
    [ Fact ]
    public void LenientCoercesInvalidKeywordsButStrictDoesNot()
    {
        Assert.True( FITSSerializationOptions.Lenient.HasFlag( FITSSerializationOptions.CoerceInvalidKeywords ) );
        Assert.False( FITSSerializationOptions.Strict.HasFlag( FITSSerializationOptions.CoerceInvalidKeywords ) );
    }

    /// <summary>
    /// On-write data-size validation is relaxed only in the lenient preset.
    /// </summary>
    [ Fact ]
    public void LenientAllowsDataSizeMismatchButStrictDoesNot()
    {
        Assert.True( FITSSerializationOptions.Lenient.HasFlag( FITSSerializationOptions.AllowDataSizeMismatch ) );
        Assert.False( FITSSerializationOptions.Strict.HasFlag( FITSSerializationOptions.AllowDataSizeMismatch ) );
    }
}
