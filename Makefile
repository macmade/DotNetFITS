#-------------------------------------------------------------------------------
# Copyright (c) 2026, Jean-David Gadina - www.xs-labs.com
# Distributed under the terms of the MIT License.
#
# Builds the DotNetFITS class library, runs the xUnit test suite and produces
# the NuGet package. The whole toolchain is cross-platform, so every target runs
# unchanged on macOS, Linux and Windows.
#
# The .NET SDK is used through the `DOTNET` variable, which prefers a `dotnet`
# found on `PATH` and otherwise falls back to the default macOS install location.
# Override it on the command line if your SDK lives elsewhere (`make DOTNET=...`).
#-------------------------------------------------------------------------------

DIR_ROOT            := $(realpath .)/
DIR_BUILD           := $(DIR_ROOT)Build/

SOLUTION            := DotNetFITS.slnx
PROJECT             := DotNetFITS/DotNetFITS.csproj

CONFIGURATION       := Release

DOTNET              := $(shell command -v dotnet || echo /usr/local/share/dotnet/dotnet)

.PHONY: all build test pack clean

all: build test

	@:

build:

	@$(DOTNET) build $(SOLUTION) -c $(CONFIGURATION)

test:

	@$(DOTNET) test $(SOLUTION) -c $(CONFIGURATION)

pack:

	@mkdir -p $(DIR_BUILD)
	@$(DOTNET) pack $(PROJECT) -c $(CONFIGURATION) -o $(DIR_BUILD)

clean:

	@$(DOTNET) clean $(SOLUTION) -c $(CONFIGURATION)
	@rm -f $(DIR_BUILD)*.nupkg $(DIR_BUILD)*.snupkg
