#!/bin/bash
# Build + deploy via the master script (mod-src/build.py) — single source of
# truth for the whole Mods/ tree. Client-side; builds from src/.
set -e
exec python3 /srv/7days/mod-src/build.py DS_VehicleCruiseControl
