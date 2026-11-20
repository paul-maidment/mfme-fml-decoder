# Context: mfme-fml-decoder

Shared vocabulary for the decoder and the zip-reconstruction path. Read this before
touching `src/Utilities/LayoutImporter.cs`, `src/Decoder/Writer/`, or
`tools/RoundTripHarness`.

## Layouts and containers

- **FML** — an MFME layout file. `.fml` is an AACS container (compressed, encrypted
  chunks); `.dat` is the plaintext/TLV variant. Both decrypt to a tagged stream.
- **Prologue** — the file-level TLV block at the start of the plaintext, terminated by
  tag `0xFFFFFFFF`. Holds layout-wide settings (`0x0C`/`0x2C` description, `0x2F`
  version, device/emulation option tags, splash bitmap `0x98`).
- **Component stream** — everything after the prologue: `u32 kind, u32 size, payload`
  records for Background, Reel, Lamp, SevenSeg, … (see `MFMEComponentType`).
- **AACS / ecl-CRC32** — the container crypto and checksum (`FmlDecryptor`). The
  checksum is reflected CRC-32 with init 0 and no final inversion, not `zlib.crc32`.

## Export zip (the four-member IR)

`MfmeFmlDecoder --export` writes a zip with exactly four members. Reconstruction treats
this as a frozen, self-describing intermediate representation:

- `layout.json` — the component tree (`Components[]`: `Type`, `Geometry`, `Values`,
  optional `Fonts`, `SubLampNumberTable`, `Images` metadata, `Masks`, `Owned…`).
- `images/` — `images/<key>.bmp` for layout-level images (e.g. `splash_bitmap`) and
  `images/<ordinal>/<key>.bmp` for per-component images.
- `machine.json` — FML-side settings decoded through a system map (`System` + `Settings`).
- `game.json` — GAM-side settings decoded through the same map (`System` + `Settings`,
  each control carrying `kind`/`value`/`raw`/`defaultUsed`).

No external `.gam` is consulted. There is no raw-tag channel: `[JsonIgnore]` plumbing
(`RawTagValues`, `NestedRawTagValues`, `GeometryAngleFlag`) is deliberately absent.

## GameConfig maps

`src/GameConfig/Maps/*GameConfigMap.cs` are embedded JSON maps, one per GAM `System`
name. A control names its `storage` (`fml`, `gam`, `volatile`), `kind`, `tag`, `gamLine`,
and encodings. The decoder (`GameConfigDecoder`) is map-driven; the reconstruction path
inverts the same maps.

## Reconstruction

- **Layout importer** (`LayoutImporter`) — `layout.json` + `images/` → `Layout` model.
  Routes the flattened `Values` object back to the typed dictionaries
  (`Strings`/`UInt32s`/`Int32s`/`UInt16s`/`Booleans`/`Bytes`/`Floats`/`Colours`) using the
  20.1 tag-map role.
- **Prologue synthesizer** (`PrologueSynthesizer`) — `machine.json` + map + constants →
  prologue TLV (clean `0x2F` "20.1", map-derived tags, splash `0x98`).
- **GAM synthesizer** (`GamSynthesizer`) — `game.json` + map → `.gam` text.
- **Reconstructor** (`Reconstructor`) — importer → writer (`emitAllModelled`) → AACS
  encode → `.fml`, plus GAM synth → `.gam`. `--rebuild=<zip> [--outdir=<dir>]`, base name
  from the zip stem.
- **`emitAllModelled`** — writer mode where tag presence comes from the model (a zip-only
  model has no raw bytes), rather than raw-tag presence (`--fmlout`) or value-vs-default.

## Acceptance gates

- **Gate A (round-trip)** — `decode(reconstruct(zip))` reproduces the zip's
  `layout.json`/`machine.json`/`game.json`. Cheap, primary. Self-referential (our decoder
  reads our writer), mitigated by Gate B for the render half. Run on **20.1** sources: the
  writer normalises representation to 20.1, so a legacy source decodes to a different
  shape than the rebuild.
- **Gate B (render parity)** — the rebuilt FML renders 0.000% vs the original legacy layout
  via `mfme_pw.ps1` + `diff_png.ps1` (`-Threshold 16 -IgnoreTop 48`). The independent
  visual gate; excludes the 21 known load-fail-both layouts.

## Tooling

- `tools/CoverageCensus` — read-only corpus census of prologue tags/GAM keys vs map
  coverage; `--dump`, `--dumpimport`, `--dumpcomp` helpers.
- `tools/RoundTripHarness` — whole-corpus Gate A.
- `mfme_pw.ps1` / `diff_png.ps1` in `%TEMP%\opencode` — Gate B render + diff.
