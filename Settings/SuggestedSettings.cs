using System;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Class.Kineticist;
using Kingmaker.UnitLogic.FactLogic;

namespace CompanionAI_Pathfinder.Settings
{
    /// <summary>
    /// ★ v0.3.70 features review (issue #115): reads a character's build for <see cref="SuggestedSettingsRules"/> and applies
    /// the suggestion - automatically to a character AI Control sees for the first time (never over saved choices), or on the
    /// player's request from the settings panel (one character or the whole party).
    /// </summary>
    internal static class SuggestedSettings
    {
        internal static SuggestedSettingsRules.BuildFacts Read(UnitEntityData unit)
        {
            var f = new SuggestedSettingsRules.BuildFacts();
            if (unit?.Descriptor == null) return f;
            try
            {
                var weapon = unit.GetFirstWeapon();
                f.RangedWeapon = weapon?.Blueprint?.IsRanged ?? false;
                f.Shield = unit.Body?.SecondaryHand?.MaybeShield != null;
                int level = Math.Max(1, unit.Descriptor.Progression?.CharacterLevel ?? 1);
                foreach (var book in unit.Descriptor.Spellbooks)
                {
                    if (book?.Blueprint == null || book.Blueprint.MaxSpellLevel < 9) continue;
                    // The character's main progression: its caster level at least half its character level.
                    if (book.CasterLevel * 2 < level) continue;
                    f.FullCaster = true;
                    f.Divine |= !book.Blueprint.IsArcane;
                }
                var category = weapon?.Blueprint?.Category;
                var features = unit.Descriptor.Progression?.Features?.Enumerable;
                if (features != null)
                    foreach (var feature in features)
                    {
                        var bp = feature?.Blueprint;
                        if (bp == null) continue;
                        if (bp.GetComponent<AttackStatReplacement>() != null ||
                            category.HasValue && bp.GetComponent<WeaponFocusParametrized>() != null &&
                            feature.Param?.WeaponCategory == category)
                        {
                            f.WeaponFeats = true;
                            break;
                        }
                    }
                f.BlastKineticist = unit.Get<UnitPartKineticist>() != null &&
                    (weapon?.Blueprint == null || Kingmaker.Blueprints.BlueprintExtenstions.GetComponent<WeaponKineticBlade>(weapon.Blueprint) == null) &&
                    !f.RangedWeapon;
            }
            catch (Exception ex) { Main.Verbose($"[Settings] build of {unit.CharacterName}: {ex.Message}"); }
            return f;
        }

        internal static SuggestedSettingsRules.Suggestion For(UnitEntityData unit) => SuggestedSettingsRules.Suggest(Read(unit));

        /// <summary>Sets <paramref name="settings"/>' role and range from <paramref name="unit"/>'s build; returns the reason.</summary>
        internal static string Apply(UnitEntityData unit, CharacterSettings settings)
        {
            var s = For(unit);
            settings.Role = s.Role;
            settings.RangePreference = s.Range;
            settings.SuggestionPending = false;
            return s.Why;
        }

        /// <summary>A character's settings made in this session get the suggestion once, the first time its unit is at hand.</summary>
        internal static void EnsureApplied(UnitEntityData unit, CharacterSettings settings)
        {
            if (settings == null || !settings.SuggestionPending || unit?.Descriptor == null) return;
            try
            {
                string why = Apply(unit, settings);
                Main.Log($"[Settings] {unit.CharacterName}: role {settings.Role}, range {settings.RangePreference} suggested from its build ({why})");
                ModSettings.Save();
            }
            catch (Exception ex)
            {
                settings.SuggestionPending = false;
                Main.Verbose($"[Settings] suggestion for {unit.CharacterName}: {ex.Message}");
            }
        }
    }
}
