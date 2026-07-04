using System.Text.RegularExpressions;

namespace HardwareManagementSystem.Tools
{
    /// <summary>Static audit for save-confirmation standardization (--qa-save-confirm).</summary>
    public static class SaveConfirmationQaRunner
    {
        public static Task RunAsync(WebApplication app)
        {
            var pass = 0;
            var fail = 0;
            void Ok(string m) { pass++; Console.WriteLine($"[QASave] PASS — {m}"); }
            void No(string m) { fail++; Console.WriteLine($"[QASave] FAIL — {m}"); }

            var root = app.Environment.ContentRootPath;
            var siteJs = Path.Combine(root, "wwwroot", "js", "site.js");

            if (!File.Exists(siteJs))
            {
                No("wwwroot/js/site.js not found");
            }
            else
            {
                var js = File.ReadAllText(siteJs);
                if (js.Contains("HbForm") && js.Contains("data-hb-confirmed") && js.Contains("confirmAndSubmit"))
                    Ok("HbForm global helper present in site.js");
                else
                    No("HbForm helper incomplete in site.js");

                if (js.Contains("initAntiDoubleSubmit"))
                    Ok("Anti double-submit initialized in site.js");
                else
                    No("Anti double-submit missing");
            }

            var viewsRoot = Path.Combine(root, "Views");
            var cshtmlFiles = Directory.GetFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories);

            var duplicateHandlerPattern = new Regex(
                @"document\.querySelectorAll\(\s*['""]\.confirm-submit['""]\s*\)\.forEach",
                RegexOptions.IgnoreCase);

            var riskyLoopPattern = new Regex(
                @"isConfirmed\)\s*\{[^}]*form\.requestSubmit\(\)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            int duplicateHandlers = 0;
            int riskyLoops = 0;

            foreach (var file in cshtmlFiles)
            {
                var text = File.ReadAllText(file);
                if (duplicateHandlerPattern.IsMatch(text))
                {
                    duplicateHandlers++;
                    No($"Duplicate confirm-submit handler still in {Path.GetRelativePath(root, file)}");
                }

                if (riskyLoopPattern.IsMatch(text))
                {
                    riskyLoops++;
                    No($"Risky requestSubmit without HbForm guard in {Path.GetRelativePath(root, file)}");
                }
            }

            if (duplicateHandlers == 0)
                Ok("No per-page .confirm-submit forEach handlers (centralized in site.js)");

            if (riskyLoops == 0)
                Ok("No unguarded Swal → requestSubmit loops detected in views");

            Console.WriteLine($"[QASave] Summary: {pass} passed, {fail} failed.");
            if (fail > 0)
                Environment.ExitCode = 1;

            return Task.CompletedTask;
        }
    }
}
