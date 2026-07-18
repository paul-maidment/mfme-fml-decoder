# GameConfig smoke goldens

Self-contained under `test/gameconfig_smoke/`:

| Path | Purpose |
|------|---------|
| `<System>/layout.gam` | Sample GAM |
| `<System>/layout.fml` | Sample FML |
| `<System>/expected.machine.json` | Golden machine Settings |
| `<System>/expected.game.json` | Golden game Settings |
| `<System>/source.json` | Metadata |

Control maps live in-assembly under `src/GameConfig/Maps/` (see `GameConfigMapRegistry`).

Linked into **MfmeFmlDecoder.UnitTests** as Content (`gameconfig_smoke/...`) so it appears in Solution Explorer.
