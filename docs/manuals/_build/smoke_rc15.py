"""RC1.5 UI smoke test — HTTP status + no login-redirect for key routes."""
import asyncio, json, os, sys
from playwright.async_api import async_playwright

BASE = os.environ.get("RC15_BASE_URL", "https://localhost:7232")
OUT = os.path.join(os.path.dirname(__file__), "..", "_assets", "rc15_smoke_results.json")

SUPER_ROUTES = [
    ("login", "/Account/Login", None, None),
    ("super_dashboard", "/Home/Index", "superadmin", "SuperAdmin123!"),
    ("super_tenants", "/Tenants/Index", "superadmin", "SuperAdmin123!"),
    ("super_plans", "/SubscriptionPlans/Index", "superadmin", "SuperAdmin123!"),
    ("super_users", "/Users/Index", "superadmin", "SuperAdmin123!"),
    ("super_audit", "/AuditTrail/Index", "superadmin", "SuperAdmin123!"),
]

TENANT_ROUTES = [
    ("tenant_dashboard", "/Home/Index", "owner", "Owner123!"),
    ("settings", "/Settings/Index", "owner", "Owner123!"),
    ("branches", "/Branches/Index", "owner", "Owner123!"),
    ("users", "/Users/Index", "owner", "Owner123!"),
    ("products", "/Products/Index", "owner", "Owner123!"),
    ("inventory", "/Inventory/Index", "owner", "Owner123!"),
    ("stockin", "/StockIn/Index", "owner", "Owner123!"),
    ("pos", "/POS/Index", "owner", "Owner123!"),
    ("sales", "/Sales/Index", "owner", "Owner123!"),
    ("customers", "/Customers/Index", "owner", "Owner123!"),
    ("suppliers", "/Suppliers/Index", "owner", "Owner123!"),
    ("purchaseorders", "/PurchaseOrders/Index", "owner", "Owner123!"),
    ("quotations", "/Quotations/Index", "owner", "Owner123!"),
    ("deliveryreceipts", "/DeliveryReceipts/Index", "owner", "Owner123!"),
    ("reports", "/Reports/Index", "owner", "Owner123!"),
    ("import", "/Import/Index", "owner", "Owner123!"),
    ("audit", "/AuditTrail/Index", "owner", "Owner123!"),
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
        await page.wait_for_timeout(800)
        final = page.url
        body = await page.content()
        is_500 = "An error occurred" in body and "Development Mode" in body
        on_login = "/Account/Login" in final and key != "login"
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
        print(f"[SMOKE] {'PASS' if ok else 'FAIL'} {key:22s} {url} ({note or 'OK'})")
        return ok
    except Exception as e:
        results.append({"key": key, "url": url, "status": 0, "ok": False, "note": str(e)[:200]})
        print(f"[SMOKE] FAIL {key:22s} {url} ({e})")
        return False


async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch()
        ctx = await browser.new_context(viewport={"width": 1440, "height": 900}, ignore_https_errors=True)
        page = await ctx.new_page()

        # Login page (unauthenticated)
        await check_route(page, "login", "/Account/Login")

        # SuperAdmin session
        await login(page, "superadmin", "SuperAdmin123!")
        for key, url, _, _ in SUPER_ROUTES[1:]:
            await check_route(page, key, url)

        await ctx.close()

        # Tenant session
        ctx2 = await browser.new_context(viewport={"width": 1440, "height": 900}, ignore_https_errors=True)
        page2 = await ctx2.new_page()
        try:
            await login(page2, "owner", "Owner123!")
            for key, url, _, _ in TENANT_ROUTES:
                await check_route(page2, key, url)
            # Currency symbol visible on POS
            await page2.goto(f"{BASE}/POS/Index", wait_until="domcontentloaded")
            content = await page2.content()
            has_currency = "₱" in content or "$" in content or "CurrencySymbol" in content
            sym_ok = "₱" in content or "$" in content
            results.append({"key": "currency_symbol_pos", "url": "/POS/Index", "status": 200, "ok": sym_ok, "note": "" if sym_ok else "no currency symbol"})
            print(f"[SMOKE] {'PASS' if sym_ok else 'FAIL'} currency_symbol_pos     /POS/Index")
        except Exception as e:
            results.append({"key": "tenant_session", "url": "login", "status": 0, "ok": False, "note": f"owner login failed: {e}"})
            print(f"[SMOKE] FAIL tenant_session       owner login: {e}")

        await ctx2.close()
        await browser.close()

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(results, f, indent=2)

    passed = sum(1 for r in results if r["ok"])
    failed = sum(1 for r in results if not r["ok"])
    print(f"\n[SMOKE] DONE — {passed} passed, {failed} failed")
    sys.exit(1 if failed else 0)


asyncio.run(main())
