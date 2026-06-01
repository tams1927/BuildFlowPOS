# QA Runner Guide

Two dev-only, automated end-to-end QA runners validate the dedicated-database routing flow without
a browser. They run **only** when launched with an explicit flag **and** in the Development
environment, and they **exit without starting the web server**.

| Flag | Runner | Scope |
|------|--------|-------|
| `--qa-phase50d1` | `Tools/Phase50D1QaRunner.cs` | Pilot core cycle: provision → migrate → route → Items/Inventory/Stock-In/POS/Sales/core Reports → rollback |
| `--qa-phase50d2` | `Tools/Phase50D2QaRunner.cs` | **Full + final** module cutover (extended in Phase 5.0D.4): full operational lifecycle + Branches, Set Main Branch, User-Branch assignment, real Excel imports, audit read, settings, rollback/re-enable |

## How to run

```bash
# from the project directory, Development environment
dotnet run -- --qa-phase50d1
dotnet run -- --qa-phase50d2
```

Filter output to QA lines (PowerShell):

```powershell
dotnet run -- --qa-phase50d2 2>&1 | Select-String -Pattern "\[QA\]" | Out-String
```

> Do **not** truncate the process (e.g. `Select-Object -First N`) — that can kill the runner before
> it writes its report file. Let it finish, then read the report.

## Purpose

- Prove that a routing-enabled tenant performs all operational work against its **dedicated**
  database, that no operational rows leak into the **shared** database, that reports read the
  dedicated data, and that **rollback/re-enable** work.
- Provide a repeatable regression gate before any release or routing change.

## Expected output

- Console: a `[QA] PASS — <step> :: <detail>` line per check and a final summary, e.g.
  `[QA] ==== Phase 5.0D.2 + 5.0D.4 Full/Final Module Cutover ==== OVERALL PASS — 50/50 steps passed.`
- Report files written to:
  - `docs/Phase50D1_FullCycleQAReport.md`
  - `docs/Phase50D2_FullCycleQAReport.md`
- Test data uses the `QA_` prefix; a dedicated DB named `QA_TenantDb_<timestamp>` is created.

## Pass criteria

- **OVERALL PASS** with all steps passed (current baseline: 5.0D.2+5.0D.4 = **50/50**).
- Dedicated DB row counts for every QA entity are **> 0**.
- Shared DB row counts for the same QA operational entities are **0** (platform rows excepted).
- Routing diagnostics show **Dedicated**; rollback shows **Shared**; re-enable shows **Dedicated**.
- Audit row `TENANT_FINAL_CUTOVER_VERIFIED` (5.0D.2/5.0D.4) or `TENANT_RUNTIME_ROUTING_VERIFIED`
  (5.0D.1) is written.

## Failure criteria

- Any `[QA] FAIL` line, or **OVERALL FAIL**.
- A QA operational row found in the shared DB (split-brain leak).
- A QA row missing from the dedicated DB.
- An unexpected exception (logged in the report's Exception section).
- Provisioning/migration/connection step fails.

## Cleanup

Test data is **retained** by default (QA_ prefix) so you can inspect it. To auto-clean (delete the
QA tenant/user/settings/audit and **DROP** the dedicated QA database), set:

```jsonc
// appsettings.Development.json
"QA": { "EnableCleanup": true }
```

Cleanup is dev-only and gated by this flag. Leave it **false/absent** in any shared environment.

## Troubleshooting

| Symptom | Likely cause / fix |
|---------|--------------------|
| "runner is Development-only. Aborting." | Set `ASPNETCORE_ENVIRONMENT=Development`. |
| Report file shows an older run | The process was truncated before writing — re-run without `Select-Object -First`. |
| Provisioning fails (CREATE DATABASE) | SQL login lacks `dbcreator`/`CREATE DATABASE`; or DB name invalid (allow-list: letters/digits/`_`/`-`, starts letter/`_`). |
| Multiple-cascade-path FK error on migrate | Tenant schema FK delete behavior — see Phase 5.0C Hotfix (dual-branch FKs use `Restrict`). |
| Connection test fails | Wrong server/instance in `DefaultConnection`, or `TrustServerCertificate` needed. |
| Routing stays "Shared" | One of the 5 preconditions is false (Mode/Provisioned/Migrated/RoutingEnabled/ConnectionString), or resolver cache not invalidated (it caches 5 min). |
| QA rows leak into shared | A module still uses `ApplicationDbContext` directly — re-check controller/service routing (should not occur post 5.0D.4). |

## Safety notes

- Runners never start the web server; they call the runner and `return`.
- Both are guarded by the explicit flag **and** `app.Environment.IsDevelopment()`.
- They do not modify production logic and write only `QA_`-prefixed data.
- Never run with a production `DefaultConnection`.
