# Supplied installed mods and settings

Installed-input intake: 8 November 2026 at development `fdc5e5e`; interpretation self-reviewed against `3443d94` / §126. These are the author's supplied packages/settings, inspected as metadata and targeted decompiled types. All **eight loader-cache assemblies are byte-identical to the corresponding supplied DLLs**. Binary identity does not establish successful initialization, applied patches or actor ownership. No supplied DLL, symbol, settings file or decompiled source is committed.

Module MVIDs, SHA-256 identities, manifest dependencies and relevant configuration gates are in [INSTALLED_MODS.json](INSTALLED_MODS.json). [INPUT_CORPUS.json](INPUT_CORPUS.json) records input receipts. Earlier [public-source leads](MOD_EVIDENCE.md) are historical navigation evidence; installed bytes take precedence.

| Mod / supplied manifest version | Installed-code evidence and relevant mechanism | Register / remaining boundary |
| --- | --- | --- |
| TTT Core **0.7.15** | `TabletopTweaks.Core.MechanicsChanges.ActivatableAbilitySpendLogic`: custom packed spend types/amounts, critical spending, `ResourceCount`/availability patches | C06/C04; read the actual runtime spend type and enabled consumer, not a native-enum-only table |
| TTT Base **2.6.18** | `Bugfixes.Classes.Magus`: configured Spell Combat/Spellstrike immediate deactivation, burst/enchantment replacement, Perfect Critical changes; content includes eight captured class/archetype factory sites | C07/C08 belong to Claude. Source presence and a factory site do not prove the actor selected the feature |
| Isekai Reimagined **6.5.0** | `Content.Classes.IsekaiProtagonist` class/progression/ascendant and archetype/prestige builders; 24 captured class/archetype construction sites. Installed progression explicitly constructs levels 1–20; later systems depend on settings and generated content | C12/C14/C11/C04; generated class/progression graphs are absent from the supplied folders. Do not substitute the older public 5.4 source |
| Psionics **0.2.3** | `PsychicWarriorClass.Configure`, `SoulKnifeClass.Configure`; `Shared.Mechanics.Focus.Configure` creates focus and removes twelve named trance buffs on deactivation | C14/C04/C08; actual current cascade differs from assumptions drawn from older public sources. Both generated class roots are absent |
| Mystical Mayhem **0.7.0h**, KRXLib **1.2.0.0** | Installed custom component registrations and `MysticalMayhem.Settings.IsEnabled/Initialize`; its saved switches are read through ModMenu | C12/C14/C08; persisted ModMenu values not supplied. Factory recognizer captures none of its helper patterns; this does not mean it creates no content |
| TomeOfTheFirebird **1.4.18** | `New_Content.Feats.DiscordantSong.Make`: friendly/hostile areas, melee extra dice, ranged extra dice requiring the hostile buff on the target; archetype factory candidates also present | C05/C13/C14; selection, actual recipients/partners and saved content switches remain separate |
| DarkCodex **1.9.6**, CodexLib **1.8.3.1** | `Shared.Main.LoadSafe`, `PatchInfoCollection.IsDisenabled`, `Kineticist.CreateElementalScion`, `CodexLib.PropertyKineticistBurn` | C11/C02/C12; settings admit Elemental Scion and disable Mind Shield. Its `BurnNumberProperty` mutation matters to the Psychokineticist stat-penalty consumer even with Mind Shield off; it does not explain Orvath's nonlethal-property fallback |
| Demonology **0.1.5** | `NewMythicAbilities`, `NewSpellsAndFeatures`, custom level/combat restrictions; supplied GUID registry has 109 created-asset names. Assembly version is **1.5.18.0**, separately from manifest version | C12/C14/C04; generated summon/feat graphs and actors are absent, so no summon/control compatibility claim follows |

## Settings that change the interpretation

| Supplied configuration | Checked gate / consequence |
| --- | --- |
| DarkCodex `NewFeatureDefaultOn=false`, empty blacklist, whitelist `Kineticist.CreateElementalScion` | `LoadSafe` consults `IsDisenabled` before invoking a feature method. The whitelist admits Elemental Scion; Mind Shield is not admitted. This does not describe every unconditional framework initialization path |
| TTT Base Magus `SpellCombatDisableImmediatly`, `SpellCombatSpellbookRestrictions`, `SpellCombatAbilityVariants`, `FixBurstStacking`; Sword Saint `PerfectCritical`: enabled, parent groups not disabled | Installed `Bugfixes.Classes.Magus` reads those named gates. Preserve Claude's native-command and enchantment work; do not loosen internal-toggle exclusions |
| TTT Base Alchemist mutation/bomb fixes, Skald Spell Kenning, Warpriest fighter/war-blessing fixes: enabled | Current code/configuration, rather than the earlier public snapshot, is the input for their contracts |
| Isekai `ExcludeCompanionsFromIsekaiClass=true`, `AllowMainCharacterRetinue=false`, level uncapping enabled | These constrain selection/progression opportunities; they neither remove already-owned runtime facts nor authorize AI control. The installed class builder and actual save still determine ownership |
| Later `AddedContent.json` upload matches `IsekaiMod/Config/AddedContent.json` exactly | It is the packaged-default copy already supplied. The explicitly supplied `UserSettings/AddedContent.json` remains the user-setting evidence; no silent override is inferred from a duplicate upload |
| MysticalMayhem/TomeOfTheFirebird/Psionics saved content switches unavailable | Defaults in code are recorded as defaults, not promoted to enabled-patch evidence |

## Generated-asset scope

The author reports exporting after modded-game initialization. The folder contents nevertheless contain **zero** matches for the generated IDs in the supplied Isekai (4,097), TTT Base (1,427) and Demonology (109) `NewBlueprints` maps. The later JSON uploads are byte-identical copies of registries/defaults already present in the packages/settings. They identify names and GUIDs; they do not supply the corresponding feature/component records. The exporter is unknown. This is a scope discrepancy, not a claim the author ran an unmodded game or that a particular mod failed.

Concrete example: enabled `Kineticist.CreateElementalScion` constructs a Kineticist archetype and appends it to the class. Its added breakpoints are **1, 7, 15 and 20**; it replaces the level-1 infusion and level-7/15 extra elements. The method also replaces property `02c5943c77717974cb7fa1b7c0dc51f8` with `CodexLib.PropertyKineticistBurn`, whose getter adds one when Elemental Embodiment is owned. The supplied `BurnNumberProperty` still contains the native `KineticistBurnPropertyGetter`, and the new archetype asset is absent. The Psychokineticist stat-penalty graph consumes that property, so its expected patched shape remains unverified even though Mind Shield is off. Ordinary/Dark Elementalist `BurnEffectBuff` ranks instead use `KineticNonLethalDamageProperty`; this Elemental Scion mutation does not explain Orvath's observed fallback. §126 leaves that cause open, and `3443d94` names the buff/property/getter/feature in the next runtime log. Verify that actual actor evidence before an adapter or causal claim.

[installed-construction-sites.json](installed-construction-sites.json) records **static construction hints** from the ten supplied assemblies, plus registered component type identities. The recognizer captures only named factory patterns, uses bounded literal lookback and does not reconstruct loops, clones, feature ownership or enabled call paths. Counts are not a complete mod roster. Class/archetype construction candidates remain separate from the roster-backed exported rows.

Next evidence for closing this gap is a targeted runtime-cache export of the generated class/archetype/progression roots and referenced facts, or a complete runtime kit for the selected mechanism. Older source is unnecessary for current-version conclusions. Existing DLL/settings analysis and the bounded export map can proceed without it.

## Supplementary mod-content index

The later `alterasc.github.io-main.zip` is an automatically generated Wrath mod-content catalog: Markdown names/GUIDs/category listings and binary GUID arrays, without JBP component/action/progression records. [SUPPLEMENTARY_INDEX.json](SUPPLEMENTARY_INDEX.json) records its archive identity, version labels, recognized class/archetype listings and overlaps.

| Index entry | Index / supplied version | Use |
| --- | --- | --- |
| Demonology | 0.1.5 / 0.1.5 | All 109 listed GUIDs match the supplied registry; names corroborated, object graphs still absent |
| TTT Base | 2.6.18 / 2.6.18 | 1,427 GUIDs overlap the supplied registry; 1,436 are listed. Category listings are navigation, not complete enabled-content evidence |
| TomeOfTheFirebird | 1.4.18 / 1.4.18 | 256 listed GUIDs provide lookup candidates; matching manifest labels do not fingerprint the installed binary |
| Isekai / Psionics | 5.4.2 / 6.5.0; 1.1.1 / 0.2.3 | Historical/different-version navigation only; no substitution for current code |
| DarkCodex / MysticalMayhem | 1.8.0 / 1.9.6; 0.6.6h / 0.7.0h | Historical navigation. The current Mystical registry has 906 entries and zero matches in these exports |

None of those catalog GUID arrays overlap the supplied JBP folders. The arrays encode a count followed by GUIDs; they do not close the missing initialized-object graph. No catalog prose, binary files or full third-party source is copied into the repository.
