using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;

namespace HardwareManagementSystem.Tools
{
    /// <summary>
    /// Subscription access QA. Development only.
    ///   dotnet run -- --qa-subscription-access
    ///   dotnet run -- --qa-subscription-access --business-date=2026-07-15
    /// </summary>
    public static class SubscriptionAccessQaRunner
    {
        public static Task RunAsync(WebApplication app, string[] args)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[QA-SUB] Development-only.");
                Environment.ExitCode = 1;
                return Task.CompletedTask;
            }

            var pass = 0;
            var fail = 0;
            var results = new List<object>();

            void Check(string name, bool ok, string detail)
            {
                if (ok) { pass++; Console.WriteLine($"  [PASS] {name}"); }
                else { fail++; Console.WriteLine($"  [FAIL] {name} — {detail}"); }
                results.Add(new { name, pass = ok, detail });
            }

            ITenantSubscriptionAccessService SvcAt(string utcIso, string tz = "Asia/Manila")
            {
                var clock = new BusinessClock(DateTime.Parse(utcIso).ToUniversalTime(), tz);
                return new TenantSubscriptionAccessService(clock);
            }

            Console.WriteLine("── Login / eligibility (date-only, Asia/Manila) ──");

            // 1 Active before expiration
            var r1 = SvcAt("2026-07-10T10:00:00Z").Evaluate(true, TenantStatus.Active, new DateTime(2026, 7, 14));
            Check("active_before_expiration", r1.IsAllowed, r1.Message);

            // 2 Trial before expiration
            var r2 = SvcAt("2026-07-10T10:00:00Z").Evaluate(true, TenantStatus.Trial, new DateTime(2026, 7, 14));
            Check("trial_before_expiration", r2.IsAllowed, r2.Message);

            // 3 Trial on expiration date (inclusive)
            var r3 = SvcAt("2026-07-14T10:00:00Z").Evaluate(true, TenantStatus.Trial, new DateTime(2026, 7, 14));
            Check("trial_on_expiration_date", r3.IsAllowed, r3.Message);

            // 4 Trial one day after (Manila July 15 morning)
            var r4 = SvcAt("2026-07-14T18:00:00Z").Evaluate(true, TenantStatus.Trial, new DateTime(2026, 7, 14));
            Check("trial_day_after_expiration", !r4.IsAllowed && r4.EffectiveStatus == TenantStatus.Expired, r4.Message);

            // 5 Status Expired
            var r5 = SvcAt("2026-07-10T10:00:00Z").Evaluate(true, TenantStatus.Expired, new DateTime(2026, 12, 31));
            Check("status_expired", !r5.IsAllowed, r5.Message);

            // 6 Suspended
            var r6 = SvcAt("2026-07-10T10:00:00Z").Evaluate(true, TenantStatus.Suspended, new DateTime(2026, 12, 31));
            Check("suspended", !r6.IsAllowed, r6.Message);

            // 7 Disabled tenant
            var r7 = SvcAt("2026-07-10T10:00:00Z").Evaluate(false, TenantStatus.Active, new DateTime(2026, 12, 31));
            Check("disabled_tenant", !r7.IsAllowed, r7.Message);

            // 8 No subscription (no plan, no expiration)
            var r8 = SvcAt("2026-07-10T10:00:00Z").Evaluate(true, TenantStatus.Trial, null, null);
            Check("no_subscription", !r8.IsAllowed, r8.Message);

            Console.WriteLine("── Time boundary tests ──");

            // UTC still July 14 but Manila July 15
            var r9 = SvcAt("2026-07-14T18:30:00Z").Evaluate(true, TenantStatus.Trial, new DateTime(2026, 7, 14));
            Check("manila_july15_utc_july14", !r9.IsAllowed, $"today={r9.BusinessToday}");

            // End of expiration day Manila
            var r10 = SvcAt("2026-07-14T15:59:00Z").Evaluate(true, TenantStatus.Trial, new DateTime(2026, 7, 14));
            Check("last_minute_expiration_day", r10.IsAllowed, r10.Message);

            // Month-end
            var r11 = SvcAt("2026-02-01T00:00:00Z").Evaluate(true, TenantStatus.Active, new DateTime(2026, 1, 31));
            Check("month_end_expired", !r11.IsAllowed, r11.Message);

            // Year-end inclusive
            var r12 = SvcAt("2025-12-31T10:00:00Z").Evaluate(true, TenantStatus.Active, new DateTime(2025, 12, 31));
            Check("year_end_inclusive", r12.IsAllowed, r12.Message);

            // Leap year Feb 29
            var r13 = SvcAt("2024-03-01T00:00:00Z").Evaluate(true, TenantStatus.Trial, new DateTime(2024, 2, 29));
            Check("leap_year_expired", !r13.IsAllowed, r13.Message);

            // Null expiration with Active + plan id = allowed (custom perpetual)
            var r14 = SvcAt("2026-07-10T10:00:00Z").Evaluate(true, TenantStatus.Active, null, 1);
            Check("active_with_plan_no_expiration", r14.IsAllowed, r14.Message);

            // TCS reproduction July 15 2026
            var overrideDate = args.FirstOrDefault(a => a.StartsWith("--business-date="))?.Split('=')[1];
            if (overrideDate != null)
            {
                var utc = DateTime.Parse(overrideDate + "T02:00:00").ToUniversalTime();
                var tcs = SvcAt(utc.ToString("o")).Evaluate(true, TenantStatus.Trial, new DateTime(2026, 7, 14));
                Check($"manual_tcs_{overrideDate}", !tcs.IsAllowed, tcs.Message);
            }
            else
            {
                var tcs = SvcAt("2026-07-14T18:00:00Z").Evaluate(true, TenantStatus.Trial, new DateTime(2026, 7, 14));
                Check("tcs_july15_manila_blocked", !tcs.IsAllowed, tcs.Message);
            }

            Console.WriteLine();
            Console.WriteLine($"[QA-SUB] {pass} passed, {fail} failed");

            var outPath = Path.Combine("docs", "SubscriptionAccessTestResults.json");
            Directory.CreateDirectory("docs");
            File.WriteAllText(outPath, System.Text.Json.JsonSerializer.Serialize(
                new { passed = pass, failed = fail, tests = results },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            Environment.ExitCode = fail == 0 ? 0 : 1;
            return Task.CompletedTask;
        }
    }
}
