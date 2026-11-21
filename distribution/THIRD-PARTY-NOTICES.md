# Third-party components

HnMemo's project-specific code and documentation are MIT licensed.
Copyright (c) 2026 hinoerusi. See [LICENSE](LICENSE).
The following components retain their own copyright and license notices.

| Component | Version | License text in this package | Upstream |
|---|---|---|---|
| OpenVR (official C# binding and Windows x64 native library) | 2.15.6 | [BSD-3-Clause](licenses/OpenVR.txt) | https://github.com/ValveSoftware/openvr |
| Vortice.Direct3D11, Vortice.DXGI, Vortice.DirectX | 3.8.3 | [MIT](licenses/Vortice-LICENSE.txt) | https://github.com/amerkoleci/Vortice.Windows |
| Vortice.Mathematics | 2.1.0 | [MIT](licenses/Vortice.Mathematics-LICENSE.txt) | https://github.com/amerkoleci/Vortice.Mathematics |
| SharpGen.Runtime, SharpGen.Runtime.COM | 2.4.2-beta | [MIT](licenses/SharpGen-LICENSE.txt) | https://github.com/SharpGenTools/SharpGenTools |
| Fluent UI System Icons (pen, ink_stroke, eraser, arrow_undo, arrow_redo; 24 regular) | commit 83b26348c811f072429427ff48362238656524fe | [MIT](licenses/FluentUI-System-Icons.txt) | https://github.com/microsoft/fluentui-system-icons |
| Public Icons Reload (David Merfield) | Reload | [CC0 1.0 Universal](https://github.com/davidmerfield/Public-Icons/blob/master/LICENSE) | https://publicicons.lllllllllllllllll.com/reload-icon |

OpenVR's binding and native library are unmodified. Fluent icon geometry is converted for drawing;
the ink_stroke icon has a project-drawn circle and cross overlay for whole-stroke erase.
Public Icons Reload is drawn from the original SVG geometry.
The bundled libraries have their full license texts included in licenses/.
Self-contained releases also include Microsoft .NET and Windows Desktop Runtime.
Their license texts are in `licenses/NETRuntime-LICENSE.txt` and `licenses/WindowsDesktop-LICENSE.txt`;
additional runtime notices are in `licenses/NETRuntime-NOTICES.txt`.
Upstream: https://github.com/dotnet/runtime and https://github.com/dotnet/windowsdesktop.
