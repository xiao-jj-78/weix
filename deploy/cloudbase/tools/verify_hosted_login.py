"""Inspect the public CloudBase login screen, without entering any access code.

Uses a new, empty browser context and an installed Chrome channel. CloudBase's
test-domain access notice is handled through its visible, enabled Continue
button. This does not inspect the user's existing browser or cloud account.
"""

from __future__ import annotations

import hashlib
import json
import urllib.request
from pathlib import Path

from playwright.sync_api import expect, sync_playwright


ROOT = Path(__file__).resolve().parents[3]
PUBLIC = ROOT / "deploy" / "cloudbase" / "public"
EVIDENCE = ROOT / "build" / "qa" / "evidence"
BASE = "https://jou-d4gq7a3cqf51e6caa-1394316826.tcloudbaseapp.com/wechat-workspace/"


def main():
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    record = {"websiteUrl": BASE + "index.html", "files": [], "browser": "installed Chrome",
              "loginScreenVerified": False, "credentialsEntered": False,
              "realWechatOperations": False, "cloudResourcesChanged": False,
              "testDomainAccessNotice": False, "pageErrors": []}
    try:
        for name in ("index.html", "cloud-config.js", "cloud-bootstrap.js", "cloud.css"):
            with urllib.request.urlopen(BASE + name, timeout=25) as response:
                remote = response.read()
                status = response.status
            local = (PUBLIC / name).read_bytes()
            record["files"].append({"file": name, "http": status,
                                    "same_sha256": hashlib.sha256(remote).digest() == hashlib.sha256(local).digest()})
        with sync_playwright() as p:
            browser = p.chromium.launch(headless=True, channel="chrome", args=["--no-proxy-server"])
            try:
                page = browser.new_page(viewport={"width": 1440, "height": 1000}, device_scale_factor=1)
                page.on("pageerror", lambda error: record["pageErrors"].append(str(error)))
                requests = []
                page.on("request", lambda request: requests.append(request.url) if "/api/" in request.url else None)
                response = page.goto(BASE + "index.html", wait_until="domcontentloaded", timeout=30000)
                record["initialHttp"] = response.status if response else None
                if page.title() == "风险提醒":
                    record["testDomainAccessNotice"] = True
                    page.screenshot(path=str(EVIDENCE / "cloud-test-domain-notice-live.png"), full_page=True)
                    continue_button = page.get_by_role("button", name="确定访问", exact=True)
                    expect(continue_button).to_be_enabled(timeout=10000)
                    with page.expect_navigation(wait_until="networkidle", timeout=30000) as navigation:
                        continue_button.click()
                    record["afterNoticeHttp"] = navigation.value.status if navigation.value else None
                else:
                    page.wait_for_load_state("networkidle")
                expect(page.locator("#cloud-login-form")).to_be_visible(timeout=15000)
                assert page.locator("#cloud-workspace").get_attribute("inert") is not None
                assert page.locator("#cloud-access-code").get_attribute("type") == "password"
                assert page.locator("#cloud-access-code").input_value() == ""
                assert page.evaluate("sessionStorage.length") == 0
                assert page.evaluate("document.documentElement.scrollWidth <= innerWidth")
                page.screenshot(path=str(EVIDENCE / "cloud-login-live.png"), full_page=True)
                page.set_viewport_size({"width": 390, "height": 844})
                assert page.evaluate("document.documentElement.scrollWidth <= innerWidth")
                page.screenshot(path=str(EVIDENCE / "cloud-login-live-mobile.png"), full_page=True)
                assert not requests, "An API was requested before access-code entry"
                record["unauthenticatedApiRequestCount"] = len(requests)
                record["pageTitle"] = page.title()
                record["loginScreenVerified"] = True
            finally:
                browser.close()
    except Exception as e:
        record["error"] = str(e)[:1500]
    record["ok"] = record["loginScreenVerified"] and not record["pageErrors"] and all(
        item["http"] == 200 and item["same_sha256"] for item in record["files"])
    (EVIDENCE / "cloud-page-result.json").write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(record, ensure_ascii=False, indent=2))
    raise SystemExit(0 if record["ok"] else 1)


if __name__ == "__main__":
    main()
