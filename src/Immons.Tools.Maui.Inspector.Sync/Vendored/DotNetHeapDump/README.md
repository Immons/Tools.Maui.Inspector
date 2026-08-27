# DotNetHeapDump (vendored)

Reader for `.gcdump` files, copied verbatim from
[dotnet/diagnostics](https://github.com/dotnet/diagnostics/tree/main/src/Tools/dotnet-gcdump/DotNetHeapDump)
(which in turn mirrors [microsoft/perfview](https://github.com/microsoft/PerfView)): `GCHeapDump.cs`,
`MemoryGraph.cs`, `Graph.cs`, `DotNetHeapInfo.cs`. The only edits are the `#nullable disable` /
`#pragma warning disable` lines at the top of each file. Treat the files as read-only — the format is
theirs, and the reader has to stay bit-compatible with what dotnet-gcdump writes.

`FastSerialization` (the stream format) comes from the `Microsoft.Diagnostics.Tracing.TraceEvent` package.

## License (microsoft/perfview, dotnet/diagnostics)

The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
