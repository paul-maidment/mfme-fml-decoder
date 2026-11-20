# ADR 0001: the four-member export IR and the two reconstruction gates

Status: Accepted
Date: 2026-11-20

## Context

The decoder's `--export` zip was an inspection artefact: `layout.json` plus images, and
(when a sibling `.gam` existed) `machine.json`/`game.json`. The zip-reconstruction plan
makes that zip the only input for rebuilding a playable FML+GAM pair. Two things must be
fixed so the decoder and the reconstructor can be developed independently:

1. What exactly the zip contains, and what it may never contain.
2. How "the rebuild is correct" is judged.

## Decision

### 1. The export zip is a frozen four-member IR

Reconstruction consumes exactly `layout.json`, `images/`, `machine.json`, `game.json`.
All four are required. The zip schema is frozen: no new members, no raw byte blobs.
Missing coverage is closed by modelling (decoder/maps), never by carrying original
bytes. Raw-tag plumbing (`RawTagValues`, `NestedRawTagValues`, `GeometryAngleFlag`)
stays `[JsonIgnore]` and is intentionally absent from the IR.

`machine.json` is the FML-side half (prologue tags via the system map); `game.json` is
the GAM-side half. Output is a name-aligned `<name>.fml` + `<name>.gam`, base name from
the zip stem.

### 2. Two acceptance gates

- **Gate A (round-trip).** `decode(reconstruct(exportZip))` must equal the zip's
  `layout.json`, `machine.json`, and `game.json`. Run over a 20.1 corpus, because the
  writer normalises layout representation to 20.1 (a legacy source decodes to a different
  shape than the rebuild, which is a representation change, not a defect). Cheap and
  primary, but self-referential; Gate B covers the render half.
- **Gate B (render parity).** The rebuilt FML must render 0.000% against the original
  layout via `mfme_pw.ps1` + `diff_png.ps1` (`-Threshold 16 -IgnoreTop 48`), excluding the
  21 known load-fail-both layouts.

Byte-conformance with MFME's own writer is out of scope; so are editing/authoring and
classic `.dat` input.

## Consequences

- The layout importer must invert `layout.json` using only the tag-map roles, including
  derived values that the decoder folds into typed dictionaries (Border box/pane, Reel
  `WidthDiff`, RGBLed channel numbers) and the flattened/nested forms of `SubLampNumberTable`
  and BandReel `Masks`.
- The prologue synthesizer must supply every tag MFME needs to load; the minimal set is
  determined by an MFME probe, not assumed. Gate B depends on that probe, because MFME is
  stricter than our decoder about a sparse prologue.
- Gate A on the whole corpus is expected to be partial where the JSON lost raw-backed or
  derived state; those gaps are documented and must be resolved by modelling or accepted
  via Gate B, not by widening the IR.
