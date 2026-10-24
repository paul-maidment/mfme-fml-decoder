# MFME FML Decoder

A cross-platform command-line tool that decodes **MFME** (Multi Fruit Machine Emulator)
layout files into JSON and, optionally, exports a layout plus all of its images to a zip.

MFME layouts are stored as either plain `.dat` files or encrypted `.fml` files. Both
contain a TLV (type-length-value) stream of components: lamps, reels, alpha displays,
dot matrices, 7-segment displays, LEDs, bitmaps, and many more. This tool walks that
stream and turns it into structured, self-describing JSON.

## Features

- Decodes `.dat` and encrypted `.fml` layout files.
- Emits the full component tree as JSON (`--json`).
- Exports `layout.json` plus every layout and per-component image to a zip (`--export`).
- When a sibling `.gam` file is present, also decodes the machine/game configuration and
  adds `machine.json` and `game.json` to the export.
- Reports the embedded MFME version (`--mfme-version`).
- Supports parsing a layout embedded at an offset inside a larger file.
- Ships as a self-contained **Native AOT** binary (no .NET runtime required to run it).

## Requirements

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) or later.
- A C++ toolchain (Visual Studio 2022 with "Desktop development with C++", or `clang`)
  only if you want to produce Native AOT binaries. See the
  [Native AOT prerequisites](https://aka.ms/nativeaot-prerequisites).

## Build and run

```pwsh
# Run straight from source
dotnet run -- <layout.dat|layout.fml> --json > layout.json

# Or build a normal (framework-dependent) binary
dotnet build -c Release
.\bin\Release\net8.0\MfmeFmlDecoder.exe <layout.dat|layout.fml> --json
```

Native AOT builds go to `dist/aot/<rid>/`:

```pwsh
.\scripts\build-aot.ps1                 # all RIDs available on this host
.\scripts\build-aot.ps1 -Target win-x64
.\scripts\build-aot.ps1 -Target win-x64,linux-x64 -Clean
```

Pre-built binaries for Windows, Linux, and macOS are attached to each
[GitHub release](../../releases).

## Usage

```
MfmeFmlDecoder <filename.(dat|fml)> [offset] [--json] [--export] [--mfme-version]
```

| Option | Description |
| --- | --- |
| `--json`, `-j` | Emit the decoded layout as JSON on **stdout only**; errors go to stderr. |
| `--export[=path]` | Write `layout.json` and component images to a zip. Default output is `<input>.zip` next to the input file. |
| `--mfme-version` | Print the MFME version from the layout (TLV tag `0x2F`) on stdout. |

`offset` selects where the layout starts inside the file. It accepts decimal (`4096`) or
hex (`0x1000`) and defaults to `0`.

### Examples

```pwsh
MfmeFmlDecoder file.dat --json > layout.json
MfmeFmlDecoder file.dat 0x1000 --json
MfmeFmlDecoder file.fml --export
MfmeFmlDecoder file.fml --export=C:\out\layout.zip
MfmeFmlDecoder file.fml --mfme-version
```

### Export contents

```
layout.json          # decoded component tree
images/              # layout-level bitmaps
images/<index>/      # per-component images (index = component ordinal)
machine.json         # FML tags (only if a sibling .gam is found)
game.json            # GAM settings (only if a sibling .gam is found)
```

## Notes

Parsing is strict: encountering an unregistered component ID aborts the decode with a
detailed error (file offset, component ID, payload length, and a hex preview) and exit
code `1`. To support a new component type, register it in `MFMEComponentType` and add a
parser in `ComponentParser`.

## Repository layout

| Path | Purpose |
| --- | --- |
| `src/Application/` | CLI entry point and argument parsing |
| `src/Decoder/` | TLV file walker and per-component parsers |
| `src/Decryption/` | `.fml` decryption |
| `src/GameConfig/` | `.gam` machine/game configuration decoding and control maps |
| `src/Model/` | Layout, component, and header models |
| `src/Utilities/` | Export, BMP, hex, and JSON helpers |
| `test/` | Unit tests, smoke tests, and golden fixtures |
| `tools/` | `FmlTagEdit`, `GenGameConfigMaps` helper projects |
| `scripts/` | Native AOT build scripts |
| `docs/` | Generated reports |

## Tests

```pwsh
dotnet test
```

## License

Licensed under the [GNU Affero General Public License v3.0 or later](LICENSE) (AGPL-3.0-or-later).
