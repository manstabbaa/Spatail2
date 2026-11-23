# Third-party libraries in this folder

These run the programs Spatail's planner writes. They are unmodified builds from NuGet (the `netstandard2.1`
assets, except where noted).

| File | Package | Version | Licence |
|---|---|---|---|
| `Jint.dll` | [Jint](https://github.com/sebastienros/jint), a JavaScript interpreter for .NET | 4.18.1 | BSD-2-Clause |
| `Acornima.dll` | [Acornima](https://github.com/adams85/acornima), the parser Jint uses | 1.9.1 | BSD-3-Clause |
| `System.Runtime.CompilerServices.Unsafe.dll` | Microsoft, `netstandard2.0` asset | 6.0.0 | MIT |

If Unity reports that `System.Runtime.CompilerServices.Unsafe` is defined twice, another package in the project
already ships it: delete the copy here.
