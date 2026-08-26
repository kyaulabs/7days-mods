#!/bin/bash
# Build + deploy via the master script (mod-src/build.py) — single source of
# truth for the whole Mods/ tree. Builds server + client DLLs.
set -e
exec python3 /srv/7days/mod-src/build.py DS_WaterDouse
