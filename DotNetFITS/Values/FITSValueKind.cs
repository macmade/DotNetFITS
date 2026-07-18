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
/// The type discriminator of a <see cref="FITSValue"/>, independent of any
/// payload.
/// </summary>
/// <remarks>
/// Used to compare or validate a value's type without unwrapping it. The member
/// names double as the human-readable descriptions
/// (<see cref="object.ToString"/> returns them verbatim).
/// </remarks>
public enum FITSValueKind
{
    /// <summary>The kind of a record that carries no value (e.g. <c>COMMENT</c>, <c>HISTORY</c>).</summary>
    /// <remarks>The enum's zero value, so <c>default(FITSValue)</c> is a neutral undefined value.</remarks>
    Undefined,

    /// <summary>The kind of a logical (boolean) value, written <c>T</c> or <c>F</c>.</summary>
    Logical,

    /// <summary>The kind of an integer value representable as an <see cref="System.Int64"/>.</summary>
    Integer,

    /// <summary>The kind of a floating-point value.</summary>
    Float,

    /// <summary>The kind of a character-string value.</summary>
    String,

    /// <summary>
    /// The kind of a value that matches no known FITS type, or that matches the
    /// integer/float grammar but overflows its range.
    /// </summary>
    Unknown,
}
