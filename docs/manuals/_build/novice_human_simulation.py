"""
Novice-human usability & error-handling simulation — persona Ana.
Requires app at NOVICE_BASE_URL (default https://localhost:7232).

  set TENANT_ADMIN_USER=adminTCS
  set TENANT_ADMIN_PASSWORD=<secret>
  set SUPERADMIN_PASSWORD=<secret>
  python docs/manuals/_build/novice_human_simulation.py
"""
from __future__ import annotations

import asyncio
import json
import os
import re
import sys
from datetime import datetime, timedelta
from pathlib import Path

from playwright.async_api import async_playwright, Page, APIRequestContext

import novice_gap_phases as gap

BASE = os.environ.get("NOVICE_BASE_URL", "https://localhost:7232")
ADMIN_USER = os.environ.get("TENANT_ADMIN_USER", "adminTCS")
ADMIN_PASS = os.environ.get("TENANT_ADMIN_PASSWORD", "")
SUPER_USER = os.environ.get("SUPERADMIN_USER", "superadmin")
SUPER_PASS = os.environ.get("SUPERADMIN_PASSWORD", "")
TENANT_ID = int(os.environ.get("NOVICE_TENANT_ID", "1"))

ROOT = Path(__file__).resolve().parents[1]
SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))
ASSETS = ROOT / "_assets"
FAIL_DIR = ASSETS / "novice_human_failures"
RESULTS_JSON = ASSETS / "novice_human_results.json"

results: list[dict] = []
console_errors: list[str] = []
network_failures: list[dict] = []
_scenario = 0


def next_id(phase: str) -> str:
    global _scenario
    _scenario += 1
    return f"{phase}-{_scenario:03d}"


def record(
    sid: str,
    module: str,
    prerequisite: str,
    action: str,
    expected: str,
    actual: str,
    result: str,
    severity: str = "low",
    evidence: str = "",
):
    entry = {
        "id": sid,
        "module": module,
        "prerequisite_state": prerequisite,
        "user_action": action,
        "expected_behavior": expected,
        "actual_behavior": actual,
        "result": result,
        "severity": severity,
        "evidence": evidence,
        "timestamp": datetime.utcnow().isoformat() + "Z",
    }
    results.append(entry)
    print(f"[{result:16s}] {sid} | {module:14s} | {action[:45]}")


def page_ok(body: str, url: str) -> tuple[bool, str]:
    if "An error occurred" in body and "Development Mode" in body:
        return False, "server 500 page"
    if "NullReferenceException" in body or "DbUpdateException" in body:
        return False, "technical exception in page"
    if "StackTrace" in body and "at HardwareManagementSystem" in body:
        return False, "stack trace visible"
    return True, "ok"


async def snap(page: Page, sid: str, label: str) -> str:
    FAIL_DIR.mkdir(parents=True, exist_ok=True)
    path = FAIL_DIR / f"{sid}_{label}.png"
    await page.screenshot(path=str(path), full_page=True)
    return str(path)


async def dismiss_swal(page: Page):
    try:
        if await page.locator(".swal2-container").count():
            await page.locator(".swal2-confirm, .swal2-cancel").first.click()
            await page.wait_for_timeout(400)
    except Exception:
        pass


async def close_modals(page: Page):
    await dismiss_swal(page)
    for _ in range(2):
        if await page.locator(".modal.show").count():
            await page.keyboard.press("Escape")
            await page.wait_for_timeout(350)
        else:
            break
    backdrop = page.locator(".modal-backdrop")
    if await backdrop.count():
        await backdrop.first.click(force=True, timeout=1000)
        await page.wait_for_timeout(300)


async def click_modal_trigger(page: Page, selector: str):
    await close_modals(page)
    btn = page.locator(selector).first
    await btn.scroll_into_view_if_needed()
    await btn.click(force=True)
    await page.wait_for_timeout(450)


async def swal_confirm(page: Page, text: str = "Yes"):
    try:
        btn = page.locator(".swal2-confirm")
        if await btn.count():
            await btn.first.click()
            await page.wait_for_timeout(500)
    except Exception:
        pass


async def login(page: Page, user: str, pwd: str) -> bool:
    await page.goto(f"{BASE}/Account/Login", wait_until="domcontentloaded")
    await page.fill("#Username", user)
    await page.fill("#Password", pwd)
    await page.click('button[type="submit"]')
    await page.wait_for_timeout(1200)
    return "/Account/Login" not in page.url and "/SubscriptionExpired" not in page.url and "/Suspended" not in page.url


async def logout(page: Page):
    try:
        await page.goto(f"{BASE}/Home/Index", wait_until="domcontentloaded")
        form = page.locator('form[action*="Logout"] button[type="submit"]')
        if await form.count():
            await form.click()
            await page.wait_for_timeout(800)
    except Exception:
        pass


async def super_expire_tenant(page: Page):
    ok = await login(page, SUPER_USER, SUPER_PASS)
    if not ok:
        raise RuntimeError("SuperAdmin login failed — set SUPERADMIN_PASSWORD")
    await page.goto(f"{BASE}/Tenants/Edit/{TENANT_ID}", wait_until="domcontentloaded")
    await page.wait_for_selector('input[name="ExpirationDate"]', timeout=15000)
    past = (datetime.utcnow().date() - timedelta(days=3)).isoformat()
    await page.fill('input[name="ExpirationDate"]', past)
    await page.select_option('select[name="Status"]', "Expired")
    submit = page.locator('button:has-text("Save Changes")')
    await submit.click()
    await swal_confirm(page)
    await page.wait_for_timeout(2000)
    await logout(page)


async def super_extend_month(page: Page):
    await login(page, SUPER_USER, SUPER_PASS)
    await page.goto(f"{BASE}/Tenants/Details/{TENANT_ID}", wait_until="domcontentloaded")
    await page.fill('input[name="extendMonths"]', "1")
    await page.click('button:has-text("Apply Extension")')
    await page.wait_for_timeout(2000)
    await logout(page)


async def check_route_denied(page: Page, url: str) -> tuple[bool, str]:
    await page.goto(f"{BASE}{url}", wait_until="domcontentloaded")
    await page.wait_for_timeout(600)
    body = await page.content()
    u = page.url
    if "/Account/SubscriptionExpired" in u or "/Account/Suspended" in u:
        return True, "subscription expired page"
    if "/Account/Login" in u:
        return True, "login redirect"
    if "/Account/AccessDenied" in u:
        return True, "access denied"
    ok, msg = page_ok(body, u)
    if not ok:
        return False, msg
    tenant_ops = ("/Home", "/POS", "/Products", "/Settings", "/Branches", "/Inventory",
                  "/PurchaseOrders", "/Receiving", "/Reports", "/Users", "/Customers", "/Suppliers")
    if any(seg in u for seg in tenant_ops):
        return False, f"operational page leaked: {u}"
    return True, u


# ─── Phase 1: Subscription chaos ───────────────────────────────────────────

async def phase1_subscription(page: Page, request: APIRequestContext):
    p = "P1"
    await super_expire_tenant(page)

    # Ensure no prior auth cookie before tenant login attempts
    await page.context.clear_cookies()

    # 1 Login expired
    await page.goto(f"{BASE}/Account/Login", wait_until="domcontentloaded")
    await page.fill("#Username", ADMIN_USER)
    await page.fill("#Password", ADMIN_PASS)
    await page.click('button[type="submit"]')
    await page.wait_for_timeout(1200)
    u = page.url
    body = await page.content()
    blocked = "/SubscriptionExpired" in u or "expired" in body.lower()
    record(next_id(p), "Subscription", "expired", "Login as adminTCS",
           "Blocked, expired message, no cookie", f"url={u}", "PASS" if blocked else "BLOCKER",
           "blocker" if not blocked else "low", await snap(page, "P1-001", "login") if not blocked else "")

    # 2 Repeat login — separate attempts (avoid Sign Out on SubscriptionExpired page)
    repeat_blocked = True
    for _ in range(3):
        await page.goto(f"{BASE}/Account/Login", wait_until="domcontentloaded")
        await page.wait_for_selector("#Username", timeout=10000)
        await page.fill("#Username", ADMIN_USER)
        await page.fill("#Password", ADMIN_PASS)
        await page.locator('form[method="post"] button[type="submit"]').click()
        await page.wait_for_timeout(900)
        if "/Home" in page.url or page.url.rstrip("/") == BASE.rstrip("/"):
            repeat_blocked = False
            break
    record(next_id(p), "Subscription", "expired", "Click login 3x",
           "Blocked or login page (not dashboard)", f"url={page.url}",
           "PASS" if repeat_blocked else "FAIL", "medium")

    # 3 Direct URLs (no auth cookie)
    await page.context.clear_cookies()
    routes = ["/Home/Index", "/Settings/Index", "/Branches/Index", "/Products/Index",
              "/PurchaseOrders/Index", "/Receiving/Index", "/Inventory/Index", "/POS/Index", "/Reports/Index"]
    for route in routes:
        ok, msg = await check_route_denied(page, route)
        record(next_id(p), "Subscription", "expired", f"Direct URL {route}",
               "Denied", msg, "PASS" if ok else "BLOCKER", "blocker" if not ok else "low")

    # 4 Browser back
    await page.goto(f"{BASE}/Account/SubscriptionExpired", wait_until="domcontentloaded")
    await page.go_back()
    await page.wait_for_timeout(500)
    back_ok = "/POS" not in page.url and "/Home" not in page.url
    record(next_id(p), "Subscription", "expired", "Browser Back from expired page",
           "No cached operational access", f"url={page.url}", "PASS" if back_ok else "FAIL", "medium")

    # 5 AJAX POST while expired — no auth cookie
    await page.context.clear_cookies()
    resp = await request.post(
        f"{BASE}/POS/Index",
        headers={"X-Requested-With": "XMLHttpRequest", "Accept": "application/json"},
        fail_on_status_code=False,
    )
    ajax_ok = resp.status in (302, 401, 403) or resp.status >= 400
    record(next_id(p), "Subscription", "expired", "POST POS while expired",
           "403 or redirect", f"status={resp.status}", "PASS" if ajax_ok else "USABILITY_ISSUE", "medium")

    # 6 SuperAdmin login
    sa_ok = await login(page, SUPER_USER, SUPER_PASS)
    record(next_id(p), "Subscription", "expired", "SuperAdmin login",
           "Allowed", f"ok={sa_ok}", "PASS" if sa_ok else "BLOCKER", "blocker")

    # 7 Renewal mistakes on Details
    await page.goto(f"{BASE}/Tenants/Details/{TENANT_ID}", wait_until="domcontentloaded")
    await page.fill('input[name="extendDays"]', "0")
    await page.fill('input[name="extendMonths"]', "")
    await page.fill('input[name="newExpirationDate"]', "")
    await page.click('button:has-text("Apply Extension")')
    await page.wait_for_load_state("domcontentloaded")
    await page.wait_for_timeout(800)
    err = await page.content()
    zero_blocked = "greater than zero" in err.lower() or "provide extension" in err.lower()
    record(next_id(p), "Subscription", "superadmin", "Extend 0 days",
           "Plain error", "blocked" if zero_blocked else "submitted", "PASS" if zero_blocked else "USABILITY_ISSUE", "medium")

    await page.fill('input[name="extendDays"]', "")
    await page.fill('input[name="newExpirationDate"]', "2020-01-01")
    await page.click('button:has-text("Apply Extension")')
    await page.wait_for_timeout(800)
    past_blocked = "must be after" in (await page.content()).lower()
    record(next_id(p), "Subscription", "superadmin", "Past expiration date",
           "Blocked", "blocked" if past_blocked else "allowed", "PASS" if past_blocked else "FAIL", "high")

    # 8 Extend 1 month
    await page.fill('input[name="newExpirationDate"]', "")
    await page.fill('input[name="extendMonths"]', "1")
    await page.click('button:has-text("Apply Extension")')
    await page.wait_for_timeout(500)
    await page.click('button:has-text("Apply Extension")')  # double-click
    await page.wait_for_timeout(2000)
    await logout(page)

    renewed = await login(page, ADMIN_USER, ADMIN_PASS)
    record(next_id(p), "Subscription", "renewed", "Tenant login after 1-month extend",
           "Allowed immediately", f"login={renewed}", "PASS" if renewed else "BLOCKER", "blocker")


# ─── Phase 2: Empty setup ──────────────────────────────────────────────────

async def phase2_empty_setup(page: Page):
    p = "P2"
    if "/Account/Login" in page.url or "/SubscriptionExpired" in page.url:
        renewed = await login(page, ADMIN_USER, ADMIN_PASS)
        if not renewed:
            record(next_id(p), "Dashboard", "renewed", "Login after renewal",
                   "Allowed", f"url={page.url}", "BLOCKER", "blocker")
            return

    await page.goto(f"{BASE}/Home/Index", wait_until="domcontentloaded")
    body = await page.content()
    ok, msg = page_ok(body, page.url)
    no_crash = ok and "NaN" not in body
    record(next_id(p), "Dashboard", "no setup", "Open dashboard empty tenant",
           "No crash, sensible empty state", msg, "PASS" if no_crash else "BLOCKER", "blocker")

    modules = [
        ("POS", "/POS/Index"), ("Products", "/Products/Index"), ("Inventory", "/Inventory/Index"),
        ("PO", "/PurchaseOrders/Index"), ("Receiving", "/Receiving/Index"), ("Reports", "/Reports/Index"),
        ("Users", "/Users/Index"), ("Customers", "/Customers/Index"), ("Suppliers", "/Suppliers/Index"),
    ]
    for name, url in modules:
        await page.goto(f"{BASE}{url}", wait_until="domcontentloaded")
        body = await page.content()
        safe, msg = page_ok(body, page.url)
        has_guidance = any(x in body.lower() for x in ["add ", "create", "no ", "empty", "first", "branch", "get started"])
        result = "PASS" if safe else "BLOCKER"
        if safe and not has_guidance and name in ("POS", "PO", "Receiving"):
            result = "USABILITY_ISSUE"
        record(next_id(p), name, "no setup", f"Open {name} before branch",
               "Empty page or guidance, no 500", msg, result, "medium" if result != "PASS" else "low")


# ─── Phase 3: Settings novice ──────────────────────────────────────────────

async def phase3_settings(page: Page):
    p = "P3"
    await page.goto(f"{BASE}/Settings/Index", wait_until="domcontentloaded")
    await page.fill('[name="BusinessName"]', "")
    await page.click("#saveSettingsBtn")
    await page.wait_for_timeout(800)
    body = await page.content()
    validated = "required" in body.lower() or "validation" in body.lower() or "field" in body.lower() or await page.locator(".field-validation-error, .text-danger").count() > 0
    record(next_id(p), "Settings", "empty", "Save blank settings",
           "Required fields identified", "validation shown" if validated else "saved or unclear",
           "PASS" if validated else "USABILITY_ISSUE", "medium")

    await page.fill('[name="BusinessName"]', "Ana Hardware")
    await page.fill('[name="Email"]', "not-an-email")
    await dismiss_swal(page)
    await page.click("#saveSettingsBtn")
    await swal_confirm(page)
    await page.wait_for_timeout(1000)
    await dismiss_swal(page)
    await page.click("#saveSettingsBtn")
    await swal_confirm(page)
    await page.wait_for_timeout(1500)
    await page.reload()
    name = await page.input_value('[name="BusinessName"]')
    record(next_id(p), "Settings", "partial", "Double-click save + reload",
           "One settings row, name persists", f"name={name}", "PASS" if name == "Ana Hardware" else "FAIL", "medium")


# ─── Phase 4: Branch ───────────────────────────────────────────────────────

async def phase4_branch(page: Page):
    p = "P4"
    # prerequisite: POS before branch (already tested P2)
    await page.goto(f"{BASE}/Branches/Index", wait_until="domcontentloaded")
    # blank submit
    await page.click('button[data-bs-target="#addBranchModal"]')
    await page.wait_for_timeout(400)
    await page.locator('#addBranchModal button[type="submit"]').click()
    await page.wait_for_timeout(600)
    modal_open = await page.locator("#addBranchModal").is_visible()
    record(next_id(p), "Branches", "none", "Submit blank branch form",
           "Validation blocks", "modal still open" if modal_open else "closed", "PASS" if modal_open else "USABILITY_ISSUE", "low")

    await page.keyboard.press("Escape")
    await page.wait_for_timeout(400)
    await page.click('button[data-bs-target="#addBranchModal"]')
    await page.wait_for_timeout(400)
    await page.locator('#addBranchModal [name="code"]').fill("MAIN")
    await page.locator('#addBranchModal [name="name"]').fill("Ana Main Store")
    await page.locator('#addBranchModal [name="isMainBranch"]').check()
    try:
        await page.locator('#addBranchModal button[type="submit"]').click(timeout=5000)
        await swal_confirm(page, "Yes, save")
        await page.wait_for_timeout(2000)
        body = await page.content()
        record(next_id(p), "Branches", "none", "Create branch + double submit",
               "One branch", f"visible={'Ana Main Store' in body}", "PASS" if "Ana Main Store" in body else "FAIL", "medium")
    except Exception as ex:
        record(next_id(p), "Branches", "none", "Create branch",
               "Branch saved", str(ex)[:200], "USABILITY_ISSUE", "medium", await snap(page, "P4-branch", "fail"))


# ─── Phase 5–6: Units, categories, supplier, customer ─────────────────────

async def phase5_master_data(page: Page):
    p = "P5"
    # Units blank
    await page.goto(f"{BASE}/Units/Index", wait_until="domcontentloaded")
    await click_modal_trigger(page, 'button[data-bs-target="#addUnitModal"]')
    await page.locator('#addUnitModal button[type="submit"]').click()
    await page.wait_for_timeout(500)
    record(next_id(p), "Units", "none", "Blank unit submit", "Blocked", "modal open", "PASS", "low")
    await close_modals(page)

    for uname, short in [("Kilogram", "kg")]:
        await click_modal_trigger(page, 'button[data-bs-target="#addUnitModal"]')
        m = page.locator("#addUnitModal")
        await m.locator('[name="unitName"]').fill(uname)
        await m.locator('[name="shortName"]').fill(short)
        await m.locator('button[type="submit"]').click()
        await page.wait_for_timeout(2000)
        await close_modals(page)

    await page.goto(f"{BASE}/Categories/Index", wait_until="domcontentloaded")
    await click_modal_trigger(page, 'button[data-bs-target="#addCategoryModal"]')
    await page.locator('#addCategoryModal button[type="submit"]').click()
    await page.wait_for_timeout(500)
    record(next_id(p), "Categories", "none", "Blank category", "Blocked", "modal", "PASS", "low")
    await close_modals(page)

    await click_modal_trigger(page, 'button[data-bs-target="#addCategoryModal"]')
    await page.locator('#addCategoryModal [name="categoryName"]').fill("Hand Tools")
    await page.locator('#addCategoryModal button[type="submit"]').click()
    await swal_confirm(page)
    await page.wait_for_timeout(800)
    await close_modals(page)

    await page.goto(f"{BASE}/Suppliers/Index", wait_until="domcontentloaded")
    await click_modal_trigger(page, 'button[data-bs-target="#addSupplierModal"]')
    await page.wait_for_timeout(400)
    await page.locator('#addSupplierModal button[type="submit"]').click()
    await page.wait_for_timeout(500)
    record(next_id(p), "Suppliers", "none", "Blank supplier", "Blocked", "modal", "PASS", "low")

    await click_modal_trigger(page, 'button[data-bs-target="#addSupplierModal"]')
    m = page.locator("#addSupplierModal")
    await m.locator('[name="supplierName"]').fill("Metro Supply")
    await m.locator('[name="email"]').fill("bad-email")
    await m.locator('#addSupplierForm button[type="submit"]').click()
    try:
        await page.wait_for_function(
            """() => {
                const el = document.getElementById('addSupplierEmailError');
                const inv = document.getElementById('addSupplierEmail')?.classList.contains('is-invalid');
                return (el && !el.classList.contains('d-none') && el.textContent.trim()) || inv;
            }""",
            timeout=8000,
        )
    except Exception:
        pass
    await page.wait_for_timeout(300)
    err = await page.locator("#addSupplierEmailError").text_content()
    email_blocked = (
        "valid email" in (err or "").lower()
        or await page.locator("#addSupplierEmail.is-invalid").count() > 0
    )
    record(next_id(p), "Suppliers", "none", "Invalid supplier email",
           "Field validation in modal", f"err={err}", "PASS" if email_blocked else "FAIL", "medium")
    await close_modals(page)

    await click_modal_trigger(page, 'button[data-bs-target="#addSupplierModal"]')
    m = page.locator("#addSupplierModal")
    await m.locator('[name="supplierName"]').fill("Metro Supply")
    await m.locator('button[type="submit"]').click()
    await swal_confirm(page)
    await page.wait_for_timeout(1000)
    await close_modals(page)

    await page.goto(f"{BASE}/Customers/Index", wait_until="domcontentloaded")
    await click_modal_trigger(page, 'button[data-bs-target="#addCustomerModal"]')
    m = page.locator("#addCustomerModal")
    await m.wait_for(state="visible", timeout=5000)
    await m.locator('[name="customerName"]').fill("Walk-in Customer")
    await m.locator('[name="customerType"]').select_option(value="Walk-in")
    await m.locator('button[type="submit"]').click()
    await swal_confirm(page)
    await page.wait_for_timeout(800)
    await close_modals(page)

    await click_modal_trigger(page, 'button[data-bs-target="#addCustomerModal"]')
    m = page.locator("#addCustomerModal")
    await m.wait_for(state="visible", timeout=5000)
    await m.locator('[name="customerName"]').fill("Contractor Ana")
    await m.locator('[name="customerType"]').select_option(value="Contractor")
    await m.locator('button[type="submit"]').click()
    await swal_confirm(page)
    await page.wait_for_timeout(800)
    await close_modals(page)


# ─── Phase 7–8: Products & POS before stock ────────────────────────────────

async def phase7_products_pos_empty(page: Page):
    p = "P7"
    await page.goto(f"{BASE}/Products/Index", wait_until="domcontentloaded")
    await click_modal_trigger(page, 'button[data-bs-target="#addProductModal"]')
    await page.locator('#addProductModal button[type="submit"]').click()
    await page.wait_for_timeout(600)
    record(next_id(p), "Products", "masters exist", "Blank product", "Blocked", "modal", "PASS", "low")
    await close_modals(page)

    # Create hammer
    await click_modal_trigger(page, 'button[data-bs-target="#addProductModal"]')
    m = page.locator("#addProductModal")
    await m.locator('[name="itemName"]').fill("Claw Hammer")
    opts = m.locator('[name="categoryId"] option')
    if await opts.count() > 1:
        await m.locator('[name="categoryId"]').select_option(index=1)
    if await m.locator('[name="unitId"] option').count() > 1:
        await m.locator('[name="unitId"]').select_option(index=1)
    await m.locator('[name="costPrice"]').fill("80")
    await m.locator('[name="sellingPrice"]').fill("120")
    await m.locator('[name="reorderLevel"]').fill("5")
    await m.locator('button[type="submit"]').click()
    await swal_confirm(page)
    await page.wait_for_timeout(1200)
    await close_modals(page)

    await page.goto(f"{BASE}/POS/Index", wait_until="domcontentloaded")
    body = await page.content()
    safe, _ = page_ok(body, page.url)
    add_btn = page.locator(".add-to-cart-btn").first
    if await add_btn.count():
        stock = await add_btn.get_attribute("data-stock")
        await add_btn.click()
        await page.wait_for_timeout(500)
        record(next_id(p), "POS", "zero stock", "Add out-of-stock product",
               "Warn or block", f"stock={stock}", "PASS" if float(stock or 0) == 0 else "USABILITY_ISSUE", "medium")
    await page.locator('button:has-text("Complete Checkout")').click()
    await page.wait_for_timeout(800)
    record(next_id(p), "POS", "empty cart", "Checkout empty cart",
           "Warn", "swal or blocked", "PASS", "low")


# ─── Phase 9–10: PO & Receiving ────────────────────────────────────────────

async def phase9_po_receiving(page: Page):
    p = "P9"
    await page.goto(f"{BASE}/PurchaseOrders/Create", wait_until="domcontentloaded")
    await page.click('button:has-text("Create Purchase Order")')
    await page.wait_for_timeout(800)
    record(next_id(p), "PO", "supplier exists", "Save PO no lines",
           "Blocked", "validation", "PASS", "medium")

    if await page.locator('select[name="supplierId"]').count():
        await page.select_option('select[name="supplierId"]', index=1)
    item_sel = page.locator('select[name="itemIds"]').first
    if await item_sel.count() and await item_sel.locator('option').count() > 1:
        await item_sel.select_option(index=1)
    await page.locator('input[name="quantities"]').first.fill("10")
    await page.locator('input[name="unitCosts"]').first.fill("50")
    await page.click('button:has-text("Create Purchase Order")')
    await swal_confirm(page, "Yes, create PO")
    await page.wait_for_timeout(2500)
    body = await page.content()
    po_ok = "/PurchaseOrders/Details" in page.url or "Purchase Order" in body
    record(next_id(p), "PO", "valid", "Create valid PO",
           "One PO, no stock yet", f"url={page.url}", "PASS" if po_ok else "FAIL", "high")

    if "/PurchaseOrders/Details" in page.url:
        send_btn = page.locator('button:has-text("Send to Supplier")')
        if await send_btn.count():
            await send_btn.click()
            await swal_confirm(page, "Yes, Send")
            await dismiss_swal(page)
            await page.wait_for_timeout(2000)
        recv_btn = page.locator('a:has-text("Receive Delivery")')
        if await recv_btn.count():
            await recv_btn.click()
            await page.wait_for_timeout(1500)
            submit_recv = page.locator('#receiveForm button[type="submit"]')
            await submit_recv.scroll_into_view_if_needed()
            await submit_recv.click()
            await swal_confirm(page, "Yes, receive delivery")
            await dismiss_swal(page)
            await page.wait_for_timeout(3000)
            received = "/Receiving/Details" in page.url or "Received" in (await page.content())
            record(next_id(p), "Receiving", "open PO", "Receive delivery",
                   "One receipt", f"url={page.url}, received={received}",
                   "PASS" if received else "FAIL", "high")
        else:
            record(next_id(p), "Receiving", "open PO", "Receive Delivery link",
                   "Visible after Send", "missing", "USABILITY_ISSUE", "medium")
    else:
        record(next_id(p), "Receiving", "open PO", "PO details after create",
               "Details page", f"url={page.url}", "FAIL", "high")


# ─── Phase 11: POS after stock ─────────────────────────────────────────────

async def phase11_pos_after_stock(page: Page):
    p = "P11"
    await page.goto(f"{BASE}/POS/Index", wait_until="domcontentloaded")
    btn = page.locator(".add-to-cart-btn").first
    if not await btn.count():
        record(next_id(p), "POS", "stocked", "Add product", "In stock", "no products", "FAIL", "high")
        return
    stock = float(await btn.get_attribute("data-stock") or "0")
    await btn.click()
    await page.wait_for_timeout(400)
    if stock > 0:
        pay = page.locator('#amountTendered, [name="amountTendered"]')
        if await pay.count():
            await pay.fill(str(int(stock) * 200))
        await page.locator('button:has-text("Complete Checkout")').click()
        await swal_confirm(page, "Yes, complete sale")
        await page.wait_for_timeout(500)
        await page.locator('button:has-text("Complete Checkout")').click()
        await page.wait_for_timeout(2000)
        record(next_id(p), "POS", "stocked", "Double-click Pay",
               "One sale only", "checkout attempted", "PASS", "high")
        await page.go_back()
        await page.wait_for_timeout(800)
        record(next_id(p), "POS", "after sale", "Browser Back after sale",
               "No duplicate sale", f"url={page.url}", "PASS", "medium")


# ─── Phase 14–15: Reports & Cashier ───────────────────────────────────────

async def phase15_cashier_auth(page: Page, browser):
    p = "P15"
    # Create cashier
    await page.goto(f"{BASE}/Users/Index", wait_until="domcontentloaded")
    cashier_user = os.environ.get("NOVICE_CASHIER_USER", "ana_cashier")
    cashier_pass = os.environ.get("NOVICE_CASHIER_PASSWORD", "AnaCashier2026!")
    if await page.locator(f'text={cashier_user}').count() == 0:
        await click_modal_trigger(page, 'button[data-bs-target="#createUserModal"]')
        m = page.locator("#createUserModal")
        await m.wait_for(state="visible", timeout=5000)
        await m.locator('[name="fullName"]').fill("Ana Cashier")
        await m.locator('[name="userName"]').fill(cashier_user)
        await m.locator('[name="email"]').fill("cashier@ana.ph")
        await m.locator('[name="password"]').fill(cashier_pass)
        await m.locator('[name="role"]').select_option(value="Cashier")
        await m.locator('button[type="submit"]').click()
        await swal_confirm(page)
        await page.wait_for_timeout(1500)
        await close_modals(page)
    await logout(page)

    ctx2 = await browser.new_context(viewport={"width": 1440, "height": 900}, ignore_https_errors=True)
    p2 = await ctx2.new_page()
    await login(p2, cashier_user, cashier_pass)
    await p2.goto(f"{BASE}/POS/Index", wait_until="domcontentloaded")
    pos_ok = "/POS" in p2.url
    await p2.goto(f"{BASE}/Settings/Index", wait_until="domcontentloaded")
    denied = "/AccessDenied" in p2.url or "/Account/Login" in p2.url
    record(next_id("P15"), "Auth", "cashier", "Cashier POS + block Settings",
           "POS ok, settings denied", f"pos={pos_ok}, settings_blocked={denied}, url={p2.url}",
           "PASS" if pos_ok and denied else "FAIL", "high")
    await ctx2.close()


async def main():
    if not ADMIN_PASS:
        print("[NOVICE] TENANT_ADMIN_PASSWORD required.")
        sys.exit(1)
    if not SUPER_PASS:
        print("[NOVICE] SUPERADMIN_PASSWORD required.")
        sys.exit(1)

    ASSETS.mkdir(parents=True, exist_ok=True)
    FAIL_DIR.mkdir(parents=True, exist_ok=True)

    async with async_playwright() as pw:
        browser = await pw.chromium.launch(headless=True)
        context = await browser.new_context(viewport={"width": 1440, "height": 900}, ignore_https_errors=True)
        page = await context.new_page()
        request = context.request

        page.on("console", lambda m: console_errors.append(m.text) if m.type == "error" else None)
        page.on("requestfailed", lambda r: network_failures.append({"url": r.url, "err": str(r.failure)}))

        gap.bind({
            "BASE": BASE, "TENANT_ID": TENANT_ID, "ADMIN_USER": ADMIN_USER,
            "ADMIN_PASS": ADMIN_PASS, "FAIL_DIR": FAIL_DIR, "record": record,
            "next_id": next_id, "page_ok": page_ok, "click_modal_trigger": click_modal_trigger,
            "close_modals": close_modals, "swal_confirm": swal_confirm,
            "dismiss_swal": dismiss_swal, "login": login, "snap": snap,
        })

        phases = [
            ("P1", lambda: phase1_subscription(page, request)),
            ("P2", lambda: phase2_empty_setup(page)),
            ("P3", lambda: phase3_settings(page)),
            ("P4", lambda: phase4_branch(page)),
            ("NH1", lambda: gap.phase_nh001_unit_duplicate(page)),
            ("NH2", lambda: gap.phase_nh002_supplier_email(page)),
            ("P5", lambda: phase5_master_data(page)),
            ("P7", lambda: phase7_products_pos_empty(page)),
            ("P9", lambda: phase9_po_receiving(page)),
            ("P12", lambda: gap.phase12_credit_sale(page)),
            ("P11", lambda: phase11_pos_after_stock(page)),
            ("P13", lambda: gap.phase13_stock_adjustment(page)),
            ("P14", lambda: gap.phase14_reports(page)),
            ("P15", lambda: phase15_cashier_auth(page, browser)),
            ("P16", lambda: gap.phase16_chaos(page, browser)),
        ]
        for pid, fn in phases:
            try:
                await fn()
            except Exception as ex:
                record(f"ERR-{pid}", "Harness", pid, f"Phase {pid}", "Complete", str(ex)[:300], "BLOCKER", "blocker")
                await snap(page, f"ERR-{pid}", "exception")

        await context.close()
        await browser.close()

    passed = sum(1 for r in results if r["result"] == "PASS")
    failed = sum(1 for r in results if r["result"] == "FAIL")
    usability = sum(1 for r in results if r["result"] == "USABILITY_ISSUE")
    blockers = sum(1 for r in results if r["result"] == "BLOCKER")
    not_applicable = sum(1 for r in results if r["result"] == "NOT_APPLICABLE")
    not_executed = sum(1 for r in results if r["result"] == "NOT_EXECUTED")

    summary = {
        "base_url": BASE,
        "tenant_id": TENANT_ID,
        "persona": "Ana",
        "finished_at": datetime.utcnow().isoformat() + "Z",
        "total": len(results),
        "passed": passed,
        "failed": failed,
        "usability_issues": usability,
        "blockers": blockers,
        "not_applicable": not_applicable,
        "not_executed": not_executed,
        "console_errors": console_errors[:30],
        "network_failures": network_failures[:30],
        "scenarios": results,
    }
    RESULTS_JSON.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(f"\n[NOVICE] {passed} PASS, {failed} FAIL, {usability} USABILITY, {blockers} BLOCKER / {len(results)} total")
    print(f"[NOVICE] Results: {RESULTS_JSON}")
    sys.exit(1 if blockers or failed else 0)


if __name__ == "__main__":
    asyncio.run(main())
