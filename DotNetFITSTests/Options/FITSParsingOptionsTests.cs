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
/// Unit tests for <see cref="FITSParsingOptions"/>.
/// </summary>
/// <remarks>
/// This guard test covers a type that hand-assigns fourteen bit values and two
/// composite presets, which the compiler cannot check for a wrong shift, a bit
/// collision or a missing preset member.
/// </remarks>
public class FITSParsingOptionsTests
{
    /// <summary>
    /// Each flag has its expected, distinct power-of-two bit value, asserted
    /// against independent literals so a wrong shift is caught.
    /// </summary>
    [ Fact ]
    public void EachFlagHasItsExpectedBitValue()
    {
        Assert.Equal(    1, ( int )FITSParsingOptions.MergeHistoryProperties );
        Assert.Equal(    2, ( int )FITSParsingOptions.MergeCommentProperties );
        Assert.Equal(    4, ( int )FITSParsingOptions.MergeStringProperties );
        Assert.Equal(    8, ( int )FITSParsingOptions.AllowUnknownProperties );
        Assert.Equal(   16, ( int )FITSParsingOptions.AllowTrailingQuoteJunk );
        Assert.Equal(   32, ( int )FITSParsingOptions.AllowNonPrintableHeaderText );
        Assert.Equal(   64, ( int )FITSParsingOptions.AllowDataLengthMismatch );
        Assert.Equal(  128, ( int )FITSParsingOptions.AllowMissingValueIndicatorSpace );
        Assert.Equal(  256, ( int )FITSParsingOptions.AllowLowercaseExponents );
        Assert.Equal(  512, ( int )FITSParsingOptions.AllowNulPadding );
        Assert.Equal( 1024, ( int )FITSParsingOptions.AllowTrailingPartialBlock );
        Assert.Equal( 2048, ( int )FITSParsingOptions.AllowContentAfterEnd );
        Assert.Equal( 4096, ( int )FITSParsingOptions.AllowNulPaddingInValues );
        Assert.Equal( 8192, ( int )FITSParsingOptions.AllowOrphanedContinue );
    }

    /// <summary>
    /// The strict preset is exactly the three spec conveniences and the lenient
    /// preset is exactly all fourteen flags, with every leniency flag present in
    /// lenient and absent from strict.
    /// </summary>
    [ Fact ]
    public void PresetsComposeToTheExpectedFlags()
    {
        Assert.Equal( 0b0000_0000_0000_0111, ( int )FITSParsingOptions.Strict );
        Assert.Equal( 0b0011_1111_1111_1111, ( int )FITSParsingOptions.Lenient );

        Assert.True( FITSParsingOptions.Lenient.HasFlag( FITSParsingOptions.Strict ) );

        FITSParsingOptions[] leniencyFlags =
        [
            FITSParsingOptions.AllowUnknownProperties,
            FITSParsingOptions.AllowTrailingQuoteJunk,
            FITSParsingOptions.AllowNonPrintableHeaderText,
            FITSParsingOptions.AllowDataLengthMismatch,
            FITSParsingOptions.AllowMissingValueIndicatorSpace,
            FITSParsingOptions.AllowLowercaseExponents,
            FITSParsingOptions.AllowNulPadding,
            FITSParsingOptions.AllowTrailingPartialBlock,
            FITSParsingOptions.AllowContentAfterEnd,
            FITSParsingOptions.AllowNulPaddingInValues,
            FITSParsingOptions.AllowOrphanedContinue,
        ];

        foreach( FITSParsingOptions flag in leniencyFlags )
        {
            Assert.True( FITSParsingOptions.Lenient.HasFlag( flag ) );
            Assert.False( FITSParsingOptions.Strict.HasFlag( flag ) );
        }
    }
}
