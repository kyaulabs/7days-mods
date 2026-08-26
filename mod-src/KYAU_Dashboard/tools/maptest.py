#!/usr/bin/env python3
"""Verify map tiles render aligned at the explored region."""
from playwright.sync_api import sync_playwright

with sync_playwright() as p:
    b = p.chromium.launch()
    pg = b.new_page(viewport={"width": 1440, "height": 900})
    errors = []
    pg.on("pageerror", lambda e: errors.append(str(e)))
    pg.goto("http://localhost:8090/app", wait_until="domcontentloaded")
    pg.wait_for_timeout(5000)
    pg.evaluate("document.querySelector('#map').scrollIntoView()")
    pg.wait_for_timeout(800)
    pg.evaluate("window.__ahMap && window.__ahMap.setView([1600, 64], 4)")
    pg.wait_for_timeout(3000)
    pg.screenshot(path="/tmp/shots/map_explored_z4.png")
    pg.evaluate("window.__ahMap && window.__ahMap.setView([1600, 64], 2)")
    pg.wait_for_timeout(3000)
    pg.screenshot(path="/tmp/shots/map_explored_z2.png")
    print("pageerrors:", errors)
    b.close()
