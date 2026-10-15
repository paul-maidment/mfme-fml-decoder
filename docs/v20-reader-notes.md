# Notes from MFME v20's own component reader

These notes come from Fruitulator's `.fml` reader, which started as a port of
this decoder and has since been checked against MFME v20's own loading code
(a Ghidra decompile of the unpacked v20 executable; function names below are
its addresses). They cover three things:

1. two silent misreads in v20.1 files that this decoder has today;
2. what saves older than v20.1 write differently, for the multi-version work;
3. small points that are questions rather than fixes.

Numbers come from running this decoder and Fruitulator's over a public
archive of v20.1 layouts (6,823 files; 6,390 report v20.1, the rest older
versions, which this decoder refuses as designed) and its pre-conversion
originals. No layout is named here.

## The base section, as v20 reads it

Every component's `LoadFromStream` first calls one base reader,
`FUN_0048fe68` (41 callers). It loops `read tag byte; switch` until a `0x00`.
Then the type's own loader runs until the next `0x00`. The per-type loaders sit
in a `.data` table at `0xa86df0 + 4 * type` (for example `0x04` Lamp
`FUN_004a467c`, `0x06` DiscReel `FUN_0049d680`, `0x03` Reel `FUN_004a89fc`).

Widths in the base reader, by tag:

| width | tags |
|---|---|
| 1 byte | `07 09 0A 0B 0C 15 17 19 29 2A 3C 3E 40 4B 4C` |
| 4 bytes | `06 08 16 18 1A 1C 1D-26 2B 2C-35 37 3B 48 49 4A` |
| count | `38` sublamp count (4 bytes); `39`, `3A` are count x 4 bytes each |
| text | `28` = u32 byte count + ANSI; `3F` = u32 char count + UTF-16 |
| other | `27` font (`FUN_0048fb94`); `36` a TBitmap (`FUN_0065abdc`) |

A tag the base reader has no case for is read and ignored. Two consequences
matter for this decoder:

* **`0x08` (the angle) and `0x06` are written only when non-zero.** A
  component at angle 0 has no `08` at all: after `07 01` the next byte is the
  next base tag.
* **The "nested tag block" `4C ?? 00` is base tag `0x4C` (one byte) followed by
  the base section's own `0x00` terminator.** What follows is the type's own
  section. v20.1 writes `0x4C` (the orientation flag Fruitulator reads as the
  axis); older writers do not, and end the base section with a bare `0x00`.

## 1. Three silent misreads in v20.1 files

All three decode without an error, so nothing flags them. All three come from
`GeometryAngleNormalization`, which expects five fixed fields, then `07` at a
fixed position, then a six-byte angle wire. When the angle is 0 it is not
written, so the normaliser inserts a zero angle and skips a fixed number of
bytes per type (`RewriteTriggerOffsetDelta`). The fixed skip is right for the
base tags that type usually carries and wrong when it carries others.

**Disc reels whose header carries `0A`.** Raw header after the geometry:

```
07 01  0A 00  0B 00  1C 13 00 00 00  38 ...
```

Normalised wire `01 0A 00 0B 00 1C`; with DiscReel's delta of 2 the parse
resumes at `0B` and reads it as the own-section tag `0x0B` (OuterL, 4 bytes),
which misaligns everything after. The disc never reaches its own section and
reports defaults: on four of them 12 stops on 96 half-steps where the layout
states 20 on 400 (the `.gam` reel line agrees with 400), and on all of them
the outer and inner lamp geometry and the sublamp table. 7 of the 96 disc
reels in the archive; the other 89 agree with Fruitulator exactly.

**AlphaNew with an overlay bitmap straight after a zero angle.** Raw:

```
07 01  36 <TBitmap: 42 4D 36 5D 00 00 ...>
```

Wire `01 36 42 4D 36 5D`; with AlphaNew's delta of 5 the parse resumes five
bytes into the bitmap and either meets a `0x00` there and stops, or reads
bitmap bytes as tags. Different properties come out wrong each time (Charset,
Reversed, the spacings, Slant, Thickness, Segment16, the colours).
82 AlphaNew components in the archive, every one of them in this shape.

**Base `0x06` before `07`.** v20 writes `06 <u32>` between the number and
`07` when that field is non-zero:

```
05 2F 00 00 00  06 00 00 00 54  07 01 ...
```

`GeometryParser` skips `06`, but the normaliser looks for `07` at a fixed
position, finds `06`, and does not normalise; the type's own parse then starts
in the wrong place. On the one Button in the archive that carries it, the
button number, shortcut and colours are lost.

The general fix is to read the base section as v20 does (the table above)
rather than to normalise the angle and skip: each of these cases is then just
another base tag. Fruitulator's `BASE_TAGS` map in `src/layout/fmlparse.ts`
is that switch, width for width.

## 2. What older saves write differently

Each layout in the archive has an exact v20.1 re-save, so each rule below was
checked by decoding the original and its twin and requiring equal values.
After these rules, every component type listed reads clean in all 6,797
originals.

| type | v20 own loader | older saves | rule |
|---|---|---|---|
| all non-separated types (AceMatrix, ProconnMatrix, BFMAlpha, BFMVideo, Led, ...) | | base section ends with a bare `0x00`, no `4C xx` | the first top-level `0x00` ends the base section; continue into the own map when an own tag (or an empty section) follows |
| Led | `FUN_004adc84` (own `01`-`04` u32, `05`-`07` byte) | bare `00`, then `02`/`04`... | the own map is the whole own section |
| BFMAlpha | `TGRBFMAlpha::LoadFromStream` `0x4931cc` | `06 01` in the own section | own `0x06` = Reversed (byte) |
| BFMVideo | `FUN_00494b12`; base case `0x0B` | `07 01 3B ...` where v20.1 writes `07 01 0B 00 3B ...` | base `0x0B` is one byte; a fixed angle skip breaks this type. Own `0x02` is u32 |
| Reel | `FUN_004a89fc` | `12 01`; `10 <bitmap>`; `18 00` | own `0x12` = axis (v20.1 moves it to base `4C 01`); own `0x10` = overlay (v20.1 moves it to base `36`); own `0x14 0x18 0x1B 0x1D 0x22 0x23` sized per the loader |
| BandReel | `FUN_00486e7c`; ctor `FUN_00484dd0` | `0A` axis, `0B` Reversed, `0F` InvertedOpto, `08` overlay, `11 12 14 15`; a 20-slot sublamp table | when the type is `0x08` and the sublamp count is 20 (`0x14`), v20 widens the table to 60, slot `i` -> `i/5 + i` |
| DiscReel | `FUN_0049d680` | `04 01` | own `0x04` = InvertedOpto (v20.1 writes base `3C 01`) |
| BFMColourLed | `FUN_00493bc6` | `02 <u32>` | own `0x02` is four bytes read into a stack local and dropped; v20 keeps nothing of it, but it must be sized |

Also seen in both versions:

* **ReelBonusReel own `0x01`**, four bytes into `+0x17c`, constructor default
  `0x60`. v20's properties tab has one unclaimed edit, `RBNumber` ("Number");
  the pairing is inferred from the tab, not traced.
* **Captions:** base `0x28` is ANSI with a byte count, `0x3F` UTF-16 with a
  character count; the Lamp's own `0x1B 0x1C 0x1F 0x20` are ANSI and
  `0x26`-`0x29`, `0x52`-`0x55` UTF-16. Choose the encoding by tag, never by
  sniffing: emoji captions (surrogate pairs) and `£` in ANSI captions both
  occur.

## 3. Questions rather than fixes

* **Reel `WidthDiff` is doubled** after parsing (`widthDiff * 2` in
  `ReelParser`). Fruitulator reads the stored value (v20 case `0x25`, an
  int32), so the two disagree by a factor of 2 on every reel that sets it. If
  the doubling matches the editor, nothing to do.
* **AlphaNew `0x99` Chop** is never written. In the hexdiff repo's isolation
  saves, Chop 45, 90, 91 and 95 leave the AlphaNew record byte-identical. The
  map entry is harmless.
* **BFMLed defaults** in v20's constructor (`FUN_00493d4c`): XSize, YSize,
  DigitSpacing and LedSize 4; On `0xFFFF0000`; Off and Back `0xFF000000`. They
  matter only when a tag is absent.
