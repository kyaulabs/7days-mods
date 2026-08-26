#!/bin/bash
# Build + deploy via the master script (mod-src/build.py) — single source of
# truth for the whole Mods/ tree. 
set -e
exec python3 /srv/7days/mod-src/build.py KYAU_AfterHoursApi
