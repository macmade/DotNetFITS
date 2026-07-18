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

namespace DotNetFITS;

/// <summary>
/// Options controlling how strictly FITS data is validated and rendered when
/// serialized back to bytes.
/// </summary>
/// <remarks>
/// The write-side counterpart to <see cref="FITSParsingOptions"/>. It offers the
/// same two presets - <see cref="Strict"/> and <see cref="Lenient"/> - so a
/// consumer can choose between spec-faithful output that rejects anything the
/// FITS standard forbids and real-world-friendly output that tolerates the same
/// noncompliant constructs the parser accepts. The raw bitmask is the enum's
/// underlying value.
/// </remarks>
[ Flags ]
public enum FITSSerializationOptions
{
    /// <summary>No options; the empty set, equivalent to <see cref="Strict"/>.</summary>
    None = 0,

    /// <summary>
    /// Coerce an otherwise-invalid keyword name into the FITS keyword character
    /// set by upper-casing it, rather than rejecting the record.
    /// </summary>
    /// <remarks>
    /// Only case is corrected: a name that is still outside the FITS keyword set
    /// after upper-casing, or that is longer than
    /// <see cref="FITSFile.KeywordLength"/>, is rejected regardless of this flag.
    /// </remarks>
    CoerceInvalidKeywords = 1 << 0,

    /// <summary>
    /// Emit a file whose data-segment size does not match the size implied by its
    /// header geometry, instead of rejecting it on write.
    /// </summary>
    /// <remarks>
    /// The write-side counterpart to
    /// <see cref="FITSParsingOptions.AllowDataLengthMismatch"/>. Mandatory
    /// keywords and section ordering are still validated regardless of this flag.
    /// </remarks>
    AllowDataSizeMismatch = 1 << 1,

    /// <summary>
    /// Spec-faithful serialization: emits standards-compliant bytes and rejects
    /// any content the FITS standard forbids.
    /// </summary>
    Strict = None,

    /// <summary>
    /// Real-world-friendly serialization: like <see cref="Strict"/> but tolerates
    /// the noncompliant constructs found in many existing FITS files.
    /// </summary>
    Lenient = CoerceInvalidKeywords | AllowDataSizeMismatch,
}
