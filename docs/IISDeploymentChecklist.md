# HardBuild POS — IIS Deployment Checklist

**Project:** HardwareManagementSystem / HardBuild POS  
**Runtime:** .NET 9 / ASP.NET Core MVC  
**Host:** Windows Server + IIS  
**Last Updated:** 2026-05-29

---

## Pre-Deployment Prerequisites

- [ ] Windows Server 2019 / 2022 (IIS role enabled)
- [ ] SQL Server instance reachable from the app server
- [ ] Domain / subdomain DNS record pointing to the server
- [ ] SSL/TLS certificate obtained (see step 9)
- [ ] Pre-deployment database backup taken (see `ProductionBackupPlan.md`)

---

## Step 1 — Install .NET 9 Hosting Bundle

1. Download the **.NET 9 Windows Hosting Bundle** from:  
   https://dotnet.microsoft.com/en-us/download/dotnet/9.0
2. Run the installer on the server.
3. After installation, restart IIS:

```powershell
iisreset
```

4. Verify:

```powershell
dotnet --version
# Expected output: 9.x.x
```

---

## Step 2 — Configure IIS Application Pool

1. Open **IIS Manager** → Application Pools → Add Application Pool.
2. Settings:

   | Setting | Value |
   |---------|-------|
   | Name | `HardBuildPool` |
   | .NET CLR Version | **No Managed Code** |
   | Managed Pipeline Mode | Integrated |
   | Start Mode | AlwaysRunning (recommended) |
   | Identity | `ApplicationPoolIdentity` (or a dedicated service account) |

3. Click **OK**.

---

## Step 3 — Publish the Application

On the **build machine** (or CI server), run:

```powershell
dotnet publish HardwareManagementSystem.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained false `
  --output C:\Publish\HardBuild
```

> `--self-contained false` assumes the Hosting Bundle is installed on the server.  
> Use `--self-contained true` if you prefer a self-contained deployment.

Copy the contents of `C:\Publish\HardBuild` to the server's site root (e.g. `D:\Sites\HardBuild`).

---

## Step 4 — Create the IIS Website

1. IIS Manager → Sites → Add Website.
2. Settings:

   | Setting | Value |
   |---------|-------|
   | Site Name | `HardBuildPOS` |
   | Application Pool | `HardBuildPool` |
   | Physical Path | `D:\Sites\HardBuild` |
   | Binding Protocol | `https` |
   | Binding Port | `443` |
   | Host name | `pos.yourdomain.com` |

3. Also add an `http` binding on port 80 that redirects to HTTPS (see step 9).

---

## Step 5 — Environment Variable Setup

ASP.NET Core reads the environment from `ASPNETCORE_ENVIRONMENT`. Set it via IIS:

1. IIS Manager → Select the site → **Configuration Editor**.
2. Navigate to `system.webServer/aspNetCore`.
3. In `environmentVariables`, add:

   | Variable | Value |
   |----------|-------|
   | `ASPNETCORE_ENVIRONMENT` | `Production` |

**Alternative** — set at the Application Pool level via **Advanced Settings → Environment Variables** (IIS 10+).

> Never set `ASPNETCORE_ENVIRONMENT=Development` on a production server.

---

## Step 6 — appsettings.Production.json

The file `appsettings.Production.json` is **excluded from source control** (`.gitignore`).  
It must be manually placed at the site root (`D:\Sites\HardBuild\appsettings.Production.json`).

Minimum required content:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=HardwareManagementDB;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

> ⚠️ Keep this file secured with NTFS permissions — readable only by the App Pool identity.  
> Do **not** commit real credentials to source control.

---

## Step 7 — Run Database Migration

From the server (or a machine with database access and the dotnet EF tools installed):

```powershell
dotnet ef database update `
  --project HardwareManagementSystem.csproj `
  --connection "Server=YOUR_SERVER;Database=HardwareManagementDB;..."
```

Or apply from the published output using the migration bundle (if generated).

> Always take a **full database backup** before running migrations (see `ProductionBackupPlan.md`).

---

## Step 8 — Folder Permissions

Grant the IIS Application Pool identity read/write access to the site folder:

```powershell
icacls "D:\Sites\HardBuild" /grant "IIS AppPool\HardBuildPool:(OI)(CI)RX" /T
icacls "D:\Sites\HardBuild\logs" /grant "IIS AppPool\HardBuildPool:(OI)(CI)M" /T
```

If the app writes temporary files (e.g., Excel import temp storage), also grant write on those paths.

**Key paths that require write access:**
- App log folder (if file-based logging is configured)
- `wwwroot` — read-only is sufficient after publish
- Any custom upload folder defined in appsettings

---

## Step 9 — HTTPS Certificate

**Option A: Let's Encrypt (free, auto-renew)**
- Use [Win-ACME](https://www.win-acme.com/) or Certbot for Windows.
- Binds automatically to IIS.

**Option B: Commercial certificate**
- Import via IIS Manager → Server Certificates → Import.
- Bind to the HTTPS site binding created in step 4.

**HTTP → HTTPS redirect:**  
In `web.config` (generated by publish), or in IIS URL Rewrite module:

```xml
<rule name="HTTP to HTTPS" stopProcessing="true">
  <match url="(.*)" />
  <conditions>
    <add input="{HTTPS}" pattern="off" ignoreCase="true" />
  </conditions>
  <action type="Redirect" url="https://{HTTP_HOST}/{R:1}" redirectType="Permanent" />
</rule>
```

---

## Step 10 — Verify Health Check Endpoint

After deployment, confirm the app is healthy:

```
GET https://pos.yourdomain.com/health
Expected response: 200 OK  |  Body: Healthy
```

This endpoint is anonymous and does not require login.

---

## Step 11 — IIS Request Limits (web.config)

The app enforces a 15 MB max request body size via Kestrel.  
IIS also has its own `maxAllowedContentLength` which defaults to 30 MB — no change needed unless you lower it below 15 MB.

To explicitly document the IIS limit, verify `web.config` contains:

```xml
<security>
  <requestFiltering>
    <!-- Must be >= 15728640 (15 MB) to allow Excel imports -->
    <requestLimits maxAllowedContentLength="15728640" />
  </requestFiltering>
</security>
```

---

## Smoke Test After Deployment

After completing all steps, run the **Production Smoke Test** (`docs/ProductionSmokeTest.md`).

---

## Rollback Plan

If the deployment fails after migration:

1. Stop the IIS site.
2. Restore the database from the pre-deployment backup.
3. Deploy the previous application version.
4. Start the IIS site.
5. Verify `/health` returns `Healthy`.

---

*Review this checklist whenever upgrading the .NET runtime, changing the hosting environment, or deploying a major release.*
