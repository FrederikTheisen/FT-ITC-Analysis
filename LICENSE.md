
FT‑ITC Analysis – License and third‑party notices
=================================================

Copyright (c) 2026 Frederik Theisen

This project, **FT‑ITC Analysis**, is distributed under the MIT License.  The
following sections reproduce the text of the MIT License, followed by the
license notices for the public packages and other third-party material
redistributed by the application.

MIT License
-----------

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

Third-party packages and material
----------------------------------

The following components are included in one or more application, web, or
legacy macOS distributions.  The MIT license text above applies to every
component marked **MIT**, but each copyright notice and source reference is
retained here as part of the notice.

| Component and version | License | Copyright / source |
| --- | --- | --- |
| `Accord` 3.8.0; `Accord.Math` 3.8.0 | LGPL-2.1 | Accord.NET; [upstream license](https://raw.githubusercontent.com/accord-net/framework/development/LICENSE), [source](https://github.com/accord-net/framework) |
| `MathNet.Numerics` 5.0.0 | MIT | Copyright Math.NET Project; [source](https://github.com/mathnet/mathnet-numerics) |
| `Newtonsoft.Json` 13.0.2 | MIT | Copyright © James Newton-King 2008; [source](https://github.com/JamesNK/Newtonsoft.Json) |
| `Avalonia`, `Avalonia.Desktop`, `Avalonia.Fonts.Inter`, `Avalonia.FreeDesktop`, `Avalonia.FreeDesktop.AtSpi`, `Avalonia.HarfBuzz`, `Avalonia.Native`, `Avalonia.Remote.Protocol`, `Avalonia.Skia`, `Avalonia.Themes.Fluent`, `Avalonia.Win32`, and `Avalonia.X11` 12.0.5 | MIT | Copyright 2013-2026 © The AvaloniaUI Project; [source](https://github.com/AvaloniaUI/Avalonia/) |
| `Avalonia.Angle.Windows.Natives` 2.1.27548.20260419 | BSD-3-Clause | Copyright 2018 The ANGLE Project Authors; [source](https://github.com/google/angle) |
| `AvaloniaUI.DiagnosticsSupport` 2.2.3 | MIT | Copyright 2019-2026 © AvaloniaUI OÜ; [source](https://github.com/AvaloniaUI/Avalonia.DiagnosticsSupport) |
| `SkiaSharp` and platform native assets 3.119.4 | MIT plus bundled third-party notices | © Microsoft Corporation. All rights reserved; native notices include ANGLE BSD-3-Clause and HarfBuzz Old MIT; [source](https://github.com/mono/SkiaSharp) |
| `HarfBuzzSharp` and platform native assets 8.3.1.3 | MIT plus bundled third-party notices | © Microsoft Corporation. All rights reserved; native notices include ANGLE BSD-3-Clause and HarfBuzz Old MIT; [source](https://github.com/mono/SkiaSharp) |
| `MicroCom.Runtime` 0.11.4 | MIT | Copyright 2021 © Nikita Tsukanov; [source](https://github.com/kekekeks/MicroCom) |
| `Tmds.DBus.Protocol` 0.92.0 | MIT | Copyright Tom Deseyn; [source](https://github.com/tmds/Tmds.DBus) |
| `Microsoft.Data.Sqlite` and `Microsoft.Data.Sqlite.Core` 10.0.11 | MIT | © Microsoft Corporation. All rights reserved; [source](https://github.com/dotnet/dotnet) |
| `SQLitePCLRaw.bundle_e_sqlite3`, `.core`, `.lib.e_sqlite3`, and `.provider.e_sqlite3` 2.1.12 | Apache-2.0 | Copyright 2014-2024 SourceGear, LLC; [source](https://github.com/ericsink/SQLitePCL.raw) |
| `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions` 8.0.0; `Microsoft.IO.RecyclableMemoryStream` 3.0.1 | MIT | © Microsoft Corporation. All rights reserved; [source](https://github.com/dotnet/runtime) |
| `System.Formats.Nrbf` 10.0.11; `System.Text.Json` 10.0.5; and resolved `System.*`, `Microsoft.*`, and `NETStandard.Library` support packages | MIT | © Microsoft Corporation. All rights reserved; [source](https://github.com/dotnet/dotnet) |
| Plotly.js cartesian bundle 2.35.3 (`viewer-charts-2.35.3.min.js`) | MIT | Copyright 2012-2024, Plotly, Inc.; [source](https://github.com/plotly/plotly.js) |
| Inter 4.1 bundled TrueType faces | SIL Open Font License 1.1 | Complete license: `AnalysisITC.Avalonia/Assets/Fonts/Licenses/Inter-OFL.txt`; source and hashes: `AnalysisITC.Avalonia/Assets/Fonts/PROVENANCE.md` |
| Liberation Sans 2.1.5 bundled TrueType faces | SIL Open Font License 1.1 | Complete license: `AnalysisITC.Avalonia/Assets/Fonts/Licenses/LiberationSans-OFL.txt`; source and hashes: `AnalysisITC.Avalonia/Assets/Fonts/PROVENANCE.md` |

The Plotly bundle refers to its generated `plotly-cartesian.min.js.LICENSE.txt`
file.  The generated notice must accompany every distribution containing the
bundle; the bundle's MIT header alone is not a substitute for that notice.

The complete [LGPL-2.1 text](https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html)
must accompany distributions containing Accord.  LGPL obligations concerning
license notices, corresponding source, and relinking apply to the actual
distributed Accord binaries and linking method.  The complete
[Apache-2.0 text](https://www.apache.org/licenses/LICENSE-2.0) applies to the
SQLitePCLRaw components.  The complete OFL texts are the tracked files listed
in the font rows above.

Acknowledgements
----------------

Many thanks to the authors and maintainers of the open‑source projects above.
Without their contributions, this application would not have been possible.

The legacy Origin project reader was implemented as managed C# code from the
publicly documented CPYA block format. The MIT-licensed OpenOPJ project by
Juliusz Gonera was consulted as a format reference:

* **Copyright:** Copyright (c) 2012 Juliusz Gonera, Minor Laboratory,
  University of Virginia
* **Source:** https://github.com/jgonera/openopj
* **Licence:** MIT License (the complete terms appear above)

No GPL liborigin code is linked or redistributed.
