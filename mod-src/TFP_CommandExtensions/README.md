# Server Command Extensions (TFP_CommandExtensions)

Official TFP "additional commands for server operation" mod (author: The Fun
Pimps LLC, v1.3.0.0). **Binary-only** — no upstream source is published.

## Layout

- `mod/` — the deployed mod folder, byte-identical to `Mods/TFP_CommandExtensions/`
  (`CommandExtensions.dll` + `ModInfo.xml`).
- `tools/CommandExtensions.decompiled.cs` — ilspycmd decompilation of the shipped
  DLL (done 2026-08-11, ilspycmd 9.1.0.7988, refs = game Managed dir + Harmony).
  **Reference only** — for understanding which console commands the mod provides
  and how they're registered; not a buildable source tree.

## Deploy

```bash
./build.sh        # copies mod/ -> Mods/TFP_CommandExtensions
```

If the upstream DLL is ever updated, replace `mod/` contents (and refresh the
decompiled reference). Restart the server to load.
