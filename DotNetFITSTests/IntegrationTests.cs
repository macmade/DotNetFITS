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
using System.IO;
using System.Linq;
using System.Text;
using DotNetFITS;

namespace DotNetFITSTests;

/// <summary>
/// End-to-end tests spanning the whole read/write pipeline: building files from
/// scratch, editing parsed files, managing extensions, and writing to disk,
/// exercising the <see cref="FITSFile"/>, <see cref="FITSSection"/> and
/// <see cref="FITSProperty"/> layers together.
/// </summary>
public class IntegrationTests
{
    /// <summary>
    /// A 2x2 8-bit image is built, tagged with a keyword, written to disk and read
    /// back; then reshaped to a 3x3 16-bit image with new pixels, written again and
    /// reread. The final geometry, the preserved keyword and the new data all
    /// survive the whole round-trip.
    /// </summary>
    [ Fact ]
    public void ImageFileFullLifecycle()
    {
        byte[] pixels = Enumerable.Repeat( ( byte )0x7F, 18 ).ToArray();
        string path   = Path.Combine( Path.GetTempPath(), Guid.NewGuid().ToString() + ".fits" );

        try
        {
            FITSFile file = new FITSFile( 8, [ 2, 2 ], new byte[] { 1, 2, 3, 4 } );

            file.Header?.SetProperty( new FITSProperty( "OBJECT", "M42", FITSSerializationOptions.Strict ) );
            file.Write( path, FITSSerializationOptions.Strict );

            FITSFile reread = new FITSFile( path, FITSParsingOptions.Strict );

            Assert.Equal( FITSValue.String( "M42" ), reread.Header?[ "OBJECT" ]?.Value );
            Assert.Equal( 2L, reread.Header?.Naxis );

            reread.SetPrimaryData( 16, [ 3, 3 ], pixels );
            reread.Write( path, FITSSerializationOptions.Strict );

            FITSFile     finalFile = new FITSFile( path, FITSParsingOptions.Strict );
            FITSSection? segment   = finalFile.Sections.FirstOrDefault( section => section.SectionKind == FITSSection.Kind.Data );

            Assert.NotNull( segment );
            Assert.Equal( 16L, finalFile.Header?.Bitpix );
            Assert.Equal( 3L,  finalFile.Header?.NaxisAt( 1 ) );
            Assert.Equal( 3L,  finalFile.Header?.NaxisAt( 2 ) );
            Assert.Equal( FITSValue.String( "M42" ), finalFile.Header?[ "OBJECT" ]?.Value );
            Assert.Equal( pixels, segment.Data.Slice( 0, 18 ).ToArray() );
        }
        finally
        {
            TestUtilities.RemoveTemporaryFile( path );
        }
    }

    /// <summary>
    /// A NAXIS = 0 primary plus a TABLE extension (a 4x2 character array) built
    /// from scratch serializes and re-parses with its <c>EXTEND</c> declaration,
    /// geometry and data intact.
    /// </summary>
    [ Fact ]
    public void TableExtensionFileRoundTrips()
    {
        byte[]   payload = Encoding.ASCII.GetBytes( "ABCDEFGH" );
        FITSFile file    = new FITSFile( 8, [] );

        file.AppendExtension( "TABLE", 8, [ 4, 2 ], data: payload );

        FITSFile     reparsed = new FITSFile( file.SerializedData( FITSSerializationOptions.Strict ), FITSParsingOptions.Strict );
        FITSSection? ext      = reparsed.Extensions.FirstOrDefault();
        FITSSection? segment  = reparsed.Sections.LastOrDefault();

        Assert.NotNull( ext );
        Assert.NotNull( segment );
        Assert.Single( reparsed.Extensions );
        Assert.Equal( FITSValue.Logical( true ),   reparsed.Header?[ "EXTEND" ]?.Value );
        Assert.Equal( FITSValue.String( "TABLE" ), ext[ "XTENSION" ]?.Value );
        Assert.Equal( 2L, ext.Naxis );
        Assert.Equal( 4L, ext.NaxisAt( 1 ) );
        Assert.Equal( 2L, ext.NaxisAt( 2 ) );
        Assert.Equal( payload, segment.Data.Slice( 0, 8 ).ToArray() );
    }

    /// <summary>
    /// A primary plus two extensions are parsed so every section is clean; editing
    /// only the first extension's header lands the change while the primary and
    /// the second extension are re-emitted byte-for-byte.
    /// </summary>
    [ Fact ]
    public void MultiHduEditPreservesUntouchedSectionsByteForByte()
    {
        FITSFile builder = new FITSFile( 8, [ 2, 2 ], new byte[] { 1, 2, 3, 4 } );

        builder.AppendExtension( "IMAGE", 8, [ 2, 2 ], data: new byte[] { 5, 6, 7, 8 } );
        builder.AppendExtension( "IMAGE", 8, [ 3 ],    data: new byte[] { 9, 10, 11 } );

        FITSFile     file      = new FITSFile( builder.SerializedData( FITSSerializationOptions.Strict ), FITSParsingOptions.Strict );
        FITSSection? primary   = file.Header;
        FITSSection? secondExt = file.Extensions.LastOrDefault();

        Assert.NotNull( primary );
        Assert.NotNull( secondExt );

        byte[] primaryBefore   = primary.Data.ToArray();
        byte[] secondExtBefore = secondExt.Data.ToArray();

        file.Extensions.FirstOrDefault()?.SetProperty( new FITSProperty( "EXTNAME", "SCI", FITSSerializationOptions.Strict ) );

        FITSFile reparsed = new FITSFile( file.SerializedData( FITSSerializationOptions.Strict ), FITSParsingOptions.Strict );

        Assert.Equal( FITSValue.String( "SCI" ), reparsed.Extensions.FirstOrDefault()?[ "EXTNAME" ]?.Value );
        Assert.Equal( primaryBefore,   reparsed.Header?.Data.ToArray() );
        Assert.Equal( secondExtBefore, reparsed.Extensions.LastOrDefault()?.Data.ToArray() );
    }

    /// <summary>
    /// Three extensions with distinct dimensionalities are built; dropping the
    /// middle one and moving the last to the front leaves the expected surviving
    /// order after a serialize/re-parse round-trip.
    /// </summary>
    [ Fact ]
    public void BuildRemoveAndReorderExtensions()
    {
        FITSFile file = new FITSFile( 8, [] );

        file.AppendExtension( "IMAGE", 8, [ 1 ],       data: new byte[] { 1 } );
        file.AppendExtension( "IMAGE", 8, [ 2, 1 ],    data: new byte[] { 2, 3 } );
        file.AppendExtension( "IMAGE", 8, [ 3, 1, 1 ], data: new byte[] { 4, 5, 6 } );

        Assert.Equal( 3, file.Extensions.Count );

        file.RemoveExtension( 1 );
        file.MoveExtension( 1, 0 );

        FITSFile reparsed = new FITSFile( file.SerializedData( FITSSerializationOptions.Strict ), FITSParsingOptions.Strict );

        Assert.Equal( 2, reparsed.Extensions.Count );
        Assert.Equal( 3L, reparsed.Extensions[ 0 ].Naxis );
        Assert.Equal( 1L, reparsed.Extensions[ 1 ].Naxis );
    }

    /// <summary>
    /// A geometry/data-size mismatch is rejected by strict serialization and
    /// tolerated by lenient; writing to an unwritable location and out-of-range
    /// extension operations all throw rather than trap.
    /// </summary>
    [ Fact ]
    public void StrictVsLenientSerializationAndErrorPaths()
    {
        FITSFile mismatched = new FITSFile( 8, [ 5760 ], new byte[ 100 ] );

        Assert.Throws< FITSException >( () => mismatched.SerializedData( FITSSerializationOptions.Strict ) );

        _ = mismatched.SerializedData( FITSSerializationOptions.Lenient );

        FITSFile file = new FITSFile( 8, [] );
        string   path = $"/no/such/directory/{ Guid.NewGuid().ToString() }.fits";

        Assert.Throws< FITSException >( () => file.Write( path, FITSSerializationOptions.Strict ) );
        Assert.Throws< FITSException >( () => file.RemoveExtension( 0 ) );
        Assert.Throws< FITSException >( () => file.MoveExtension( 0, 0 ) );
    }
}
