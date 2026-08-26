# TFP Harmony (0_TFP_Harmony)

Official TFP HarmonyX modding framework — **dependency for every C# mod in this
project** (they reference `0Harmony.dll` from here at build time and ship a copy
in their own mod folders).

## Layout

- `mod/` — the deployed mod folder, byte-identical to `Mods/0_TFP_Harmony/`
  (pinned version 1.1.0.4, from the original server install 2026-08-02). This is
  the authoritative artifact — **never rebuild/replace casually**.
- `src/` — the TfpHarmony wrapper source, reconstructed via ilspycmd
  decompilation of the shipped `TfpHarmony.dll` (session 2026-08-02; file mtimes
  22:22 local). `TfpHarmony.dll` is the small TFP wrapper; the other DLLs
  (0Harmony, MonoMod.*, Mono.Cecil.*, System.ValueTuple) are upstream HarmonyX
  binaries vendored as-is from the official distribution.

## Deploy

```bash
./build.sh        # copies mod/ -> Mods/0_TFP_Harmony
```

Deploy-only. Rebuilding the wrapper from `src/` is possible
(`cd src && /home/kyau/dotnet/dotnet build -c Release`) but the decompiled source
is publicized output — only do it if you know exactly what you're changing, then
test on a dev server first. This mod should otherwise never change.
