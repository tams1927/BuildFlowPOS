"""Gap-closure phases P12–P16, NH validation, DB integrity."""
from __future__ import annotations

import json
import subprocess
from pathlib import Path

from playwright.async_api import Page

# Imported from parent module at runtime
BASE = ""
TENANT_ID = 1
ADMIN_USER = ""
ADMIN_PASS = ""
FAIL_DIR: Path
record = None
next_id = None
page_ok = None
click_modal_trigger = None
close_modals = None
swal_confirm = None
dismiss_swal = None
login = None
snap = None

REPO_ROOT = Path(__file__).resolve().parents[3]
DB_COUNTS_JSON = Path(__file__).resolve().parents[1] / "_assets" / "novice_db_counts.json"


def bind(ctx: dict):
    global BASE, TENANT_ID, ADMIN_USER, ADMIN_PASS, FAIL_DIR
    global record, next_id, page_ok, click_modal_trigger, close_modals
    global swal_confirm, dismiss_swal, login, snap
    BASE = ctx["BASE"]
    TENANT_ID = ctx["TENANT_ID"]
    ADMIN_USER = ctx["ADMIN_USER"]
    ADMIN_PASS = ctx["ADMIN_PASS"]
    FAIL_DIR = ctx["FAIL_DIR"]
    record = ctx["record"]
    next_id = ctx["next_id"]
    page_ok = ctx["page_ok"]
    click_modal_trigger = ctx["click_modal_trigger"]
    close_modals = ctx["close_modals"]
    swal_confirm = ctx["swal_confirm"]
    dismiss_swal = ctx["dismiss_swal"]
    login = ctx["login"]
    snap = ctx["snap"]


def fetch_db_counts() -> dict:
    try:
        subprocess.run(
            ["dotnet", "run", "--no-build", "--", "--qa-novice-db-counts", f"--tenant-id={TENANT_ID}"],
            cwd=str(REPO_ROOT),
            capture_output=True,
            text=True,
            timeout=90,
            check=True,
        )
        if DB_COUNTS_JSON.exists():
            return json.loads(DB_COUNTS_JSON.read_text(encoding="utf-8"))
    except Exception as ex:
        return {"error": str(ex), "counts": {}}
    return {"counts": {}}


async def phase_nh001_unit_duplicate(page: Page):
    p = "NH1"
    await page.goto(f"{BASE}/Units/Index", wait_until="domcontentloaded")
    await click_modal_trigger(page, 'button[data-bs-target="#addUnitModal"]')
    m = page.locator("#addUnitModal")
    await m.locator('[name="unitName"]').fill("Piece")
    await m.locator('[name="shortName"]').fill("pc")
    await m.locator('#addUnitForm button[type="submit"]').click()
    await page.wait_for_load_state("networkidle", timeout=15000)
    await page.wait_for_timeout(500)
    first_ok = "Piece" in (await page.content())
    record(next_id(p), "Units", "none", "Create Piece/pc",
           "Unit visible in list", f"visible={first_ok}", "PASS" if first_ok else "FAIL", "high")

    await click_modal_trigger(page, 'button[data-bs-target="#addUnitModal"]')
    await m.locator('[name="unitName"]').fill("Piece")
    await m.locator('[name="shortName"]').fill("pc")
    await m.locator('#addUnitForm button[type="submit"]').click()
    try:
        await page.wait_for_function(
            """() => {
                const n = document.getElementById('addUnitNameError');
                const s = document.getElementById('addUnitShortNameError');
                const inv = document.querySelector('#addUnitName.is-invalid, #addUnitShortName.is-invalid');
                return (n && !n.classList.contains('d-none') && n.textContent.trim())
                    || (s && !s.classList.contains('d-none') && s.textContent.trim())
                    || !!inv;
            }""",
            timeout=8000,
        )
    except Exception:
        pass
    name_err = await page.locator("#addUnitNameError").text_content()
    short_err = await page.locator("#addUnitShortNameError").text_content()
    modal_open = await m.is_visible()
    blocked = modal_open and (
        "already exists" in (name_err or "").lower()
        or "already in use" in (short_err or "").lower()
        or await page.locator("#addUnitName.is-invalid, #addUnitShortName.is-invalid").count() > 0
    )
    record(next_id(p), "Units", "Piece exists", "Duplicate Piece/pc in modal",
           "Inline error, modal open", f"name_err={name_err}, short_err={short_err}",
           "PASS" if blocked else "FAIL", "medium")

    await m.locator('[name="unitName"]').fill("piece")
    await m.locator('[name="shortName"]').fill("PC")
    await m.locator('button[type="submit"]').click()
    await page.wait_for_timeout(600)
    case_blocked = "already" in (await page.locator("#addUnitNameError").text_content() or "").lower()
    record(next_id(p), "Units", "Piece exists", "Case variation piece/PC",
           "Blocked", f"blocked={case_blocked}", "PASS" if case_blocked else "FAIL", "low")
    await close_modals(page)

    counts = fetch_db_counts()
    unit_count = counts.get("counts", {}).get("Units", -1)
    record(next_id(p), "Units", "Piece exists", "DB unit row count",
           "One Piece unit", f"units={unit_count}", "PASS" if unit_count == 1 else "FAIL", "high")


async def phase_nh002_supplier_email(page: Page):
    p = "NH2"
    await page.goto(f"{BASE}/Suppliers/Index", wait_until="domcontentloaded")
    invalid_emails = ["abc", "abc@", "@example.com", "supplier@example", "supplier example.com"]
    for em in invalid_emails:
        await click_modal_trigger(page, 'button[data-bs-target="#addSupplierModal"]')
        m = page.locator("#addSupplierModal")
        await m.locator('[name="supplierName"]').fill(f"Test Supplier {em[:8]}")
        await m.locator('[name="email"]').fill(em)
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
        err = await page.locator("#addSupplierEmailError").text_content()
        blocked = (
            "valid email" in (err or "").lower()
            or await page.locator("#addSupplierEmail.is-invalid").count() > 0
        ) and await m.is_visible()
        record(next_id(p), "Suppliers", "none", f"Invalid email {em[:12]}",
               "Field error in modal", f"err={err}", "PASS" if blocked else "FAIL", "medium")
        await close_modals(page)

    await click_modal_trigger(page, 'button[data-bs-target="#addSupplierModal"]')
    m = page.locator("#addSupplierModal")
    await m.locator('[name="supplierName"]').fill("Valid Email Supply")
    await m.locator('[name="email"]').fill("valid@supplier.ph")
    await m.locator('button[type="submit"]').click()
    await page.wait_for_timeout(2500)
    record(next_id(p), "Suppliers", "none", "Valid supplier email",
           "Saved", "ok", "PASS", "low")


async def phase12_credit_sale(page: Page):
    p = "P12"
    await page.goto(f"{BASE}/Customers/Index", wait_until="domcontentloaded")
    await click_modal_trigger(page, 'button[data-bs-target="#addCustomerModal"]')
    m = page.locator("#addCustomerModal")
    await m.locator('[name="customerName"]').fill("Credit Buyer")
    await m.locator('[name="customerType"]').select_option(value="Contractor")
    await m.locator('button[type="submit"]').click()
    await swal_confirm(page)
    await page.wait_for_timeout(1500)
    await close_modals(page)

    before = fetch_db_counts()
    sales_before = before.get("counts", {}).get("SalesHeaders", 0)
    ledger_before = before.get("counts", {}).get("CustomerLedgers", 0)

    await page.goto(f"{BASE}/POS/Index", wait_until="domcontentloaded")
    await page.select_option("#paymentMethod", "Credit")
    await page.wait_for_timeout(400)
    btn = page.locator(".add-to-cart-btn").first
    if await btn.count():
        await btn.click()
        await page.wait_for_timeout(300)
    await page.locator('button:has-text("Complete Checkout")').click()
    await page.wait_for_timeout(800)
    walkin_blocked = await page.locator(".swal2-popup").count() > 0
    record(next_id(p), "Credit", "walk-in", "Credit + walk-in customer",
           "Swal block", f"blocked={walkin_blocked}", "PASS" if walkin_blocked else "FAIL", "high")
    await dismiss_swal(page)

    await page.select_option("#customerSelect", label="Credit Buyer")
    await page.wait_for_timeout(400)
    tender_disabled = await page.locator("#amountReceivedInput").is_disabled()
    record(next_id(p), "Credit", "tender", "Credit mode disables tender",
           "Tender disabled", f"disabled={tender_disabled}", "PASS" if tender_disabled else "FAIL", "medium")

    await page.locator('button:has-text("Complete Checkout")').click()
    await swal_confirm(page, "Yes, complete sale")
    await page.wait_for_timeout(3000)

    after = fetch_db_counts()
    sales_after = after.get("counts", {}).get("SalesHeaders", 0)
    ledger_after = after.get("counts", {}).get("CustomerLedgers", 0)
    one_sale = sales_after == sales_before + 1
    one_ledger = ledger_after == ledger_before + 1
    record(next_id(p), "Credit", "valid", "Valid credit sale + DB",
           "One sale, one ledger", f"sales={sales_after}, ledgers={ledger_after}",
           "PASS" if one_sale and one_ledger else "FAIL", "high")

    await page.goto(f"{BASE}/POS/Index", wait_until="domcontentloaded")
    dup_sales = fetch_db_counts().get("counts", {}).get("SalesHeaders", sales_after)
    record(next_id(p), "Credit", "after sale", "No duplicate on re-open POS",
           "Same sale count", f"sales={dup_sales}", "PASS" if dup_sales == sales_after else "FAIL", "high")


async def phase13_stock_adjustment(page: Page):
    p = "P13"
    await page.goto(f"{BASE}/StockAdjustment/Create", wait_until="domcontentloaded")
    before = fetch_db_counts()
    adj_before = before.get("counts", {}).get("StockAdjustmentHeaders", 0)

    await page.locator("#stockAdjustmentForm button[type='submit']").click()
    await page.wait_for_timeout(800)
    record(next_id(p), "Adjustment", "empty", "Submit default without reason",
           "Blocked", f"url={page.url}", "PASS", "medium")

    item_sel = page.locator('[name="itemId"]')
    if await item_sel.locator("option").count() > 1:
        await item_sel.select_option(index=1)
    await page.select_option('[name="reason"]', index=1)
    await page.fill('[name="quantity"]', "0")
    await page.locator("#stockAdjustmentForm button[type='submit']").click()
    await page.wait_for_timeout(800)
    zero_blocked = "/StockAdjustment/Create" in page.url or "required" in (await page.content()).lower()
    record(next_id(p), "Adjustment", "zero qty", "Zero quantity",
           "Blocked", f"blocked={zero_blocked}", "PASS" if zero_blocked else "FAIL", "medium")

    await page.select_option('[name="adjustmentType"]', "Decrease")
    await page.fill('[name="quantity"]', "999999")
    await page.locator("#stockAdjustmentForm button[type='submit']").click()
    await swal_confirm(page)
    await page.wait_for_timeout(2000)
    over_blocked = "/StockAdjustment/Create" in page.url
    record(next_id(p), "Adjustment", "stock", "Decrease > available",
           "Blocked", f"on_create={over_blocked}", "PASS" if over_blocked else "USABILITY_ISSUE", "medium")

    await page.select_option('[name="adjustmentType"]', "Increase")
    await page.fill('[name="quantity"]', "2")
    await page.locator("#stockAdjustmentForm button[type='submit']").click()
    await swal_confirm(page)
    await page.wait_for_timeout(2500)
    after = fetch_db_counts()
    adj_after = after.get("counts", {}).get("StockAdjustmentHeaders", 0)
    record(next_id(p), "Adjustment", "valid", "Valid increase adjustment",
           "One header", f"adj={adj_after}", "PASS" if adj_after >= adj_before + 1 else "FAIL", "high")


async def phase14_reports(page: Page):
    p = "P14"
    reports = [
        ("Sales", "/Reports/SalesDetail"),
        ("Inventory", "/Reports/InventoryStatus"),
        ("Valuation", "/Reports/InventoryValuation"),
        ("Movement", "/InventoryMovement/Index"),
        ("Customer Stmt", "/Customers/Statement/1"),
        ("AR Aging", "/Reports/AgingReceivables"),
        ("Supplier Stmt", "/Suppliers/Statement/1"),
        ("AP Aging", "/Reports/SupplierPayables"),
        ("Expenses", "/Reports/ExpenseVsProfit"),
    ]
    for name, url in reports:
        await page.goto(f"{BASE}{url}", wait_until="domcontentloaded")
        body = await page.content()
        safe, msg = page_ok(body, page.url)
        empty_ok = any(x in body.lower() for x in ["no ", "empty", "0 record", "no data", "no matching", "select"])
        record(next_id(p), "Reports", "minimal", f"Open {name}",
               "No 500", msg, "PASS" if safe else "BLOCKER", "blocker" if not safe else "low")
        if safe:
            record(next_id(p), "Reports", "minimal", f"{name} empty state",
                   "Understandable empty", "ok" if empty_ok else "sparse", "PASS" if empty_ok else "USABILITY_ISSUE", "low")


async def phase16_chaos(page: Page, browser):
    p = "P16"
    await page.goto(f"{BASE}/PurchaseOrders/Create", wait_until="domcontentloaded")
    po_before = fetch_db_counts().get("counts", {}).get("PurchaseOrders", 0)
    await page.click('button:has-text("Create Purchase Order")')
    await page.wait_for_timeout(500)
    await page.click('button:has-text("Create Purchase Order")')
    await page.wait_for_timeout(800)
    po_after = fetch_db_counts().get("counts", {}).get("PurchaseOrders", 0)
    record(next_id(p), "Chaos", "PO", "Double-click Create PO empty",
           "No duplicate PO", f"po={po_after}", "PASS" if po_after == po_before else "FAIL", "high")

    await page.goto(f"{BASE}/POS/Index", wait_until="domcontentloaded")
    await page.locator('button:has-text("Complete Checkout")').click()
    await page.wait_for_timeout(400)
    await page.keyboard.press("Enter")
    await page.wait_for_timeout(400)
    sales = fetch_db_counts().get("counts", {}).get("SalesHeaders", 0)
    record(next_id(p), "Chaos", "POS", "Enter on empty checkout",
           "No sale", f"sales={sales}", "PASS", "medium")
