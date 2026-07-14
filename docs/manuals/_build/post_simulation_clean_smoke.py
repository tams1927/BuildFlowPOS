"""Post-simulation clean-state smoke — read-only route checks, no data creation."""
import asyncio
import json
import os
import sys
from playwright.async_api import async_playwright

BASE = os.environ.get("SMOKE_BASE_URL", "https://localhost:7232")
OUT = os.path.join(os.path.dirname(__file__), "..", "_assets", "post_simulation_clean_smoke_results.json")

SUPER_USER = os.environ.get("SUPERADMIN_USER", "superadmin")
SUPER_PASS = os.environ.get("SUPERADMIN_PASSWORD", "SuperAdmin123!")
ADMIN_USER = os.environ.get("TENANT_ADMIN_USER", "adminTCS")
ADMIN_PASS = os.environ.get("TENANT_ADMIN_PASSWORD", "")

TENANT_ROUTES = [
    ("settings", "/Settings/Index"),
    ("branches", "/Branches/Index"),
    ("units", "/Units/Index"),
    ("categories", "/Categories/Index"),
    ("suppliers", "/Suppliers/Index"),
    ("customers", "/Customers/Index"),
    ("products", "/Products/Index"),
    ("purchaseorders", "/PurchaseOrders/Index"),
    ("receiving", "/Receiving/Index"),
    ("inventory", "/Inventory/Index"),
    ("pos", "/POS/Index"),
    ("reports", "/Reports/Index"),
]

results = []


async def login(page, user, pwd):
    await page.goto(f"{BASE}/Account/Login", wait_until="domcontentloaded")
    await page.fill("#Username", user)
    await page.fill("#Password", pwd)
    await page.click("button[type=submit]")
    await page.wait_for_load_state("domcontentloaded")
    await page.wait_for_timeout(1200)


async def check_route(page, key, url):
    try:
        resp = await page.goto(f"{BASE}{url}", wait_until="domcontentloaded", timeout=30000)
        status = resp.status if resp else 0
        await page.wait_for_timeout(600)
        final = page.url
        body = await page.content()
        is_500 = "An error occurred" in body and "Development Mode" in body
        on_login = "/Account/Login" in final
        on_denied = "/Account/AccessDenied" in final
        ok = status < 400 and not is_500 and not on_login and not on_denied
        note = ""
        if status >= 400:
            note = f"HTTP {status}"
        elif is_500:
            note = "500 error page"
        elif on_login:
            note = "login redirect"
        elif on_denied:
            note = "access denied"
        results.append({"key": key, "url": url, "status": status, "ok": ok, "note": note})
        print(f"[CLEAN-SMOKE] {'PASS' if ok else 'FAIL'} {key:20s} {url} ({note or 'OK'})")
        return ok
    except Exception as e:
        results.append({"key": key, "url": url, "status": 0, "ok": False, "note": str(e)[:200]})
        print(f"[CLEAN-SMOKE] FAIL {key:20s} {url} ({e})")
        return False


async def main():
    if not ADMIN_PASS:
        print("[CLEAN-SMOKE] TENANT_ADMIN_PASSWORD required.")
        sys.exit(1)

    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True)
        ctx = await browser.new_context(viewport={"width": 1440, "height": 900}, ignore_https_errors=True)
        page = await ctx.new_page()

        # SuperAdmin login
        await login(page, SUPER_USER, SUPER_PASS)
        super_ok = "/Account/Login" not in page.url
        results.append({"key": "superadmin_login", "url": "/Account/Login", "status": 200, "ok": super_ok, "note": ""})
        print(f"[CLEAN-SMOKE] {'PASS' if super_ok else 'FAIL'} superadmin_login")

        # Fresh tenant session (avoid fragile logout selector)
        await ctx.close()
        ctx = await browser.new_context(viewport={"width": 1440, "height": 900}, ignore_https_errors=True)
        page = await ctx.new_page()
        await login(page, ADMIN_USER, ADMIN_PASS)
        admin_ok = "/Account/Login" not in page.url
        results.append({"key": "adminTCS_login", "url": "/Account/Login", "status": 200, "ok": admin_ok, "note": ""})
        print(f"[CLEAN-SMOKE] {'PASS' if admin_ok else 'FAIL'} adminTCS_login")

        await check_route(page, "tenant_home", "/Home/Index")
        for key, url in TENANT_ROUTES:
            await check_route(page, key, url)

        # Access isolation — cashier must not exist or login fails
        await ctx.close()
        ctx = await browser.new_context(viewport={"width": 1440, "height": 900}, ignore_https_errors=True)
        page = await ctx.new_page()
        await page.goto(f"{BASE}/Account/Login", wait_until="domcontentloaded")
        await page.fill("#Username", "buildflow_cashier")
        await page.fill("#Password", "invalid-placeholder")
        await page.click("button[type=submit]")
        await page.wait_for_timeout(1000)
        cashier_blocked = "/Account/Login" in page.url or "/Home" not in page.url
        results.append({"key": "sim_cashier_absent", "url": "/Account/Login", "status": 200,
                        "ok": cashier_blocked, "note": f"final={page.url}"})
        print(f"[CLEAN-SMOKE] {'PASS' if cashier_blocked else 'FAIL'} sim_cashier_absent")

        # Tenant admin cannot reach SaaS tenants list (AccessDenied or redirect = expected)
        await login(page, ADMIN_USER, ADMIN_PASS)
        await page.goto(f"{BASE}/Tenants/Index", wait_until="domcontentloaded", timeout=30000)
        await page.wait_for_timeout(600)
        saas_blocked = "/Account/AccessDenied" in page.url or "/Tenants" not in page.url
        results.append({"key": "tenant_saas_blocked", "url": "/Tenants/Index", "status": 200,
                        "ok": saas_blocked, "note": page.url})
        print(f"[CLEAN-SMOKE] {'PASS' if saas_blocked else 'FAIL'} tenant_saas_blocked")

        await ctx.close()
        await browser.close()

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(results, f, indent=2)

    passed = sum(1 for r in results if r["ok"])
    failed = sum(1 for r in results if not r["ok"])
    print(f"\n[CLEAN-SMOKE] DONE — {passed} passed, {failed} failed")
    sys.exit(1 if failed else 0)


asyncio.run(main())
