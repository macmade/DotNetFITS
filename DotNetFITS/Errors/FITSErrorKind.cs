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

namespace DotNetFITS;

/// <summary>
/// Identifies which kind of error a <see cref="FITSException"/> represents.
/// </summary>
/// <remarks>
/// A discriminator a consumer can branch on to identify which error a caught
/// <see cref="FITSException"/> represents.
/// </remarks>
public enum FITSErrorKind
{
    /// <summary>
    /// The provided path does not point to a readable file (for example it is
    /// missing or refers to a directory).
    /// </summary>
    InvalidFileURL,

    /// <summary>The file at the given path exists but its contents could not be read.</summary>
    CannotReadFile,

    /// <summary>A block does not have the mandatory 2880-byte FITS block size.</summary>
    InvalidBlockSize,

    /// <summary>A block's contents are invalid for its role.</summary>
    InvalidBlockData,

    /// <summary>A header or extension section is malformed.</summary>
    InvalidSectionData,

    /// <summary>The overall file structure is invalid.</summary>
    InvalidFileData,

    /// <summary>A single 80-byte header record could not be parsed.</summary>
    InvalidPropertyData,

    /// <summary>A low-level data operation failed.</summary>
    DataError,

    /// <summary>A value could not be rendered to its FITS serialized form.</summary>
    InvalidValueForSerialization,

    /// <summary>A file or section could not be serialized to FITS data.</summary>
    CannotSerialize,

    /// <summary>The serialized data could not be written to the given path.</summary>
    CannotWriteFile,
}
