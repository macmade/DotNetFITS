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
/// The exception thrown by DotNetFITS when reading, validating or serializing
/// FITS data.
/// </summary>
/// <remarks>
/// A single exception type carrying a <see cref="FITSErrorKind"/> discriminator
/// and constructed through one static factory per error case. Every message is
/// prefixed with <c>FITS Error: </c>.
/// </remarks>
public sealed class FITSException : Exception
{
    /// <summary>The prefix applied to every FITS error message.</summary>
    private const string MessagePrefix = "FITS Error: ";

    /// <summary>The kind of error this exception represents.</summary>
    public FITSErrorKind Kind { get; }

    /// <summary>
    /// Initializes a new instance for the given kind and specific description.
    /// </summary>
    /// <param name="kind">The kind of error.</param>
    /// <param name="description">
    /// The specific description, appended to the shared FITS error prefix.
    /// </param>
    private FITSException( FITSErrorKind kind, string description ) : base( MessagePrefix + description )
    {
        this.Kind = kind;
    }

    /// <summary>Creates an <see cref="FITSErrorKind.InvalidFileURL"/> error.</summary>
    /// <param name="path">The offending file path.</param>
    /// <returns>The created exception.</returns>
    public static FITSException InvalidFileURL( string path ) => new FITSException( FITSErrorKind.InvalidFileURL, $"Invalid file URL: {path}" );

    /// <summary>Creates a <see cref="FITSErrorKind.CannotReadFile"/> error.</summary>
    /// <param name="path">The file that could not be read.</param>
    /// <returns>The created exception.</returns>
    public static FITSException CannotReadFile( string path ) => new FITSException( FITSErrorKind.CannotReadFile, $"Cannot read file: {path}" );

    /// <summary>Creates an <see cref="FITSErrorKind.InvalidBlockSize"/> error.</summary>
    /// <param name="size">The offending block size, in bytes.</param>
    /// <returns>The created exception.</returns>
    public static FITSException InvalidBlockSize( int size ) => new FITSException( FITSErrorKind.InvalidBlockSize, $"Invalid block size: {size.ToString( CultureInfo.InvariantCulture )}" );

    /// <summary>Creates an <see cref="FITSErrorKind.InvalidBlockData"/> error.</summary>
    /// <param name="reason">A description of the specific problem.</param>
    /// <returns>The created exception.</returns>
    public static FITSException InvalidBlockData( string reason ) => new FITSException( FITSErrorKind.InvalidBlockData, $"Invalid block data: {reason}" );

    /// <summary>Creates an <see cref="FITSErrorKind.InvalidSectionData"/> error.</summary>
    /// <param name="reason">A description of the specific problem.</param>
    /// <returns>The created exception.</returns>
    public static FITSException InvalidSectionData( string reason ) => new FITSException( FITSErrorKind.InvalidSectionData, $"Invalid section data: {reason}" );

    /// <summary>Creates an <see cref="FITSErrorKind.InvalidFileData"/> error.</summary>
    /// <param name="reason">A description of the specific problem.</param>
    /// <returns>The created exception.</returns>
    public static FITSException InvalidFileData( string reason ) => new FITSException( FITSErrorKind.InvalidFileData, $"Invalid file data: {reason}" );

    /// <summary>Creates an <see cref="FITSErrorKind.InvalidPropertyData"/> error.</summary>
    /// <param name="reason">A description of the specific problem.</param>
    /// <returns>The created exception.</returns>
    public static FITSException InvalidPropertyData( string reason ) => new FITSException( FITSErrorKind.InvalidPropertyData, $"Invalid property data: {reason}" );

    /// <summary>Creates a <see cref="FITSErrorKind.DataError"/> error.</summary>
    /// <param name="reason">A description of the specific problem.</param>
    /// <returns>The created exception.</returns>
    public static FITSException DataError( string reason ) => new FITSException( FITSErrorKind.DataError, $"Data error: {reason}" );

    /// <summary>Creates an <see cref="FITSErrorKind.InvalidValueForSerialization"/> error.</summary>
    /// <param name="reason">A description of the specific problem.</param>
    /// <returns>The created exception.</returns>
    public static FITSException InvalidValueForSerialization( string reason ) => new FITSException( FITSErrorKind.InvalidValueForSerialization, $"Invalid value for serialization: {reason}" );

    /// <summary>Creates a <see cref="FITSErrorKind.CannotSerialize"/> error.</summary>
    /// <param name="reason">A description of the specific problem.</param>
    /// <returns>The created exception.</returns>
    public static FITSException CannotSerialize( string reason ) => new FITSException( FITSErrorKind.CannotSerialize, $"Cannot serialize: {reason}" );

    /// <summary>Creates a <see cref="FITSErrorKind.CannotWriteFile"/> error.</summary>
    /// <param name="path">The path that could not be written.</param>
    /// <returns>The created exception.</returns>
    public static FITSException CannotWriteFile( string path ) => new FITSException( FITSErrorKind.CannotWriteFile, $"Cannot write file: {path}" );
}
