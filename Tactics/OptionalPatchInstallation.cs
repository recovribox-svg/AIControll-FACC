// ★ v0.3.22: optional features fail closed without escaping into the mod's mandatory load path.
using System;
namespace CompanionAI_Pathfinder.Tactics
{
    public static class OptionalPatchInstallation
    {
        public static bool TryInstall(Action install, Action rollback, out Exception failure)
        {
            failure = null;
            try { install(); return true; }
            catch (Exception ex)
            {
                failure = ex;
                try { rollback(); }
                catch (Exception cleanup) { failure = new AggregateException(ex, cleanup); }
                return false;
            }
        }
    }
}
