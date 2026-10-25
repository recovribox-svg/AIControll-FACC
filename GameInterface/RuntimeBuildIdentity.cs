// ★ v0.3.22: identify the loaded binary even when UMM test packages share release metadata.
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace CompanionAI_Pathfinder.GameInterface
{
    internal static class RuntimeBuildIdentity
    {
        private static readonly Lazy<string> Identity = new Lazy<string>(() =>
        {
            var assembly = Assembly.GetExecutingAssembly();
            string fileHash;
            try
            {
                using (var file = File.OpenRead(assembly.Location))
                using (var sha = SHA256.Create())
                    fileHash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            }
            catch (Exception) { fileHash = "unavailable"; }
            return $"[Build] loaded module {assembly.ManifestModule.ModuleVersionId}; file SHA-256 {fileHash}; " +
                   "features: empty-root cache, native shared-area budget, pacing diagnostics";
        });

        // Cached at load and repeated at combat start so combat-only excerpts still identify their code.
        internal static void Report()
        {
            try { Main.Log(Identity.Value); }
            catch (Exception ex) { Main.Verbose($"[Build] identity unavailable: {ex.Message}"); }
        }
    }
}
