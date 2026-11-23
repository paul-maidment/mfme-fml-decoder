# MFME `.fml` component tags named "Unknown 0xNN"

Research notes, 20.1 profiles. For every tag the decoder calls `"Unknown 0xNN"`,
this records what MFME actually does with it: the object offset it lands in, the
reader/writer that touches it, and (where evidence exists) what it controls. No
application source was changed.

- Target: `MFME_unpacked.exe` (`C:\Users\paulm\MFME-RE\`), whole-program decompile
  under `C:\Users\paulm\MFME-RE\decomp\code_*.c`.
- Profiles: `src/Decoder/Component/Profiles/V20_1/*.cs`.
- Cross-checked against the existing corpus notes under `C:\Users\paulm\MFME-RE\fml\`
  (`fields/BASE.md`, `fields/0xNN_*.md`, `FML_TAG_NAMES.md`) and
  `C:\Users\paulm\MFME-RE\evidence\gaps\element_binding.md`.

Line numbers below are from the decompilation variant that ships at
`C:\Users\paulm\MFME-RE\decomp\code_080000.c` (10664 lines). `fml/fields/BASE.md`
cites slightly different line numbers because it was built from a sibling variant
(`decomp/_baseline_default/`); the code is the same, only offsets into the file
differ. RVAs are stable and are the durable citation.

## How to read a tag

MFME reads one component record in three passes (`fml/ELEMENT_FIELDS.md`):

| pass | reader | offset base | terminator |
|---|---|---|---|
| pre-list | `FUN_0048fd60` (RVA `0x08FD60`) | none | tag `5` |
| base list | `FUN_0048fe68` (RVA `0x08FE68`) | `obj` | tag `0x00` |
| class list | the class's own `LoadFromStream` | `obj` | tag `0x00` |

A tag id can appear in the base list and the class list with different meanings.
In the 20.1 profiles:

- **base** = the shared 54-tag map (`FUN_0048fe68`), duplicated per component as
  overrides. These are the entries the profile lists *before*
  `.WithNestedTagBlock(...)` for the separated types (Background, Border, Lamp,
  Reel, SevenSeg, SevenSegBlock), and the mixed base/class entries for the flat
  `SingleStream` types (Led, Bitmap, Button, DiscReel, ...).
- **class** = the component's own loader. For the separated types it is the
  `.WithNestedTagBlock` map, opened on the wire by `4C ?? 00`.

`obj+N` are byte offsets into the element object. `param_1[N]` in the decompile is
`obj + 4*N`. The base save is `FUN_0048f2c4` (RVA `0x08F2C4`), the memberwise
`Assign` is `FUN_0048de94` (RVA `0x08DE94`, `code_080000.c:8507`).

The whole base loader is `code_080000.c:10400-10663`; read alongside this report.

---

## 1. Base-section Unknown tags

These are read by the shared base reader, so the same tag means the same thing on
every component that carries it.

| tag | role / len | object field | what it is | controls | conf |
|---|---|---|---|---|---|
| `0x3B` | u32 / 4 | `obj+0x48` | unknown serialized u32 | nothing located | low |
| `0x08` | u32 / 4 | stack local (not stored) | geometry parameter (MFME notes: angle) | passed to bounds virtual `vtable+0x0C` | medium |
| `0x06` | u32 / 4 | `obj+0xA4` | unknown u32, saved when non-zero | nothing located | low |
| `0x09` | byte / 1 | `obj+0x99` | flag, saved only when `0` | per-element flag | low |
| `0x0A` | byte / 1 | `obj+0x9A` | flag, saved only when `0` | per-element flag | low |
| `0x0B` | byte / 1 | `obj+0x38` | flag, saved only when `0` | per-element flag | low |
| `0x17` | byte / 1 | `obj+0xEC` | enable flag for the paired input | gates tag `0x18` | medium |
| `0x19` | byte / 1 | `obj+0xED` | Coin / Note Selected gate | gates tag `0x49` | high |
| `0x1C` | u32 / 4 | `obj+0xF8` | unknown u32, saved when non-zero | nothing located | low |
| `0x30` | u32 / 4 | `obj[0xc]+0x24` | sub-lamp map slot 9 (index array) | sub-lamp binding | medium |
| `0x33` | u32 / 4 | `obj[0xb]+0x1C` | sub-lamp map slot 7 (channel array) | sub-lamp binding | medium |
| `0x34` | u32 / 4 | `obj[0xb]+0x20` | sub-lamp map slot 8 (channel array) | sub-lamp binding | medium |
| `0x37` | u32 / 4 | `obj+0x108` | saved when non-zero; aliases Border own `0x08` | Border spacing/index | medium |
| `0x38` | count / 4 | local count | sub-lamp count (drives `0x39`/`0x3A`) | sub-lamp table size | high |
| `0x39` | count×4 | `obj[0xc]` array | sub-lamp index/channel table | sub-lamp binding | high |
| `0x3E` | byte / 1 | `obj+0x9E` | flag; class-dependent default | Lamp shape class, DiscReel flag | low |

### 0x3B (object+0x48) — the most common Unknown

Appears as `"Unknown 0x3B"` in ~35 of 37 V20_1 profiles, including Label, Frame,
Led, Checkbox, all BFM/Matrix/video displays.

- Load: `case 0x3b: ReadBuffer(param_2, param_1 + 0x12, 4)` (`code_080000.c:10574-10575`).
  `param_1 + 0x12` (int*) = `obj + 0x48`.
- Save: unconditional `FUN_0048f7ec(param_2, 0x3b, param_1[0x12])`
  (`code_080000.c:9902`). It is the one base field the save always writes.
- Ctor default 0 (`code_080000.c:4605`, `param_1[0x12] = 0`).
- **Finding:** the base memberwise `Assign` `FUN_0048de94` copies every other base
  field (see the block at `code_080000.c:8533-8561`) but **not** `obj+0x48`. A
  field that is serialized but neither copied nor read is most likely a
  per-element administrative value (an id/flag MFME round-trips but does not act
  on at draw time). No consumer of an element's `obj+0x48` was found in the base
  class in `code_*.c`. **Role unresolved.**

### 0x08 (read to a stack local, applied to the bounds virtual)

Appears as `"Unknown 0x08"` in Alpha, Lamp, Reel, MatrixAlpha, Bitmap, SevenSeg.

- Load: `case 8: ReadBuffer(param_2, &local_40, 4)` (`code_080000.c:10432-10434`).
  The value is not stored on the object.
- After the base loop (`code_080000.c:10657-10660`):
  `if (local_40 != 0) (*param_1)[0xC](param_1, param_1[0x2a], param_1[0x2b], local_40, param_1[0x30], param_1[0x31]);`
  i.e. it is handed to the element's geometry virtual `vtable+0x0C` alongside
  `obj+0xA8` (x), `obj+0xAC` (y), `obj+0xC0` (height), `obj+0xC4` (width).
- `v20-reader-notes.md` (Fruitulator port) labels base `0x08` the element **angle**,
  written only when non-zero.
- **Hypothesis:** an extra rotation/angle parameter applied through the shared
  bounds setter. Only reached when non-zero, so absent means 0.
- **Caveat:** the Lamp and SevenSeg profiles default it to `0x32` (50), all others
  to 0. MFME writes the tag only when non-zero and the loader defaults it to 0, so
  a profile default of 50 is likely wrong (or reflects an editor default not
  mirrored by the loader). Treat as **medium** confidence on meaning, and note the
  default discrepancy.

### 0x06 (object+0xA4)

Only DiscReel uses it (`DiscReelProfileV20_1.cs:20`).

- Load: `case 6: ReadBuffer(param_2, param_1 + 0x29, 4)` (`code_080000.c:10426-10427`),
  `param_1[0x29]` = `obj+0xA4` (4 bytes).
- Save: `if (param_1[0x35] != 0) FUN_0048f7ec(param_2, 8, ...)` is tag 8; tag 6 is
  saved from `obj+0xA4` in the base save path.
- **Hypothesis:** a plain unknown u32 on the base element. Not stored by DiscReel's
  own loader under that id (its class `0x06` lands at `obj+0xCC4`, `fields/0x06_TGRDiscReel.md:18`),
  so if the decoder sees `0x06` in the flat stream it could be either field. **Low.**

### 0x09 / 0x0A / 0x0B (object+0x99 / +0x9A / +0x38)

- Load: `case 9 -> +0x99`, `case 10 -> +0x9A`, `case 0xb -> param_1+0xe` (=`obj+0x38`)
  (`code_080000.c:10435-10443`).
- Save emits each of `9`, `10`, `0xb` **only when the value is 0**
  (`code_080000.c:9846-9857`), so a non-zero value is the "set" state.
- `Assign` copies all three (`code_080000.c:8536-8537`).
- **Finding:** three per-element byte flags. Background maps `0x09/0x0A/0x0B` as
  Unknown; Border maps `0x0A/0x0B`. Roles are not carried by any located setter in
  the class code. **Low.** (obj+0x38 is shared with the base ctor's `param_1[0xe]`.)

### 0x17 (object+0xEC) — paired with 0x18

Lamp (`LampProfileV20_1.cs:23`) and Button (`ButtonProfileV20_1.cs:16`).

- Load: `case 0x17 -> param_1 + 0x3b` = `obj+0xEC` (1 byte) (`code_080000.c:10453-10454`).
- Save emits `0x17` **and** `0x18` together, but only when `obj+0xEC != 0`
  (`code_080000.c:9867-9871`):
  `if ((char)param_1[0x3b] != 0) { FUN_0048f850(param_2,0x17,...); FUN_0048f7ec(param_2,0x18,param_1[0x3a]); }`
- `Assign` copies `obj+0xEC` via the `vtable+0xA8` setter (`code_080000.c:8550-8551`).
- Setters `FUN_0048d69c` / `FUN_0048d6a4` write 0 / 1 to `obj+0xEC`
  (`code_080000.c:8086`, `8098`).
- **Hypothesis:** an "input present" flag gating the paired u32 at base `0x18`
  (`obj+0xE8`), which the profiles name `ButtonNumber` / `Button Number` (the
  element's machine input). So `0x17` enables/owns the input whose number is `0x18`.
  **Medium.**

### 0x19 (object+0xED) — Coin / Note Selected

Button calls it `"Unknown 0x19"` (`ButtonProfileV20_1.cs:51`); Lamp already names
the same base field `"Coin / Note Selected"` (`LampProfileV20_1.cs:41`).

- Load: `case 0x19 -> obj+0xED` (`code_080000.c:10459-10460`).
- Save: `if (obj+0xED != 0) { FUN_0048f850(param_2,0x19,...); FUN_0048f7ec(param_2,0x49,param_1[0x3d]); }`
  (`code_080000.c:9872-9875`) — saved as a pair with tag `0x49` (the coin/note id
  at `obj+0xF4`).
- The `0x1A` sub-switch sets `obj+0xED = 1` for the canonical coin-note codes
  `0x43/0x44/0x45/0x49/0x4A` (`code_080000.c:10465-10493`).
- **Conclusion:** `obj+0xED` is the "Coin / Note Selected" gate; `0x49` is the
  selected coin-note id. **High.** (Full value→entry table in `fields/COINNOTE.md`.)

### 0x1C (object+0xF8)

Led (`LedProfileV20_1.cs:16`), RGBLed (`RGBLedProfileV20_1.cs:16`).

- Load: `case 0x1c -> param_1 + 0x3e` = `obj+0xF8` (4 bytes) (`code_080000.c:10499-10500`).
- Save writes it only when non-zero (`code_080000.c:9876-9878`).
- `Assign` copies it (`code_080000.c:8558`).
- **Finding:** unknown per-element u32. No located consumer. **Low.**

### 0x30 / 0x33 / 0x34 (sub-lamp map array slots)

- Load (computed destination, `code_080000.c:10541-10553`):
  - `0x2c..0x30 -> obj[0xc] + 0x14 + (tag-0x2c)*4`, so `0x30 -> obj[0xc]+0x24`.
  - `0x31..0x35 -> obj[0xb] + 0x14 + (tag-0x31)*4`, so `0x33 -> obj[0xb]+0x1C`,
    `0x34 -> obj[0xb]+0x20`.
- `obj+0x30` (obj[0xc]) and `obj+0x2c` (obj[0xb]) are the two 10-entry sub-lamp
  arrays; `obj+0x34` (count) and `obj+0xD8` (channel key) are established in
  `evidence/gaps/element_binding.md` F7/F8 (the per-sub-lamp map is the u32 array at
  `element+0x30`; `FUN_0048ee5c` walks it and returns the i-th valid entry's index
  as the element bit and its value as the machine channel).
- **Conclusion:** `0x30/0x33/0x34` are the single-slot spelling of sub-lamp map
  entries, the same data the block tags `0x39`/`0x3A` write in bulk. They control
  which machine channel/bit a sub-lamp drives. **Medium.**
- **Decoder note:** `AceVideoProfileV20_1.cs:17` declares `0x34` as `UINT16` length
  `1`, but base `0x34` is a 4-byte dword. That entry disagrees with the base reader;
  flag as suspicious.

### 0x37 (object+0x108)

Border (`BorderProfileV20_1.cs:18`).

- Load: `case 0x37 -> param_1 + 0x42` = `obj+0x108` (4 bytes) (`code_080000.c:10562-10563`).
- Save writes it only when non-zero (`code_080000.c:9894-9896`).
- **Findings:** `obj+0x108` is also Border's own class tag `0x08` destination
  (`fields/0x1B_TGRBorder.md:22`, "Spacing"; save skips when ==1). So base `0x37`
  and Border own `0x08` alias the same dword. Meaning is a Border spacing/index
  value that MFME also carries on the generic base element. **Medium.**

### 0x38 / 0x39 (sub-lamp count / table)

Led (`LedProfileV20_1.cs:17-18`), Label (`LabelProfileV20_1.cs:23`, only `0x38`).

- Load: `case 0x38 -> &local_3c, 4` (count), `case 0x39 -> obj[0xc], local_3c<<2`
  (the table) (`code_080000.c:10565-10570`).
- These are the sub-lamp count and the index/channel table for that count, the same
  pair Lamp/SevenSeg/Reel map with the semantic roles `SUBLAMP_COUNT` /
  `LAMP_SUBLAMP_TABLE`. Led and Label leave them as plain `UINT32`.
- **Conclusion:** `0x38` = number of sub-lamps; `0x39` = the per-sub-lamp table.
  **High** on identity, and see `element_binding.md` F8.

### 0x3E (object+0x9E)

Lamp (`LampProfileV20_1.cs:37`), DiscReel (`DiscReelProfileV20_1.cs:47`).

- Load: `case 0x3e -> obj+0x9E` (1 byte) (`code_080000.c:10580-10581`).
- Save writes it only when non-zero (`code_080000.c:9907-9909`).
- `Assign` copies it (`code_080000.c:8560`).
- DiscReel's ctor sets `obj+0x9E = 1` (`code_090000.c:7239`, RVA `0x09B4BC` region).
- The Lamp shape setter `FUN_004a27e0` (`code_0a0000.c:1611`) sets `obj+0x9E = 1`
  for the selected-shape values `1,3,5,7,0x11`, and `0` otherwise; for shape `7`
  it also sets `obj+0x21C = 0x32` and `obj+0x208 = 10`.
- **Hypothesis:** a per-element flag driven by shape/type (Lamp: which shape is
  active; DiscReel: default on). Likely "shape is a line/segment (no filled
  body)". **Low.**

---

## 2. Class-section Unknown tags

These are read by the component's own loader (the `.WithNestedTagBlock` map for the
separated types, or the flat map for `SingleStream` types). Offsets are from the
per-class field maps in `C:\Users\paulm\MFME-RE\fml\fields\`.

| component | tag | role / len | object field | what it is | controls | conf |
|---|---|---|---|---|---|---|
| AceVideo | `0x01` | bool / 1 | `obj+0x154` | palette/colour-selector byte | video tint (`FUN_0047E290`) | medium |
| AceVideo | `0x34` | UINT16 / 1 | `obj[0xb]+0x20` | base sub-lamp slot 8 (see §1) | sub-lamp binding | low |
| AceVideo | `0xE9` | bool / 1 | ? | not in base or class map | ? | unresolved |
| AlphaNew | `0x04` | ARGB / 4 | `obj+0x164` | slice/frame count (int, default 16) | alpha strip slicing | medium |
| AlphaNew | `0x0D` | ARGB / 4 | `obj+0x1BC` | derived slice/char height | alpha strip geometry | medium |
| AlphaNew | `0x0E` | ARGB / 4 | `obj+0x1DC` | slice-height remainder | alpha strip geometry | medium |
| BarcrestBWBVideo | `0x01` | bool / 1 | `obj+0x15C` | colour/flag byte (loader discards, save persists) | video param | medium |
| Bitmap | `0x05` | u32 / 4 | `obj+0x15C` | unknown dword | unclear | low |
| Bitmap | `0x08` | u32 / 4 | discarded local | legacy read, not stored (class `0x08`) | none | low |
| Bitmap | `0x33` | bool / 1 | `obj[0xb]+0x1C` | base sub-lamp slot 7 (see §1) | sub-lamp binding | medium |
| Border | `0x04` (nested) | u32 / 4 | `obj+0x15C` | border flag/index, save skips when ==1 | border style | low |
| DiscReel | `0x06` | u32 / 4 | `obj+0xCC4` | unknown dword (class) | unclear | low |
| DiscReel | `0x1C` | u32 / 4 | `obj+0xF8` | base `0x1C` (see §1) | unclear | low |
| DiscReel | `0x3E` | bool / 1 | `obj+0x9E` | base `0x3E` (see §1) | shape/type flag | low |
| DotAlpha | `0x03` | u32 / 4 | `obj+0x170` | unclear numeric, default 31 | dot geometry | low |
| EpochDotAlpha | `0x03` | u32 / 4 | `obj+0x170` | unclear numeric, default 0 | dot geometry | low |
| EpochDotAlpha | `0x09` | u32 / 4 | `obj+0x17C` | unclear flag, default 0 | unclear | low |
| FlipReel | `0x09` | byte / 1 | `obj+0x27C` | byte/boolean flag, default 0 | unclear | low |
| Label | `0x38` | u32 / 4 | local count | base sub-lamp count (see §1) | sub-lamp table size | high |
| Lamp | `0x0E` (nested) | u32 / 4 | `obj+0x21C` | shape parameter | set to 0x32 for shape 7 | low |
| Lamp | `0x68` (nested) | TLVBLOCK | `obj` region | mapped as TLV, but MFME reads 24 raw bytes | unclear | low |
| Lamp | `0x5C..0x67` (nested) | RAW / 24 | `obj+0x264..` | 24-byte struct array (tech-specific) | unclear | low |
| Led | `0x1C` | u32 / 4 | `obj+0xF8` | base `0x1C` (see §1) | unclear | low |
| Led | `0x38` | u32 / 4 | local count | base sub-lamp count (see §1) | sub-lamp table size | high |
| Led | `0x39` | u32 / 4 | `obj[0xc]` | base sub-lamp table (see §1) | sub-lamp binding | high |
| MatrixAlpha | `0x03` | u32 / 4 | `obj+0x17E0` | unclear numeric, default 31 | matrix geometry | low |
| MatrixAlpha | `0x08` | u32 / 4 | stack local | base `0x08` (see §1) | geometry virtual | medium |
| MatrixAlpha | `0x0A` | bool / 1 | `obj+0x9A` | base `0x0A` (see §1) | per-element flag | low |
| RGBLed | `0x1C` | u32 / 4 | `obj+0xF8` | base `0x1C` (see §1) | unclear | low |
| SevenSeg | `0x08` | u32 / 4 | stack local | base `0x08` (see §1) | geometry virtual | medium |
| SevenSeg | `0x24` (nested) | byte / 1 | `obj+0x193` | Style2 (SetStyle2) | second style index | medium |

### AceVideo 0x01 (object+0x154)

Class `0x01`, 1 byte. Loader reads it into a discarded stack local
(`code_070000.c:8015`), but the SaveToStream writes `BYTE [obj+0x154]`
(`fields/0x21_TGRACEVideo.md:11-21`). Setter `FUN_0047E290` (`code_070000.c:7958`)
writes `obj+0x154` and calls `FUN_0048BBDC(obj,(v&0xff)<<8,(v&0xff)<<9)`, i.e. a
colour/palette parameter. **Medium:** a video tint/palette selector, persisted by
the saver but not re-read by this build's loader.

### AceVideo 0xE9 — unresolved

Not a base-loader tag and not present in the TGRACEVideo class map
(`fields/0x21_TGRACEVideo.md` maps only tag `0x01`). No evidence. **Unresolved.**

### AlphaNew 0x04 / 0x0D / 0x0E (declared ARGB_COLOR, actually ints)

The decoder calls all three `"UNKNOWN 0xNN"` with role `ARGB_COLOR`, but the class
map `fields/0x19_TGRAlphaNew.md` shows they are not colours:

- `0x04` → `obj+0x164`, 4-byte int, default `0x10` (16): slice/frame count.
- `0x0D` → `obj+0x1BC`, computed as bitmap height / slice count: slice height.
- `0x0E` → `obj+0x1DC`, slice-height remainder; ctor zeroes it.

MFME's `SaveToStream` writes all three (`code_070000.c:9459,9467,9468`), so they
are round-tripped, but they are derived geometry written by the editor, not user
colours. **Medium** (offset + derivation from the class map; the decoder's
`ARGB_COLOR` role is a mislabel, the 4-byte width is fine).

### Border 0x04 (object+0x15C)

`fields/0x1B_TGRBorder.md:18`: loader `0x04 → obj+0x15C`, SaveToStream writes it
only when `!= 1`. A border flag/index mirroring base `0x0B`'s slot type. **Low.**

### DiscReel 0x06 (object+0xCC4)

`fields/0x06_TGRDiscReel.md:18`: class `0x06 → obj+0xCC4`, 4 bytes, purpose
unclear; the class `Assign` does not copy `obj+0xCC4`. **Low.**

### DotAlpha 0x03 / EpochDotAlpha 0x03, 0x09 / MatrixAlpha 0x03

Dot-display numeric fields with no located setter:

- DotAlpha `0x03 → obj+0x170`, default `0x1F` (31) (`fields/0x13_TGRDotAlpha.md:15`).
- EpochDotAlpha `0x03 → obj+0x170` default 0, `0x09 → obj+0x17C` default 0
  (`fields/0x16_TGREpochDotAlpha.md:13,19`).
- MatrixAlpha `0x03 → obj+0x17E0`, default 31, not referenced by the redraw path
  (`fields/0x1A_TGRMatrixAlpha.md:13`).

All are round-tripped but unused in the traced render paths. **Low.**

### FlipReel 0x09 (object+0x27C)

`fields/0x2D_TGRFlipReel.md:19`: class `0x09 → obj+0x27C`, 1-byte flag, ctor
default 0; the save writes `0x09` from that offset. Purpose not established.
**Low.**

### Lamp 0x0E (object+0x21C)

`fields/0x04_TGRFancyLamp.md:30`: class `0x0E → obj+0x21C`, 4-byte int. It is
written by the shape setter `FUN_004a27e0` to `0x32` (50) when the selected shape
is `7` (`code_0a0000.c:1629-1630`), and zeroed otherwise. **Hypothesis:** a
shape-dependent size/parameter used only for some shapes. **Low.**

### Lamp 0x68 and 0x5C..0x67 (24-byte region)

The class map `fields/0x04_TGRFancyLamp.md:68` shows tags `0x5C..0x68` are read as
**24 raw bytes each** into the same `obj+0x264` struct array (stride `0x18`),
overlapping the `0x3E..0x41` / `0x56..0x59` slots. The decoder instead maps:

- `LampProfileV20_1.cs:125-136`: `0x5C..0x67` as `RAW`, length `0x18` (matches the
  field map), and
- `LampProfileV20_1.cs:109`: nested `0x68` as `TLVBLOCK`, plus a second, unused
  base-section `0x68` at line 40 (`UINT32`).

The base loader has **no** `0x68` case, so the line-40 base entry can never match
in MFME's base pass; and MFME reads class `0x68` as 24 raw bytes, not a TLV block.
**Finding:** the base `0x68` entry is likely dead, and the nested `0x68` TLV
mapping disagrees with the shipped class loader. Marked **low**; worth confirming
against a sample layout. The `0x5C..0x67` RAW-24 entries agree with the field map
(a per-element, tech-specific struct array) but their semantics are unknown.

### SevenSeg 0x24 (object+0x193) — Style2

`fields/0x0E_TGRSevenSeg.md:33`: class `0x24 → obj+0x193`, setter `SetStyle2`
(`code_0b0000.c:5428`), ctor default 0. A second style index alongside
`SelectedStyleId` (`0x20 → obj+0x192`). **Medium.**

---

## 3. Cross-cutting findings

- **obj+0x48 (base `0x3B`) is serialized but inert.** Every base field except this
  one is copied by `FUN_0048de94`; `obj+0x48` is written on load, preserved on
  save, and read by no base-class code found in the corpus. If a name is wanted,
  "unknown/reserved element dword" is the honest label.
- **The sub-lamp map is the one base structure the "Unknown" array slots belong
  to.** `0x30`, `0x33`, `0x34` (and `0x38`/`0x39`) are the same per-sub-lamp
  channel/bit table the `element_binding.md` F7/F8 documents; they are not
  independent features.
- **`0x37` (base) and Border's own `0x08` alias `obj+0x108`.** Both the base save
  and Border's SaveToStream read the same dword; a tag-id collision worth noting
  in the writer.
- **Several "Unknown" tags are simply class fields MFME exposes nowhere.** The
  dot-display numerics (`0x03`, `0x09`) and AlphaNew's `0x04/0x0D/0x0E` are
  round-tripped but either derived or unused in the traced render paths.
- **Two profile entries disagree with the shipped reader:** AceVideo `0x34`
  (declared 1-byte `UINT16`, base `0x34` is 4 bytes) and Lamp nested `0x68`
  (declared `TLVBLOCK`, shipped reader reads 24 raw bytes).

## 4. Coverage

- Distinct `"Unknown 0xNN"` declaration sites in `V20_1/*.cs`: **61**
  (across 37 component profiles).
- Distinct (component, tag) Unknown pairs: **~55**; distinct tag ids: **~38**.
- Base-section Unknowns resolved to an object field + reader/writer: **all** (the
  base reader is fully mapped).
- Unknowns with a high/medium confidence meaning: **~15**
  (`0x19`, `0x18`-pair `0x17`, `0x08`, `0x30/0x33/0x34`, `0x37`, `0x38/0x39`,
  AceVideo `0x01`, AlphaNew `0x04/0x0D/0x0E`, SevenSeg `0x24`).
- Unknowns left **low / unresolved**: the rest, chiefly `0x3B` (inert), `0x06`,
  `0x09/0x0A/0x0B`, `0x1C`, `0x3E`, the dot-display numerics, Lamp `0x0E`/`0x68`/
  `0x5C..0x67`, DiscReel `0x06`/`0x1C`, Bitmap `0x05`/`0x08`, Border `0x04`,
  FlipReel `0x09`, AceVideo `0xE9`.

## 5. Open questions

1. `obj+0x48` (base `0x3B`): no consumer found. A runtime dummy-element draw or a
   sample-layout diff would settle whether it is read at all.
2. AceVideo `0xE9`: not in any mapped reader. Needs a corpus/sample search for the
   byte in AceVideo-style records.
3. Lamp `0x68` (TLVBLOCK vs 24-byte raw) and the extra base `0x68`: confirm against
   a layout that carries tag `0x68`.
4. `0x09/0x0A/0x0B` and `obj+0x9E` (`0x3E`) flags: the setters exist
   (`FUN_0048d69c`/`d6a4`, `FUN_004a27e0`) but no draw-time reader was located.
5. The decoder's Lamp/SevenSeg base-`0x08` default of `0x32` disagrees with the
   loader default of 0; confirm against a layout that omits tag `0x08`.

---

## Residual class tags (second pass)

Second pass over the separated-type class loaders (Reel, BandReel, Alpha, Lamp),
resolving tag ids a first pass left as `"Unknown 0xNN"`. Same base/class split as
§1/§2. The main result: several of these are not new fields at all. They are the
class-section spelling of a **base** flag, writing the same byte the base loader
reaches through base tags `0x3C`/`0x40`/`0x4C`.

Citations use RVA (durable) plus the current `code_*.c` line where the two differ.
The class loaders are all shaped `FUN_0048fe68(obj, stream)` (base pass) followed by
`while(tag != 0) switch(tag) { ... }` (class pass).

### 0. The three aliased base flags

These low offsets are the shared TGRBase element flags. Every "separated" class
duplicates them under its own low class tag ids.

| base tag | offset | base name (per profile) | setter (RVA) | base save |
|---|---|---|---|---|
| `0x3C` | `obj+0xEF` | `Inverted` / `InvertedOpto` | `FUN_004854e8` `0x0854E8` | `code_080000.c:9903-9904` |
| `0x40` | `obj+0x12E` | `Reversed` / `Reverse` | `FUN_00485724` `0x085724` | `code_080000.c:9917-9918` |
| `0x4C` | `obj+0xFF` | `HorizontalVertical` / `IsHorizontal` | `FUN_00485774` `0x085774` | `code_080000.c:9934` |

- Base ctor zeroes all three: `obj+0xEF` `code_080000.c:6812`, `obj+0x12E:6827`,
  `obj+0xFF:6860`. Default is 0 for all.
- `obj+0x12E` flips the reel position in `FUN_0048555c` `0x08555C`
  (`code_080000.c:2953-2967`): `if (obj+0x12e != 0) iVar1 = res - iVar1;`. So non-zero
  mirrors the strip. Blob-name "Reversed"/"Reverse". **High.**
- `obj+0xFF` selects vertical vs horizontal layout math in `FUN_00487254` `0x087254`
  (`code_080000.c:4257+`): branch picks `0x118 - y` vs `0x114 - x`. Blob-name
  "HorizontalVertical"/"IsHorizontal". **High.**
- `obj+0xEF` is a plain bool: setter `FUN_004854e8` stores only, no redraw. It is read
  with `obj+0xED` (Coin/Note Selected, base `0x19`) in `code_0e0000.c:3746`:
  `if ((param_3+0xef != 0) && (param_3+0xed == 0)) cVar1 = cVar1 == 0;`, i.e. it
  inverts an input-selected test. Blob-name "Inverted"/"InvertedOpto". **Medium.**

The pattern across classes: Reel spells them `0x02`/`0x12`/`0x13`, BandReel
`0x0F`/`0x0A`/`0x0B`, Alpha `0x05`.

### 1. Reel (TGRFancyReel, element `0x03`)

Class loader `FUN_004a89fc` RVA `0x0A89FC` (`code_0a0000.c:5472`). Class save
`FUN_004a85b4` RVA `0x0A85B4` (`code_0a0000.c:5293`). Memberwise `Assign`
`FUN_004a8304` RVA `0x0A8304` (`code_0a0000.c:5200`). Ctor `FUN_004a5784` RVA
`0x0A5784` (`code_0a0000.c:3553`).

| tag | role/len | object field | meaning | controls | conf |
|---|---|---|---|---|---|
| `0x12` | byte/1 | `obj+0xFF` | base `0x4C` HorizontalVertical | reel orientation (v/h) | high |
| `0x13` | byte/1 | `obj+0x12E` | base `0x40` Reversed | reel scroll direction | high |
| `0x14` | u32/4 | discarded local | none (legacy skip) | nothing on load | high |
| `0x18` | byte/1 | discarded local | inert skip, **not** ButtonNumber | nothing on load | high |
| `0x1B` | u32/4 | `obj+0x2F4` | reel int, default 100 | no consumer located | med (field)/low (role) |
| `0x1D` | byte/1 | discarded local | none (legacy skip) | nothing on load | high |
| `0x22` | u32/4 | `obj+0x3A4` | optional int, default `-1` | no consumer located | low |
| `0x23` | u32/4 | `obj+0x3A8` | optional int, default `-1` | no consumer located | low |

- `0x12`, `0x13`, and `0x02` (not requested but part of the trio) are the loader's
  spellings of the three base flags: `case 0x02 -> obj+0xEF` (`code_0a0000.c:5516-5518`),
  `case 0x12 -> obj+0xFF` (`5582-5584`), `case 0x13 -> obj+0x12E` (`5585-5587`).
  Reel-specific setters confirm them: `obj+0x12E` setter `FUN_004a70a4` `0x0A70A4`
  (`code_0a0000.c:4320`) calls `FUN_004a6b80` (relayout); `obj+0xEF` setter
  `FUN_004a70fc` `0x0A70FC` (`4370`) just redraws.
- `0x14`/`0x18`/`0x1D` read into stack locals and are discarded:
  `case 0x14 -> auStack_44,4` (`code_0a0000.c:5588-5590`), `case 0x18 -> uStack_45,1`
  (`5600-5602`), `case 0x1D -> uStack_45,1` (`5622-5624`). The class save
  (`code_0a0000.c:5293-5401`) writes none of them, so they are load-only skip tags.
  **`0x18` here is a 1-byte skipped byte, not the 4-byte base `ButtonNumber`**
  (`obj+0xE8`): the tag id collides across the two lists but the meaning does not.
- `0x1B` (`obj+0x2F4`, `piVar1+0xBD`): ctor default `100` (`code_0a0000.c:3686`); save
  writes it only when `!= 100` (`5355-5356`); `Assign` copies it (`5167`, `5225`). No
  reader of `obj+0x2F4` exists in the Reel class or elsewhere in `code_0a0000.c`; the
  `code_260000.c` hits on `+0x2f4` are a different class (`FUN_0066xxxx`, its own
  `0x300`/`0x58`/`0x5c` fields), not the reel. The field is real and round-tripped,
  role unresolved. **Medium** on offset/default, **low** on meaning.
- `0x22` (`obj+0x3A4`, `piVar1+0xE9`) and `0x23` (`obj+0x3A8`, `piVar1+0xEA`): loader
  `code_0a0000.c:5637-5639` / `5640-5642`; `Assign` copies both (`5186-5187`, `5251-5252`);
  save writes each only when `!= -1` (`5372-5373`, `5375-5376`), so default `-1`. The
  only other in-file reads of `obj+0x3A4`/`obj+0x3A8` are inside `FUN_004a1df0` RVA
  `0x0A1DF0`, which is a **Lamp** method (it uses `obj+0x560`, a Lamp ctor field), so
  those reads are the Lamp matrix-array, not the reel. **No consumer of the Reel's
  `obj+0x3A4`/`obj+0x3A8` was located. Unresolved.**

### 2. BandReel (TGRBandReel, element `0x08`)

Class loader `FUN_00486e7c` RVA `0x086E7C` (`code_080000.c:4118`). Ctor
`FUN_00484dd0` RVA `0x084DD0` (`code_080000.c:2706`). The class `SaveToStream`
(`0x086D0C`) was not emitted by the decompiler, so no class-save tag list is available;
the base flags it aliases are still saved by the base writer.

| tag | role/len | object field | meaning | controls | conf |
|---|---|---|---|---|---|
| `0x0A` | byte/1 | `obj+0xFF` | base `0x4C` HorizontalVertical | reel orientation (v/h) | high |
| `0x0B` | byte/1 | `obj+0x12E` | base `0x40` Reverse | reel scroll direction | high |
| `0x0F` | byte/1 | `obj+0xEF` | base `0x3C` Inverted | input-invert flag | medium |
| `0x11` | byte/1 | discarded local | none (legacy skip) | nothing on load | high |
| `0x14` | u32/4 | `obj+0x23C` | unknown int | no consumer located | low |
| `0x15` | byte/1 | `obj+0x240` | unknown byte | no consumer located | low |

- Loader: `case 0x0A -> obj+0xFF` (`code_080000.c:4199-4201`), `case 0x0B -> obj+0x12E`
  (`4202-4204`), `case 0x0F -> obj+0xEF` (`4214-4216`), `case 0x11 -> uStack_41,1`
  discarded (`4220-4222`), `case 0x14 -> piVar1+0x8F` (`4229-4231`), `case 0x15 ->
  piVar1+0x90` (`4232-4234`).
- `obj+0xFF` is consumed by the BandReel h/v math `FUN_00487254` (`code_080000.c:4266`,
  `4273`), confirming `0x0A` = orientation. `obj+0x12E`/`obj+0xEF` use the shared
  setters/maths in §0.
- `0x14` (`obj+0x23C`) and `0x15` (`obj+0x240`) have **no reader anywhere** in the
  corpus (`rg` for `+ 0x23c)`/`+ 0x240)` matches only unrelated classes, e.g.
  `code_050000.c:7011` which is a countdown timer in a different object). The field map
  marks both "No save counterpart". **Unresolved; no locatable consumer.**

### 3. Alpha (TGRAlpha, element `0x07`)

Class loader RVA `0x07EFE4` (`code_070000.c:8288`).

| tag | role/len | object field | meaning | controls | conf |
|---|---|---|---|---|---|
| `0x05` | byte/1 | `obj+0x12E` | base `0x40` Reversed | alpha strip order | high |

- Loader `case 0x05 -> obj+0x12E` (`code_070000.c:8339-8341`). Same byte as base `0x40`
  "Reversed"; `fields/0x07_TGRAlpha.md` already notes `obj+0x12E` is the "orientation /
  strip-order flag byte (0 reverses order in Render)", and the profile maps base `0x40`
  to `"Reversed"` (`AlphaProfileV20_1.cs:23`). **High.** (The field map's "0 reverses"
  phrasing is the inverse sense of the reel-side "non-zero mirrors"; either way the
  flag selects the strip order.)

### 4. Lamp (TGRFancyLamp, element `0x04`)

Class loader `FUN_004a467c` RVA `0x0A467C` (`code_0a0000.c:2920`). Ctor `FUN_004a1820`
RVA `0x0A1820`.

| tag | role/len | object field | meaning | controls | conf |
|---|---|---|---|---|---|
| `0x0E` (nested) | u32/4 | `obj+0x21C` | shape size/scale parameter | shape-7 pie/disc scale | medium |
| `0x68` (nested) | raw/24 | `obj+0x384` | 24-byte geometry slot, **not** TLV | drawn geometry | medium |

- `0x0E`: loader `case 0x0e -> piVar1+0x87` = `obj+0x21C` (`code_0a0000.c:2989-2991`);
  save writes it when non-zero (`code_0a0000.c:2765-2766`). The shape setter
  `FUN_004a27e0` `0x0A27E0` (`code_0a0000.c:1609-1640`) sets `obj+0x21C = 0x32` (50) and
  `obj+0x208 = 10` when `SelectedShapeIndex == 7`, else `0` (`1629-1635`). The draw
  `FUN_004a2abc` `0x0A2ABC` (`code_0a0000.c:1793`) uses it as a scale in the shapes 7/8
  branch: `local_7c = (100.0 - (float)obj+0x21C) / 100.0` (`2141`). So it is a
  shape-specific size/inset parameter (50 for shape 7, absent means 0 = full size).
  **Medium** (offset and setter are certain; the exact visual is inferred from the one
  draw site).
- `0x68`: loader `case 0x68 -> piVar1 + tag*6 - 399, 0x18` (`code_0a0000.c:3191-3192`),
  i.e. **24 raw bytes** into `obj + (0x68*6-399)*4 = obj+0x384`, the next slot of the
  24-byte-stride geometry struct array based at `obj+0x264` that `0x5C..0x67` also fill
  (`case 0x5C..0x68`, `3179-3192`). The array's slots are 4-int rectangles copied by
  the draw (`code_0a0000.c:3942-3947`, `3977-3982`, `4013`). So the decoder's
  `"Unknown 0x68 (TLV Block)"` mapping (`LampProfileV20_1.cs:109`) is **wrong**: it is a
  24-byte geometry blob, not a TLV. The base-section `0x68` (`LampProfileV20_1.cs:40`)
  is **dead**: `FUN_0048fe68` has no `0x68` case (its switch ends at `0x4C`,
  `code_080000.c:10425-10613`), so that entry can never match in MFME's base pass.
  **Medium.**

### 5. Not resolved

- **Reel `0x1B`** (`obj+0x2F4`): real int, default 100, round-tripped; no reader found.
- **Reel `0x22`/`0x23`** (`obj+0x3A4`/`obj+0x3A8`): real optional ints, default `-1`,
  round-tripped; no reel reader found (offset is reused by Lamp as a matrix array).
- **Reel `0x14`/`0x18`/`0x1D`, BandReel `0x11`**: read-and-discarded skip tags; no field.
- **BandReel `0x14`/`0x15`** (`obj+0x23C`/`obj+0x240`): no reader anywhere; nothing to
  tie them to.
- **Lamp `0x68`**: geometry blob only (no name); shape meaning of the array unknown.

---

## Deep pass: medium-confidence names

Second pass over the five medium-confidence fields above, following each value past
"the id lands at offset X" to the consumer that computes something with it. All
evidence is from the shipped binary (`MFME_unpacked.exe`) and its Ghidra decompile
(`decomp/code_*.c`); the base element VMT was read from the raw PE (imagebase
`0x00400000`) and cross-checked with `rea function ... --provider ghidra` (snapshot).
No application source was changed. Line numbers are from the top-level
`decomp/code_*.c`; RVAs are the durable citation.

Base element VMT = VA `0x00A77BA4` (`MFME_unpacked.exe` RVA `0x677BA4`), stored by the
base ctor `FUN_0048b2d0` (RVA `0x08B2D0`) at `code_080000.c:6742`
(`*local_8 = &PTR_FUN_00a77ba4`). Entry `+0x00` = `FUN_0048b8a0`, `+0x04` = `FUN_0048bbdc`,
`+0x08` = `FUN_0048bd44`, **`+0x0C` = `FUN_0048bd9c`**, ... `+0xA8` = `FUN_0048ee2c`.

### 1. Base `0x08` — refuted "not stored"; confirmed **rotation angle**

- The loader tail hands the value to the geometry virtual `vtable+0x0C`
  (`code_080000.c:10657-10659`):
  `(*param_1)[0xC](obj, obj+0xA8 /*x*/, obj+0xAC /*y*/, local_40 /*tag 0x08*/, obj+0xC0 /*h*/, obj+0xC4 /*w*/);`
- `vtable+0x0C` is `FUN_0048bd9c` (RVA `0x08BD9C`, `code_080000.c:7120`). Its `param_4`
  is the tag value. On a change it calls
  `FUN_004917ec(obj, param_4, width, &y, &x, height)` (`code_080000.c:7170`, `7175`).
- `FUN_004917ec` (RVA `0x0917EC`, `code_090000.c:925`) is the transform:
  - angle `== 0` frees the transform helper objects at `obj+4`/`obj+8`/`obj+0x3C` and
    returns the raw `obj+0xB0`/`obj+0xB4`;
  - angle `!= 0` computes `local_98 = (_DAT_00491c18 * -angle) / 360.0`. The global
    `_DAT_00491c18` (VA `0x00491C18`) is an 80-bit long double holding **π**
    (`00 c0 68 21 a2 da 0f c9 00 40` = `0x4000C90FDAA22168C000`). So it converts degrees
    to radians. It then builds a rotation helper (`obj+0x3C` via `FUN_00673c94` with
    VMT `PTR_FUN_00670164`; `FUN_00674224(..., -angle/2.0, ...)` rotates about the
    centre), sums the `sin`/`cos` terms for the rotated extent, and finally stores
    `*(int *)(obj+0xD4) = param_2` (`code_090000.c:1085`).
- Because the value round-trips through `obj+0xD4`, the earlier note that base `0x08`
  is "read to a stack local (not stored on the object)" is wrong: it is stored on the
  object at `obj+0xD4` by the vtable call, and the base save writes tag `0x08` from
  `obj+0xD4` (the BASE.md mismatch). The decoder already calls it `GeometryAngle`
  (`AlphaProfileV20_1.cs:20`, `Bitmap`, `Lamp`, `MatrixAlpha`, `Reel`, `SevenSeg`).
- **Verdict: CONFIRMED** (rotation angle in degrees). Recommended name
  **`GeometryAngle`**; note it is persisted at `obj+0xD4`. Confidence **high**.

### 2. Base `0x17` — confirmed **InputPresent**; consumer is the machine-input dispatch

- The paired value is `obj+0xE8` (base `0x18`), not just "meaningful when 0x17 set":
  the element property dump prints `obj+0xE8` only when `obj+0xEC != 0`
  (`code_010000.c:10005-10016`: `if (obj+0xEC == 0) write ""; else write obj+0xE8`).
- Real consumer: the input dispatcher `FUN_004e7b88` (RVA `0x0E7B88`,
  `code_0e0000.c:3732`) guard at `3753-3755`:
  `if (DAT_00b930de == 0 && ((obj+0xEC != 0 || obj+0xED != 0) && obj+0xF1 != 0))` then,
  with `obj+0xED == 0`, `FUN_004e8330(..., *(uint *)(obj+0xE8), ...)` — it routes the
  bound input number. Same pattern in `FUN_004e816c` (`code_0e0000.c:3944-3949`).
  So `obj+0xEC` decides whether the machine routes a machine input to this element; it
  is not a draw or hit-test flag.
- The element's own setter is `vtable+0xA8 = FUN_0048ee2c` (writes `obj+0xEC = param_2`;
  it is the memberwise Assign target, `code_080000.c:8550-8551`). Lamp override
  `FUN_004a23f4` (`code_0a0000.c:1372`) toggles it and relayouts/redraws.
  The `(obj+0xE4, obj+0xE8)` pair is set by `FUN_00488160` (`code_080000.c:4819`).
- Caution: `FUN_0048d69c`/`FUN_0048d6a4` also write a byte at `+0xEC` (`code_080000.c:8086`,
  `8098`), but their object is the host/machine (it owns the element list at `obj+0xf8`
  and the flag `obj+0x13c`, `code_080000.c:7950-8005`), a different class. The decompiler
  collapses both to `+0xec`; those two are not the element field.
- **Verdict: CONFIRMED** — **`InputPresent`** ("this element is bound to a machine input").
  Confidence **high** on the role; **medium** on the literal "present" wording (it could
  equally read "input enabled").

### 3. Base `0x37` / Border `0x08` — confirmed **spacing**; it is a geometric inset, default 1

- Both tags land at `obj+0x108`: base loader case `0x37` (`BASE.md:74`), Border own
  loader case `8` (`code_090000.c:4235-4236`). Border ctor `FUN_00495904` sets
  `obj+0x108 = 1` (`code_090000.c:3816`), which is why Border's save skips a value of 1
  (`code_090000.c:4155-4156`) while the base save writes any non-zero value.
- Setter `FUN_00495d40` (RVA `0x095D40`, `code_090000.c:3948`) shows it is thickness,
  not an index: for a delta Δ it calls `vtable+0x10(obj, Δ, Δ)` to offset the element,
  rebuilds the BoundingBox rect (`obj+0x190`) and shifts the PaneSplit rect
  (`obj+0x1A0`, via `obj+0x68/0x69`) by `2Δ`, stores the new value, then relayouts and
  redraws. The `×2` is the inset applied on both sides.
- Reader `FUN_004962e8` (RVA `0x0962E8`, `code_090000.c:4285`): when gathering elements
  inside the border, `if (obj+0x108 == 0) FUN_00655bec(rect, -1, -1)` — the zero case is
  the "no spacing" special case.
- `FUN_0048d598` (`code_080000.c:8031`) treats a Border with `obj+0x108 > 0` as a
  child-containing border (it is shuffled between list `owner+0x64` and `owner+0xf8`).
- **Verdict: CONFIRMED** as a border spacing/inset value (not an index or style id).
  Recommended name **`BorderSpacing`** (decoder already uses it); it is the frame
  inset/offset in element pixels with default 1. Confidence **high** on the geometric
  role, **medium** on the exact visual (inferred from the two cited sites).

### 4. SevenSeg `0x24` — identity confirmed, but **refuted** as a draw-path control

- The field is literally `@TGRSevenSeg@SetStyle2` (`functions.txt` RVA `0x0B86B4`),
  storing `obj+0x193`.
- Every reference to `obj+0x193` in the whole corpus is a write or a copy: ctor init 0
  (`code_0b0000.c:5055`), `SetStyle2` store (`5518`), `Assign` copy (`5636`, `6114`),
  `SaveToStream` tag `0x24` (`5697`), loader case `0x24` (`5820`). **No read exists
  anywhere in the binary**, and there is no `GetStyle2`.
- The renderer `_TGRSevenSeg_Render` reads the *main* style `obj+0x192`
  (`code_0b0000.c:6577`, `6643`, and the `== 2` branches) and never reads `obj+0x193`.
  `SetStyle` validates `< 3`; `SetStyle2` has no such consumer.
- So `0x20` (`SelectedStyleId`) is the live style that drives Render; `0x24` (`Style2`)
  is a stored, assigned, saved second selector that no located code path consumes.
- **Verdict: keep the name `Style2`** (identity confirmed), but correct the effect: it
  changes nothing in the draw path in this build. Treat as a stored second style select
  with no located consumer. Confidence **high** that it is `SetStyle2`'s field and
  **high** that nothing reads it.

### 5. AlphaNew `0x04` / `0x0D` / `0x0E` — confirmed derived slice geometry, **not colours**

- `@TGRAlphaNew@Resize` (RVA `0x07FDBC`, `code_070000.c:9038`) derives the two computed
  fields from the count:
  - `obj+0x1BC` (tag `0x0D`) = `obj+0xC4 /*width*/ / obj+0x164 /*count*/`;
  - `obj+0x1DC` (tag `0x0E`) = `(width − (width/count)*count) / 2` = `(width mod count)/2`.
- `@TGRAlphaNew@Setup` (RVA `0x07FB74`, `code_070000.c:8988-8990`) uses them to blit the
  strip one frame at a time:
  `for (i = 0; i < obj+0x164; i++) blit(dest, i*(obj+0x1BC) + obj+0x1DC, 0, src);`
  i.e. frame `i` is pasted at `x = i * sliceSize + offset`.
- They are ints used only in loop arithmetic and offsets. The colours are the separate
  tags `0x01`/`0x02`/`0x03` → `obj+0x1B0`/`0x1B4`/`0x1B8`. The decoder's `ARGB_COLOR`
  role for `0x04`/`0x0D`/`0x0E` is a mislabel.
- Correction to the field map: `0x0D` is `element width / count` (`obj+0xC4`), so it is a
  slice *cell size* (width), not "bitmap height / slice count". The map's "SliceHeight"
  name is width-based; "SliceSize" is more accurate.
- **Verdict: CONFIRMED.** `0x04` = slice/frame count (default 16);
  `0x0D` = slice cell size = element width / count; `0x0E` = slice centring offset =
  `(width mod count)/2`. Not colours. Confidence **high**.

### Summary

| field | prior name | verdict | recommended | confidence |
|---|---|---|---|---|
| base `0x08` | GeometryAngle (stack local) | confirmed; stored at `obj+0xD4` | `GeometryAngle` (degrees) | high |
| base `0x17` | InputPresent | confirmed | `InputPresent` | high |
| base `0x37` | BorderSpacing | confirmed geometric | `BorderSpacing` (default 1) | high |
| SevenSeg `0x24` | Style2 | identity confirmed, effect refuted | `Style2` (draw-inert) | high |
| AlphaNew `0x04`/`0x0D`/`0x0E` | SliceCount/SliceHeight/SliceRemainder | confirmed, width-based | `SliceCount` / `SliceSize` / `SliceOffset` | high |

All five remain non-colour, non-index except `BorderSpacing` (geometric) and `Style2`
(a stored selector with no consumer). The two corrections worth carrying into the
profiles: base `0x08` is persisted at `obj+0xD4` (the save source), and AlphaNew
`0x0D` is width-based (slice cell size), not height-based.
