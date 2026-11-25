# Third-party components

## OpenVR v2.15.6

- Upstream: https://github.com/ValveSoftware/openvr
- Pinned commit: `0924064316de3effbcd1acf1e309182a2deb1c05`
- Unmodified files: `vendor/openvr/openvr_api.cs`, `vendor/openvr/openvr_api.dll` (Windows x64), `vendor/openvr/LICENSE`.
- BSD-3-Clause license; full text in `vendor/openvr/LICENSE`, copied to the app output as `licenses/OpenVR.txt`.
- SHA-256 C# binding: `C17E878B7B3B925D1F22EF5382561389C47DB8B92019DE840705FF5FF28C317A`.
- SHA-256 DLL: `BAB8AC6EF64E68A9CA53315B0014D131088584B2EFDFA6DB511D67EC03CFCB4A`.
- Our `OpenVR.csproj` only builds the unchanged official binding; no Unity dependency.

## Vortice.Direct3D11 3.8.3

- Upstream: https://github.com/amerkoleci/Vortice.Windows
- NuGet: https://www.nuget.org/packages/Vortice.Direct3D11/3.8.3
- MIT license. Full upstream notice in `vendor/Vortice-LICENSE.txt`.
- Used as a dependency for D3D11/DXGI interop; no upstream sample code copied.
- Exact transitive packages and content hashes are recorded in `src/HandwriteMemo.App/packages.lock.json`.
- The project-specific code handles bitmap upload, overlay lifetime and touch interaction.

## Transitive runtime libraries

- Vortice.DXGI and Vortice.DirectX 3.8.3 share the Vortice.Windows MIT notice above (commit `9e609cb9439c9872aa1b339f177e40ec96f77239`).
- Vortice.Mathematics 2.1.0: MIT, https://github.com/amerkoleci/Vortice.Mathematics (commit `fa05ec6dcba48f3f7331791da6dc7f3d866b2ad6`); full notice in `vendor/Vortice.Mathematics-LICENSE.txt`.
- SharpGen.Runtime and SharpGen.Runtime.COM 2.4.2-beta: MIT, https://github.com/SharpGenTools/SharpGenTools (commit `6990bcafe124a4c22515ad19cee5a081da8db67b`); full notice in `vendor/SharpGen-LICENSE.txt`.
- These are the dependencies selected by Vortice's package; no separate runtime source code was copied. Full notices are copied to `licenses/` next to the built app.

## Fluent UI System Icons

- Upstream and usage: https://github.com/microsoft/fluentui-system-icons (plain SVG assets).
- Pinned commit: `83b26348c811f072429427ff48362238656524fe`.
- Unmodified source SVGs and license: `vendor/fluentui-system-icons/`; see `SOURCE.md` there.
- Used icons (24 regular): `pen`, `ink_stroke`, `eraser`, `arrow_undo`, `arrow_redo`.
- Project modification: `src/HandwriteMemo.App/Ui/ToolbarGlyph.cs` overlays the lower-right of `ink_stroke` with a tile-background circle and a foreground cross to identify whole-stroke erase. The circle and cross are project-drawn geometry, not Fluent assets. Their colors follow the tile's normal, selected, hover, pressed and disabled states. `eraser` identifies partial erase. Eraser algorithms and size settings are unchanged.
- `scripts/generate-fluent-icons.py` converts the exact source SVG geometry to GDI+ `GraphicsPath` data in `src/HandwriteMemo.App/Ui/FluentToolbarIcons.Generated.cs`; the cross overlay is added at paint time. All geometry is drawn at the current DPI scale; no new package dependency.
- MIT license; full text below and in `vendor/fluentui-system-icons/LICENSE`, copied to the app output and publish directory as `licenses/FluentUI-System-Icons.txt`.

```text
MIT License

Copyright (c) 2020 Microsoft Corporation

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
```

## Public Icons Reload

- [Reload](https://publicicons.lllllllllllllllll.com/reload-icon) by David Merfield. The [upstream license](https://github.com/davidmerfield/Public-Icons/blob/master/LICENSE) is CC0 1.0 Universal.
- The original SVG geometry is in `vendor/public-icons/reload.svg`. `ToolbarGlyph.cs` draws the filled curve and triangle as GDI+ vector geometry, using the current button ink and a DPI-scaled size.

The project-specific code and documentation are licensed under the MIT License, Copyright (c) 2026 hinoerusi. See LICENSE. The components above retain their respective licenses.

## .NET runtime in self-contained packages

Self-contained distribution packages include Microsoft .NET and Windows Desktop Runtime.
The packaging script copies their original LICENSE and runtime THIRD-PARTY-NOTICES from the default NuGet runtime packs into `licenses/`.
Upstream: https://github.com/dotnet/runtime and https://github.com/dotnet/windowsdesktop.
Runtime source is not vendored in this repository. Framework-dependent builds use the installed runtime.
