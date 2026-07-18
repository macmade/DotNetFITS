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

namespace DotNetFITSTests;

/// <summary>
/// A generic failure used by the test helpers for their own preconditions.
/// </summary>
/// <remarks>
/// Test-only on purpose: the library's public <see cref="DotNetFITS.FITSException"/>
/// must contain only errors the library itself emits, so the fixtures signal
/// their own misuse with this dedicated type instead. Ports the Swift test-only
/// <c>TestError</c>.
/// </remarks>
internal sealed class TestError : Exception
{
    /// <summary>
    /// Initializes a new instance describing an invalid precondition.
    /// </summary>
    /// <param name="reason">A description of the precondition that failed.</param>
    private TestError( string reason ) : base( reason )
    {}

    /// <summary>
    /// Creates an error describing an invalid precondition.
    /// </summary>
    /// <param name="reason">A description of the precondition that failed.</param>
    /// <returns>The created error.</returns>
    internal static TestError Invalid( string reason ) => new TestError( reason );
}
