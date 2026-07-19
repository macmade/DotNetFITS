DotNetFITS
==========

[![Build Status](https://img.shields.io/github/actions/workflow/status/macmade/DotNetFITS/ci-win.yaml?label=Windows&logo=dotnet)](https://github.com/macmade/DotNetFITS/actions/workflows/ci-win.yaml)
[![NuGet](https://img.shields.io/nuget/v/DotNetFITS.svg?logo=nuget)](https://www.nuget.org/packages/DotNetFITS)
[![Issues](http://img.shields.io/github/issues/macmade/DotNetFITS.svg?logo=github)](https://github.com/macmade/DotNetFITS/issues)
![Status](https://img.shields.io/badge/status-active-brightgreen.svg?logo=git)
![License](https://img.shields.io/badge/license-mit-brightgreen.svg?logo=open-source-initiative)  
[![Contact](https://img.shields.io/badge/follow-@macmade-blue.svg?logo=twitter&style=social)](https://twitter.com/macmade)
[![Sponsor](https://img.shields.io/badge/sponsor-macmade-pink.svg?logo=github-sponsors&style=social)](https://github.com/sponsors/macmade)

### About

FITS Image Library for C# / .NET.

This library provides a simple interface to read and write FITS files in C#, based on the [FITS 4.0 standard](https://fits.gsfc.nasa.gov/fits_standard.html).

### Status

DotNetFITS supports both **reading and writing**. It parses existing FITS files into their header/data
structure and serializes them back to standards-compliant bytes. A compliant file round-trips
byte-for-byte; you can also build new files from scratch, and edit parsed files in place — only the
sections you modify are re-rendered, while every untouched section keeps its original bytes.

### Conformance & Limitations

DotNetFITS targets the base [FITS 4.0 standard](https://fits.gsfc.nasa.gov/fits_standard.html). The
following properties are intentional, not latent surprises:

- **Keyword names** may contain only `A`–`Z`, `0`–`9`, `_` and `-`, and must be left-justified in the
  8-byte keyword field (a leading space is rejected).
- **Long-keyword conventions** are out of scope: `HIERARCH` and similar conventions are treated as
  ordinary 8-byte keywords, not expanded. Only `CONTINUE`/`HISTORY`/`COMMENT` multi-record merging is
  supported.
- **Mandatory keywords** (`SIMPLE`/`XTENSION`, `BITPIX`, `NAXIS`, `NAXISn`, and `PCOUNT`/`GCOUNT` for
  extensions) must appear in their exact standard order and index — this is enforced even in lenient
  mode.
- **Section layout is geometry-driven**: each header's declared geometry
  (`|BITPIX|/8 × GCOUNT × (PCOUNT + ∏ NAXISn)`, with random groups handled) determines exactly how many
  data blocks follow, rather than guessing from byte content. Trailing all-blank padding is preserved.
- **`END`** is excluded from a section's `Properties` but is retained in the raw bytes, so a compliant
  file round-trips byte-for-byte through `FITSFile.Data`.
- **Float values** are serialized to their shortest round-trippable decimal form with a guaranteed
  decimal point or exponent (an integral value such as `42.0` keeps its `.0`), so a floating-point
  keyword re-parses as a float rather than an integer. All numeric parsing and formatting is
  culture-invariant.
- **Strict vs. lenient**: `FITSParsingOptions.Strict` rejects technically-noncompliant input, while
  `FITSParsingOptions.Lenient` tolerates common real-world deviations (unknown value types, trailing
  characters after a string's closing quote, non-printable header text, data-length mismatches, a
  missing space after the `=` value indicator, lowercase `e`/`d` float exponents, NUL-padded headers, a
  truncated trailing partial block, non-blank records after the `END` marker, NUL padding in
  value/comment fields, and orphaned `CONTINUE` records).
- **Serialization is symmetric**: `FITSSerializationOptions` mirrors the parsing options, with `Strict`
  and `Lenient` presets. On write, mandatory keywords, `BITPIX`/`NAXIS` geometry and each data segment's
  size are validated — `Strict` rejects any mismatch, while `Lenient` tolerates a data-size mismatch and
  coerces keyword case. The library manages the `END` marker and 2880-byte padding; the caller owns the
  mandatory keywords and their order.
- **Not thread-safe**: `FITSFile`, `FITSSection` and `FITSProperty` carry mutable state, and `FITSBlock`
  caches structural facts lazily on read, so instances must not be shared across threads without external
  synchronization.

### Requirements

DotNetFITS is written in pure C# and targets **.NET 10**, with no third-party dependencies. It is
continuously built and tested on Windows (see the CI badge above) in both Debug and Release
configurations. Nothing platform-specific is used, so the library runs anywhere .NET 10 does.

### Installation

DotNetFITS is available on [NuGet](https://www.nuget.org/packages/DotNetFITS). Add it to your project
with the .NET CLI:

```bash
dotnet add package DotNetFITS
```

Or add a `<PackageReference>` to your project file:

```xml
<PackageReference Include="DotNetFITS" Version="1.0.0" />
```

### Building

The solution (`DotNetFITS.slnx`) contains the `DotNetFITS` class library and the `DotNetFITSTests`
xUnit test suite:

```bash
dotnet build DotNetFITS.slnx -c Release
dotnet test  DotNetFITS.slnx -c Release
```

### Example Usage

#### Reading

```csharp
using System;
using DotNetFITS;

try
{
    FITSFile file = new FITSFile( "/path/to/file.fits", FITSParsingOptions.Lenient );

    if( file.Header is FITSSection header )
    {
        foreach( FITSProperty property in header.Properties )
        {
            Console.WriteLine( property );
        }
    }
}
catch( FITSException exception )
{
    Console.WriteLine( exception.Message );
}
```

#### Writing & serialization

Build a file from scratch — a primary image HDU plus an optional extension — and write it out. The
mandatory keywords (`SIMPLE`/`XTENSION`, `BITPIX`, `NAXIS`, `NAXISn`, …) are populated for you:

```csharp
using DotNetFITS;

// A 3x2 8-bit image: six bytes of pixel data.
FITSFile file = new FITSFile( 8, [ 3, 2 ], new byte[] { 1, 2, 3, 4, 5, 6 } );

// Append a 4x4 16-bit image extension (4 * 4 * 2 = 32 bytes of data).
file.AppendExtension( "IMAGE", 16, [ 4, 4 ], data: new byte[ 32 ] );

file.Write( "/path/to/out.fits", FITSSerializationOptions.Strict );
```

Edit a parsed file and write it back. Only the sections you touch are re-rendered; everything else is
preserved byte-for-byte:

```csharp
using System;
using DotNetFITS;

FITSFile file = new FITSFile( "/path/to/file.fits", FITSParsingOptions.Lenient );

// Set or replace a keyword on the primary header.
file.Header?.SetProperty( new FITSProperty( "OBJECT", "M42", FITSSerializationOptions.Strict ) );

// Reshape the primary data (512x512 16-bit = 512 * 512 * 2 bytes), keeping its
// geometry keywords in sync.
byte[] pixels = new byte[ 512 * 512 * 2 ];

file.SetPrimaryData( 16, [ 512, 512 ], pixels );

// Serialize to bytes, or write straight to disk.
ReadOnlyMemory< byte > bytes = file.SerializedData( FITSSerializationOptions.Lenient );

file.Write( "/path/to/edited.fits", FITSSerializationOptions.Lenient );
```

License
-------

Project is released under the terms of the MIT License.

Repository Infos
----------------

    Owner:          Jean-David Gadina - XS-Labs
    Web:            www.xs-labs.com
    Blog:           www.noxeos.com
    Twitter:        @macmade
    GitHub:         github.com/macmade
    LinkedIn:       ch.linkedin.com/in/macmade/
    StackOverflow:  stackoverflow.com/users/182676/macmade
