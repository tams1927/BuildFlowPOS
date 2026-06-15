"""
Phase UM-1 — screenshot capture for the user manuals.
Drives the live app (https://localhost:7232) with Playwright/Chromium and
captures real UI screenshots for the SuperAdmin, TenantAdmin and Cashier manuals.

Credentials used (Development demo data):
  SuperAdmin : superadmin / SuperAdmin123!
  TenantAdmin: owner / Owner123!   (tenant "Hardware Supply Co.")
"""
import asyncio, json, os
from playwright.async_api import async_playwright

BASE = "https://localhost:7232"
ASSETS = os.path.join(os.path.dirname(__file__), "..", "_assets")
os.makedirs(ASSETS, exist_ok=True)

VIEWPORT = {"width": 1440, "height": 900}

# (key, relative_url, full_page)  — this app requires explicit /Index actions
SUPER = [
    ("super_dashboard",        "/Home/Index",            False),
    ("super_tenants",          "/Tenants/Index",         False),
    ("super_create_tenant",    "/Tenants/Create",        True),
    ("super_tenant_details",   "/Tenants/Details/9",     True),
    ("super_db_diagnostics",   "/Tenants/DatabaseInfo/9",True),
    ("super_plans",            "/SubscriptionPlans/Index",False),
    ("super_users",            "/Users/Index",           False),
    ("super_roles",            "/Roles/Index",           False),
    ("super_audit",            "/AuditTrail/Index",      False),
]

OWNER = [
    ("owner_dashboard",        "/Home/Index",            False),
    ("owner_settings",         "/Settings/Index",        True),
    ("owner_branches",         "/Branches/Index",        False),
    ("owner_userbranches",     "/UserBranches/Index",    False),
    ("owner_categories",       "/Categories/Index",      False),
    ("owner_units",            "/Units/Index",           False),
    ("owner_products",         "/Products/Index",        False),
    ("owner_inventory",        "/Inventory/Index",       False),
    ("owner_suppliers",        "/Suppliers/Index",       False),
    ("owner_customers",        "/Customers/Index",       False),
    ("owner_purchaseorders",   "/PurchaseOrders/Index",  False),
    ("owner_stockin",          "/StockIn/Index",         False),
    ("owner_stockadjustment",  "/StockAdjustment/Index", False),
    ("owner_branchtransfers",  "/BranchTransfers/Index", False),
    ("owner_quotations",       "/Quotations/Index",      False),
    ("owner_deliveryreceipts", "/DeliveryReceipts/Index",False),
    ("owner_pos",              "/POS/Index",             False),
    ("owner_sales",            "/Sales/Index",           False),
    ("owner_collections",      "/CustomerCollections/Index", False),
    ("owner_supplierpayments", "/SupplierPayments/Index",False),
    ("owner_expenses",         "/Expenses/Index",        False),
    ("owner_reports",          "/Reports/Index",         True),
    ("owner_inv_intel",        "/Reports/FastMovingItems", True),
    ("owner_inv_valuation",    "/Reports/InventoryValuation", True),
    ("owner_import",           "/Import/Index",          False),
    ("owner_audit",            "/AuditTrail/Index",      False),
    # Cashier-guide shots (same UI, captured under the tenant session)
    ("cashier_pos",            "/POS/Index",             False),
    ("cashier_receipt",        "/Sales/Receipt/13",      True),
    ("cashier_saleshistory",   "/Sales/Index",           False),
]

results = []

async def login(page, user, pwd):
    await page.goto(f"{BASE}/Account/Login", wait_until="domcontentloaded")
    await page.fill("#Username", user)
    await page.fill("#Password", pwd)
    await page.click("button[type=submit]")
    await page.wait_for_load_state("domcontentloaded")
    await page.wait_for_timeout(1200)

async def shoot(page, key, url, full):
    try:
        resp = await page.goto(f"{BASE}{url}", wait_until="domcontentloaded", timeout=30000)
        status = resp.status if resp else 0
        try:
            await page.wait_for_load_state("networkidle", timeout=6000)
        except Exception:
            pass
        await page.wait_for_timeout(1400)
        final = page.url
        on_login = "/Account/Login" in final
        ok = (status < 400) and (not on_login)
        out = os.path.join(ASSETS, key + ".png")
        await page.screenshot(path=out, full_page=full)
        note = "" if ok else (f"status {status}" + (" / login redirect" if on_login else ""))
        results.append({"key": key, "url": url, "final": final, "status": status, "ok": ok, "note": note})
        print(f"[CAP] {'OK  ' if ok else 'WARN'} {key:24s} <- {url}  (HTTP {status})")
    except Exception as e:
        results.append({"key": key, "url": url, "final": "", "ok": False, "note": str(e)[:200]})
        print(f"[CAP] FAIL {key:24s} <- {url}  :: {e}")

async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch()
        # Login screenshot (logged out, fresh context)
        ctx0 = await browser.new_context(viewport=VIEWPORT, device_scale_factor=2, ignore_https_errors=True)
        pg0 = await ctx0.new_page()
        await pg0.goto(f"{BASE}/Account/Login", wait_until="domcontentloaded")
        await pg0.fill("#Username", "owner")  # show a filled username; password stays masked/empty
        await pg0.wait_for_timeout(600)
        await pg0.screenshot(path=os.path.join(ASSETS, "login.png"))
        results.append({"key": "login", "url": "/Account/Login", "final": pg0.url, "ok": True, "note": ""})
        print("[CAP] OK  login")
        await ctx0.close()

        # SuperAdmin session
        ctxs = await browser.new_context(viewport=VIEWPORT, device_scale_factor=2, ignore_https_errors=True)
        pgs = await ctxs.new_page()
        await login(pgs, "superadmin", "SuperAdmin123!")
        for key, url, full in SUPER:
            await shoot(pgs, key, url, full)
        await ctxs.close()

        # TenantAdmin (owner) session — also covers cashier-guide UI
        ctxo = await browser.new_context(viewport=VIEWPORT, device_scale_factor=2, ignore_https_errors=True)
        pgo = await ctxo.new_page()
        await login(pgo, "owner", "Owner123!")
        # branch selector (open the dropdown for the figure)
        await pgo.goto(f"{BASE}/Home/Index", wait_until="domcontentloaded")
        await pgo.wait_for_timeout(1000)
        try:
            await pgo.click("[title='Switch Branch']", timeout=3000)
            await pgo.wait_for_timeout(600)
        except Exception:
            pass
        await pgo.screenshot(path=os.path.join(ASSETS, "cashier_branch.png"))
        results.append({"key": "cashier_branch", "url": "/Home/Index (branch dropdown)", "final": pgo.url, "ok": True, "note": ""})
        print("[CAP] OK  cashier_branch")
        for key, url, full in OWNER:
            await shoot(pgo, key, url, full)
        await ctxo.close()

        await browser.close()

    with open(os.path.join(ASSETS, "capture_results.json"), "w", encoding="utf-8") as f:
        json.dump(results, f, indent=2)
    ok = sum(1 for r in results if r["ok"])
    print(f"\n[CAP] DONE — {ok}/{len(results)} screenshots OK. Results: _assets/capture_results.json")

asyncio.run(main())
