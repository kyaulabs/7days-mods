#!/usr/bin/env python3
"""Rasterize the KYAU Labs SVG logo to a transparent PNG via headless chromium."""
import pathlib
from playwright.sync_api import sync_playwright

SVG = pathlib.Path("/srv/7days/logo-dark-noslogan.svg").read_text()
OUT = "/srv/7days/Mods/ZZ_KYAU_Dashboard/webroot/assets/kyau-labs-logo.png"

with sync_playwright() as p:
    b = p.chromium.launch()
    pg = b.new_page(viewport={"width": 804, "height": 212}, device_scale_factor=2)
    pg.set_content(
        '<html><body style="margin:0;background:transparent">'
        f'<div style="width:804px;height:212px">{SVG}</div>'
        "</body></html>"
    )
    pg.wait_for_timeout(800)
    pg.locator("div").screenshot(path=OUT, omit_background=True)
    b.close()
print("wrote", OUT)
