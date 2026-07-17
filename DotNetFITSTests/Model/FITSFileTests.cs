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
/// Unit tests for <see cref="FITSFile"/>.
/// </summary>
public class FITSFileTests
{
    /// <summary>
    /// The fixed FITS size constants carry the values mandated by the standard:
    /// a 2880-byte block, an 80-byte card, an 8-byte keyword field and a
    /// 20-byte fixed-format value field.
    /// </summary>
    [ Fact ]
    public void SizeConstantsMatchTheFITSStandard()
    {
        Assert.Equal( 2880, FITSFile.BlockSize );
        Assert.Equal( 80,   FITSFile.CardSize );
        Assert.Equal( 8,    FITSFile.KeywordLength );
        Assert.Equal( 20,   FITSFile.FixedValueFieldWidth );
    }

    /// <summary>
    /// The maximum data-segment size is the 2^53 ceiling used to reject corrupt
    /// geometries before the size arithmetic can overflow.
    /// </summary>
    [ Fact ]
    public void MaxDataSizeIsTwoToThePowerOfFiftyThree()
    {
        Assert.Equal( 1L << 53, FITSFile.MaxDataSize );
    }
}
