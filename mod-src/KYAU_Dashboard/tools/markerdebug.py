#!/usr/bin/env python3
from playwright.sync_api import sync_playwright

with sync_playwright() as p:
    b = p.chromium.launch()
    pg = b.new_page(viewport={"width": 1440, "height": 900})
    pg.goto("http://localhost:8090/app", wait_until="domcontentloaded")
    pg.wait_for_timeout(12000)
    info = pg.evaluate("""
      Array.from(document.querySelectorAll('.map-marker-icon')).map(function(el){
        return {src: el.src ? el.src.slice(0, 80) : null, ok: el.complete && el.naturalWidth > 0, cls: el.className};
      })
    """)
    print("marker imgs:", info)
    # also check if probeIcon fallback fired: any data-uri icons?
    datauri = pg.evaluate("Array.from(document.querySelectorAll('.leaflet-marker-icon')).map(e => (e.src||'').slice(0,40))")
    print("all leaflet markers:", datauri)
    b.close()
