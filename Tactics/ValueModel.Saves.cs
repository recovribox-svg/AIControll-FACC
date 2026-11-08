// ★ v0.3.30 competence programme: a DC changes ordinary/disbelief saves, not spell hit or resistance chances.
namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>The spell's modeled failed-save chance, with Persistent applied after the d20 limits.</summary>
        public static float SpellSaveFailChance(int dc, int save, bool persistent, bool autoPass = false)
        {
            if (autoPass) return 0f;
            float fail = SaveFailChance(dc, save);
            return persistent ? 1f - (1f - fail) * (1f - fail) : fail;
        }

        /// <summary>
        /// The share of a save-dependent effect delivered: a failed save for control/save-negates damage, or the
        /// expected damage fraction for half-on-save damage with the recipient's Evasion / Improved Evasion flags.
        /// </summary>
        public static float SaveEffectFactor(float fail, bool halfOnSave, bool evasion = false, bool improved = false) =>
            halfOnSave ? fail * (improved ? 0.5f : 1f) + (1f - fail) * (evasion ? 0f : 0.5f) : fail;

        /// <summary>
        /// A shadow effect is fully real on failed disbelief, at its reality fraction on successful disbelief.
        /// Preserve the existing native-settings reader's no-reduction behavior for a nonpositive fraction.
        /// </summary>
        public static float ShadowEffectFactor(float fail, float reality) =>
            reality <= 0f || reality >= 1f ? 1f : fail + (1f - fail) * reality;

        /// <summary>
        /// Marginal gain over the original ordinary/disbelief save-dependent factor. Independent hit/SR factors cancel;
        /// an effect that cannot land does not gain a finite relative value from this forecast.
        /// </summary>
        public static float SaveBoostShare(float before, float after) =>
            before > 0f && after > before ? (after - before) / before : 0f;

        /// <summary>★ v0.3.30 competence2: Persistent - a passed save is rolled again at the same DC; resisting needs both.</summary>
        public static float PersistentFail(float fail) => 1f - (1f - fail) * (1f - fail);

        /// <summary>
        /// ★ v0.3.30 competence2 (PR #20 review): what free Persistent adds, as a share of the save-dependent effect -
        /// the same effect factor as a DC boost (half damage, Evasion, Improved Evasion), not a flat half for half-on-save.
        /// Nothing for a save that always or never fails here.
        /// </summary>
        public static float PersistentBoostShare(float fail, bool halfOnSave, bool evasion = false, bool improved = false) =>
            fail > 0f && fail < 1f
                ? SaveBoostShare(SaveEffectFactor(fail, halfOnSave, evasion, improved),
                                 SaveEffectFactor(PersistentFail(fail), halfOnSave, evasion, improved))
                : 0f;
    }
}
