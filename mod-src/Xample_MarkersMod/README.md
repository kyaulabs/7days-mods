# Markers (Example Web Mod) — Xample_MarkersMod

TFP example web mod for custom markers on the web map (author: Catalysm and
Alloc, v1.3.0.0 — ModInfo `<Name>` is `TFP_MarkersExample`). Ships the
`/api/markers` web API (persisted via `MapRendering`/marker files) plus a WebMod
UI for placing markers. The AfterHours dashboard does NOT use its UI anymore
(traders come from `/api/afterhours/traders`), but the mod stays installed as
the marker backend.

## Layout

- `mod/` — deployed folder, byte-identical to `Mods/Xample_MarkersMod/`
  (`MarkersMod.dll`, `WebMod/bundle.js` + `styling.css`).
- `tools/MarkersMod.decompiled.cs` — ilspycmd decompilation of the DLL
  (session 2026-08-03). **Reference only** — shows the web API surface
  (`/api/markers`, marker persistence). `bundle.js` is minified web UI; no
  readable upstream source exists.

## Deploy

```bash
./build.sh        # copies mod/ -> Mods/Xample_MarkersMod
```

Restart the server to load. Marker persistence lives in the save dir, not in
the mod folder.
