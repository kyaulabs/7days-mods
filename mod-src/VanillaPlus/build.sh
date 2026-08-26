#!/bin/bash
# Build + deploy via the master script (mod-src/build.py) — single source of
# truth for the whole Mods/ tree. Patches TMO DLLs (removes TMO Core gate) + builds Crate Guards.
set -e
exec python3 /srv/7days/mod-src/build.py VanillaPlus
