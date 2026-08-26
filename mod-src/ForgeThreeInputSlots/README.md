# Forge Three Input Slots (1_ForgeThreeInputSlots)

Adds a third material input slot to the forge (2 → 3). **XML-only UI modlet** —
no code, no build step. Install on the server AND every client (it is part of
`AfterHours_ClientMods.zip` via `mod-src/build.py` `CLIENT_MODS`).

## Layout

- `mod/` — the modlet: `Config/XUi_InGame/windows.xml` (the `@remove`/`@add`
  window patch) + `ModInfo.xml`. The XML *is* the source; edit in `mod/`, then
  deploy.

## Deploy

```bash
./build.sh        # copies mod/ -> Mods/1_ForgeThreeInputSlots + refreshes client pack
```

No version bump needed unless the patch changes.
