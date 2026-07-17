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
/// A parsed FITS (Flexible Image Transport System) file.
/// </summary>
/// <remarks>
/// Defines the FITS size constants fixed by the standard, which the rest of the
/// library relies on for block, card and field geometry. Parsing, validation,
/// construction and serialization are added by the file layer.
/// </remarks>
public class FITSFile
{
    /// <summary>
    /// The size, in bytes, of a single FITS block. Fixed by the standard at
    /// 2880.
    /// </summary>
    public const int BlockSize = 2880;

    /// <summary>
    /// The size, in bytes, of a single FITS header record (card). Fixed by the
    /// standard at 80.
    /// </summary>
    public const int CardSize = 80;

    /// <summary>
    /// The length, in bytes, of the keyword-name field at the start of a header
    /// record. Fixed by the standard at 8.
    /// </summary>
    public const int KeywordLength = 8;

    /// <summary>
    /// The width, in bytes, of the fixed-format value field (bytes 11-30), in
    /// which scalar values are right-justified per FITS 4.0 section 4.2.
    /// </summary>
    public const int FixedValueFieldWidth = 20;

    /// <summary>
    /// An upper bound, in bytes, on a single data segment.
    /// </summary>
    /// <remarks>
    /// A geometry implying a larger segment is rejected as corrupt rather than
    /// yielding a meaningless multi-exabyte expected size. The ceiling sits far
    /// above any real FITS file (approximately 9 PB) yet safely within a signed
    /// 64-bit integer, so the size math can never overflow once a value passes
    /// it.
    /// </remarks>
    public const long MaxDataSize = 1L << 53;
}
