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
/// A contiguous run of FITS blocks forming one logical unit of a file - a primary
/// header, an extension header, or a data segment - together with the properties
/// parsed from it.
/// </summary>
/// <remarks>
/// Block accumulation, finalization and serialization are added by the section
/// layer. For now the type carries only the dirty-serialization flag that an owned
/// <see cref="FITSProperty"/> sets when its value or comment is edited in place, so
/// an otherwise-untouched section can re-emit its retained bytes byte-for-byte
/// until it is actually modified.
/// </remarks>
public sealed class FITSSection
{
    /// <summary>
    /// A value indicating whether this section has in-place edits that require it
    /// to be re-serialized rather than re-emitting its retained bytes.
    /// </summary>
    public bool NeedsSerialization { get; private set; }

    /// <summary>
    /// Marks this section as needing re-serialization, called when one of its
    /// properties is edited in place.
    /// </summary>
    internal void MarkNeedsSerialization()
    {
        this.NeedsSerialization = true;
    }
}
