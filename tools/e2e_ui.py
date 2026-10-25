#!/usr/bin/env python3
"""Browser half of the chapter-marker E2E, against the throwaway server from e2e_jellyfin.sh.

Logs in with headless Chrome, opens the plugin settings page and one show's details page, and fails
on any page error, a status line stuck on loading, a Needs-review row that didn't render, or a show
whose chapters don't appear as scenes. Screenshots go to <out_dir>.

Usage (Chrome installed; Playwright comes from uv):
  uv run --with playwright python tools/e2e_ui.py <out_dir> <item_id_with_chapters>
"""
import json
import os
import sys
import urllib.request

from playwright.sync_api import sync_playwright

out, item = sys.argv[1:3]
U = os.environ.get("JELLYFIN_URL", "http://localhost:8196")
TOKEN = open(os.path.expanduser("~/.cache/phishdata-e2e/token")).read().strip()


def api(path):
    req = urllib.request.Request(U + path, headers={"Authorization": f'MediaBrowser Client="e2e", Device="cli", DeviceId="e2e", Version="1", Token="{TOKEN}"'})
    return json.load(urllib.request.urlopen(req))


status = api("/PhishData/Chapters/Status")
chapters = len(api(f"/Items/{item}").get("Chapters") or [])
problems = []
with sync_playwright() as p:
    browser = p.chromium.launch(channel="chrome", headless=True)
    page = browser.new_page(viewport={"width": 1400, "height": 1000})
    page.on("pageerror", lambda e: problems.append(f"page error: {e}"))
    page.goto(f"{U}/web/#/login.html")
    page.wait_for_selector("#txtManualName", timeout=30000)
    page.fill("#txtManualName", "e2e")
    page.fill("#txtManualPassword", "e2e-only")
    page.click("button[type=submit]")
    page.wait_for_timeout(4000)

    page.goto(f"{U}/web/#/configurationpage?name=PhishNetConfigPage")
    page.wait_for_selector("#chapterCounts tbody tr", timeout=30000)
    page.wait_for_timeout(1500)
    page.screenshot(path=os.path.join(out, "settings.png"), full_page=True)
    message = page.inner_text("#chapterStatusMessage")
    rows = page.query_selector_all("#chapterReview tbody tr")
    print(f"status line: {message!r}; review rows: {len(rows)} (API items: {len(status['Items'])})")
    for row in rows:
        print("  ", row.inner_text().replace("\t", " | ").replace("\n", " "))
    if message.startswith("Loading"):
        problems.append("status line stuck on loading")
    if len(rows) != len(status["Items"]):
        problems.append(f"{len(rows)} review rows for {len(status['Items'])} items")

    page.goto(f"{U}/web/#/details?id={item}")
    page.wait_for_timeout(5000)
    page.screenshot(path=os.path.join(out, "details.png"), full_page=True)
    scenes = page.query_selector_all("#scenesContent .card")
    print(f"scenes: {len(scenes)} (API chapters: {chapters});", [s.inner_text().replace("\n", " ") for s in scenes[:3]])
    if len(scenes) != chapters:
        problems.append(f"{len(scenes)} scenes for {chapters} chapters")
    browser.close()

for problem in problems:
    print("FAIL", problem)
sys.exit(1 if problems else 0)
