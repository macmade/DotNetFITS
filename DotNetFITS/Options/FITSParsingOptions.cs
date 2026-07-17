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
/// Options controlling how strictly FITS data is parsed and validated.
/// </summary>
/// <remarks>
/// The set divides into two groups: <em>spec conveniences</em> that reassemble
/// values spread across several records (present in both <see cref="Strict"/>
/// and <see cref="Lenient"/>), and <em>leniency flags</em> that tolerate
/// technically-noncompliant input (present only in <see cref="Lenient"/>). The
/// raw bitmask is the enum's underlying value, and the Swift
/// <c>init(rawValue:)</c> maps to a cast.
/// </remarks>
[ Flags ]
public enum FITSParsingOptions
{
    /// <summary>No options; the empty set.</summary>
    None = 0,

    /// <summary>Merge consecutive <c>HISTORY</c> records into a single property.</summary>
    MergeHistoryProperties = 1 << 0,

    /// <summary>Merge consecutive <c>COMMENT</c> records into a single property.</summary>
    MergeCommentProperties = 1 << 1,

    /// <summary>Reassemble long string values split across <c>CONTINUE</c> records.</summary>
    MergeStringProperties = 1 << 2,

    /// <summary>
    /// Accept records whose value does not match any known FITS type instead of
    /// rejecting the section.
    /// </summary>
    AllowUnknownProperties = 1 << 3,

    /// <summary>
    /// Tolerate non-blank characters between a string value's closing quote and
    /// its comment delimiter, dropping them instead of failing.
    /// </summary>
    AllowTrailingQuoteJunk = 1 << 4,

    /// <summary>
    /// Accept header text containing characters outside the printable FITS range
    /// (<c>0x20</c> to <c>0x7E</c>).
    /// </summary>
    AllowNonPrintableHeaderText = 1 << 5,

    /// <summary>
    /// Accept a data segment whose length does not match the size implied by the
    /// header geometry.
    /// </summary>
    AllowDataLengthMismatch = 1 << 6,

    /// <summary>
    /// Accept a value indicator (<c>=</c>) not followed by the mandatory space,
    /// reclassifying the remainder of the record as a comment instead of failing.
    /// </summary>
    AllowMissingValueIndicatorSpace = 1 << 7,

    /// <summary>
    /// Accept lowercase <c>e</c>/<c>d</c> exponent markers in floating-point
    /// values, classifying them as floats instead of unknown values. FITS 4.0
    /// requires the uppercase <c>E</c>/<c>D</c> markers, which strict parsing
    /// still enforces.
    /// </summary>
    AllowLowercaseExponents = 1 << 8,

    /// <summary>
    /// Treat the NUL byte (<c>0x00</c>) as record padding, so NUL-padded or
    /// NUL-terminated keywords and <c>END</c> markers are recognized. FITS 4.0
    /// pads with the ASCII space (<c>0x20</c>) only, which strict parsing still
    /// enforces.
    /// </summary>
    /// <remarks>
    /// This flag is scoped to keyword-name and <c>END</c>-marker recognition
    /// only. To extend NUL-aware padding to value and comment fields, also set
    /// <see cref="AllowNulPaddingInValues"/>.
    /// </remarks>
    AllowNulPadding = 1 << 9,

    /// <summary>
    /// Accept a file whose total length is not a multiple of the 2880-byte block
    /// size by zero-padding the trailing partial block to full size. FITS 4.0
    /// requires whole blocks, which strict parsing still enforces.
    /// </summary>
    AllowTrailingPartialBlock = 1 << 10,

    /// <summary>
    /// Tolerate non-blank records following the <c>END</c> marker, dropping them
    /// from a section's properties instead of failing. FITS 4.0 allows only blank
    /// padding after <c>END</c>, which strict parsing still enforces. The dropped
    /// records' bytes are retained, so the file still round-trips.
    /// </summary>
    AllowContentAfterEnd = 1 << 11,

    /// <summary>
    /// Treat the NUL byte (<c>0x00</c>) as record padding in value and comment
    /// fields, so a NUL-padded or NUL-terminated value such as <c>T\0\0\0</c> is
    /// trimmed and classified normally rather than left as an unknown value.
    /// </summary>
    /// <remarks>
    /// Complements <see cref="AllowNulPadding"/> (which covers only keyword names
    /// and the <c>END</c> marker); the two are independent and may be set
    /// separately. FITS 4.0 pads with the ASCII space (<c>0x20</c>) only, which
    /// strict parsing still enforces.
    /// </remarks>
    AllowNulPaddingInValues = 1 << 12,

    /// <summary>
    /// Tolerate a <c>CONTINUE</c> record that cannot be merged into a predecessor
    /// - because there is no preceding property, or the predecessor is not a
    /// string ending in the <c>&amp;</c> continuation flag - by keeping it as a
    /// standalone property instead of rejecting the section. Requires
    /// <see cref="MergeStringProperties"/> to be set (otherwise no merge is
    /// attempted and <c>CONTINUE</c> records are already standalone).
    /// </summary>
    AllowOrphanedContinue = 1 << 13,

    /// <summary>
    /// Spec-faithful parsing: reconstructs multi-record values but rejects any
    /// input the FITS standard forbids.
    /// </summary>
    Strict = MergeHistoryProperties | MergeCommentProperties | MergeStringProperties,

    /// <summary>
    /// Real-world-friendly parsing: like <see cref="Strict"/> but tolerates the
    /// noncompliant constructs found in many existing FITS files.
    /// </summary>
    Lenient = Strict
            | AllowUnknownProperties
            | AllowTrailingQuoteJunk
            | AllowNonPrintableHeaderText
            | AllowDataLengthMismatch
            | AllowMissingValueIndicatorSpace
            | AllowLowercaseExponents
            | AllowNulPadding
            | AllowTrailingPartialBlock
            | AllowContentAfterEnd
            | AllowNulPaddingInValues
            | AllowOrphanedContinue,
}
