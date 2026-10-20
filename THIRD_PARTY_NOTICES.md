# Third-party notices

OBD Workbench includes an implementation of the Land Rover TD5 SecurityAccess seed/key algorithm informed by the public `pajacobson/td5keygen` project. That project is distributed under the BSD 2-Clause License. The required notice is reproduced below.

## td5keygen

Copyright (c) 2017, paul@discotd5.com
All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

Source: https://github.com/pajacobson/td5keygen

## Other references

Other public projects and documentation used as protocol/reference material are credited in `OBDWorkbench/Docs/SOURCES.md`. Their binaries/source code are not bundled into this release unless separately identified by their own notice.

## Project-authored code and assets

The root MIT LICENSE applies to Icarus-authored implementation, documentation and new branding. It does not change the licenses of third-party references, .NET or operating-system components. The original TD5 BSD notice above is retained.

## Runtime and package dependencies

Workbench/GPS build on Microsoft .NET 8, WPF/Windows Forms and System.IO.Ports. Package references and versions are declared in the project files. Keep the notices and license files supplied by Microsoft/NuGet publish outputs when distributing self-contained binaries. No .NET runtime binaries are bundled in this source ZIP.

## Subaru factual mapping provenance

ReferenceData/SSM_DTC_MAP.csv records factual DTC addresses/flags generated from the pinned FreeSSM revision described in ReferenceData/README.md and COMMUNICATIONS_REFERENCE.md. FreeSSM's GPL provenance is retained; this package does not claim to relicense FreeSSM. Review this provenance before first public distribution. No FreeSSM implementation source or binaries are bundled.
