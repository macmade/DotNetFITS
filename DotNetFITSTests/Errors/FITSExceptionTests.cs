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
using DotNetFITS;

namespace DotNetFITSTests;

/// <summary>
/// Unit tests for <see cref="FITSException"/>.
/// </summary>
public class FITSExceptionTests
{
    /// <summary>
    /// Every error factory sets the matching <see cref="FITSErrorKind"/> and
    /// produces a non-empty, descriptive message (not merely the type name) that
    /// includes its payload - extending the Swift <c>FITSError</c> description
    /// test with the C#-specific <see cref="FITSException.Kind"/> discriminator.
    /// </summary>
    [ Fact ]
    public void FactoriesProduceTheExpectedKindAndMessage()
    {
        ( FITSException Error, FITSErrorKind Kind, string Expected )[] cases =
        [
            ( FITSException.InvalidFileURL( "/foo/bar.fits" ),                FITSErrorKind.InvalidFileURL,               "/foo/bar.fits" ),
            ( FITSException.CannotReadFile( "/foo/bar.fits" ),                FITSErrorKind.CannotReadFile,               "/foo/bar.fits" ),
            ( FITSException.InvalidBlockSize( 42 ),                           FITSErrorKind.InvalidBlockSize,             "42" ),
            ( FITSException.InvalidBlockData( "This is a test" ),             FITSErrorKind.InvalidBlockData,             "This is a test" ),
            ( FITSException.InvalidSectionData( "This is a test" ),           FITSErrorKind.InvalidSectionData,           "This is a test" ),
            ( FITSException.InvalidFileData( "This is a test" ),              FITSErrorKind.InvalidFileData,              "This is a test" ),
            ( FITSException.InvalidPropertyData( "This is a test" ),          FITSErrorKind.InvalidPropertyData,          "This is a test" ),
            ( FITSException.DataError( "This is a test" ),                    FITSErrorKind.DataError,                    "This is a test" ),
            ( FITSException.InvalidValueForSerialization( "This is a test" ), FITSErrorKind.InvalidValueForSerialization, "This is a test" ),
            ( FITSException.CannotSerialize( "This is a test" ),              FITSErrorKind.CannotSerialize,              "This is a test" ),
            ( FITSException.CannotWriteFile( "/foo/bar.fits" ),               FITSErrorKind.CannotWriteFile,              "/foo/bar.fits" ),
        ];

        foreach( ( FITSException error, FITSErrorKind kind, string expected ) in cases )
        {
            Assert.Equal( kind, error.Kind );
            Assert.False( string.IsNullOrEmpty( error.Message ) );
            Assert.NotEqual( typeof( FITSException ).ToString(), error.Message );
            Assert.Contains( expected, error.Message, StringComparison.Ordinal );
        }
    }

    /// <summary>
    /// The numeric payload in a message is formatted with the invariant culture,
    /// so it never picks up culture-specific digit grouping under a non-invariant
    /// current culture.
    /// </summary>
    /// <remarks>
    /// An <see cref="int"/> does not group in any culture's default format, so
    /// this pins the invariant-formatting call and guards against a grouped
    /// numeric format being introduced - not a decimal-separator difference. The
    /// meaningful decimal-separator culture guards arrive with float
    /// serialization in a later milestone.
    /// </remarks>
    [ Fact ]
    public void InvalidBlockSizeMessageIsCultureInvariant()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo( "fr-FR" );

            FITSException exception = FITSException.InvalidBlockSize( 123456 );

            Assert.Contains( "123456", exception.Message, StringComparison.Ordinal );
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
