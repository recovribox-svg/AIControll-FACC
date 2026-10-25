// ★ v0.3.22: explicit partial-load allowance; empty and predominantly failed loads never produce coverage verdicts.
using System;
using System.Collections.Generic;
namespace ProfileAudit
{
    public sealed class AuditOptions
    {
        public string Output;
        public readonly List<string> Roots = new List<string>();
        public int AllowFailures;
        public bool KineticChecks;
        public static bool TryParse(string[] args, out AuditOptions options, out string error)
        {
            options = new AuditOptions(); error = null;
            if (args.Length < 2) { error = "expected dump folders and output file"; return false; }
            options.Output = args[args.Length - 1];
            bool allowanceSeen = false;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--kinetic-checks") options.KineticChecks = true;
                else if (args[i] == "--allow-failures")
                {
                    if (allowanceSeen || ++i >= args.Length - 1 || !int.TryParse(args[i], out int limit) || limit < 0)
                    { error = "--allow-failures requires one nonnegative integer"; return false; }
                    options.AllowFailures = limit; allowanceSeen = true;
                }
                else if (args[i].StartsWith("--", StringComparison.Ordinal))
                { error = "unknown option: " + args[i]; return false; }
                else options.Roots.Add(args[i]);
            }
            if (options.Roots.Count == 0) { error = "no blueprint dump folders supplied"; return false; }
            return true;
        }
        public bool CanAudit(int loaded, int failed) => loaded > 0 && failed <= AllowFailures && (failed == 0 || loaded > failed);
    }
}
