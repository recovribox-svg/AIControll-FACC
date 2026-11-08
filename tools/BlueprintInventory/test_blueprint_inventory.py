import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest

import blueprint_inventory as inventory


CLASS = "1" * 32
ARCHETYPE = "2" * 32
PROGRESSION = "3" * 32
FEATURE = "4" * 32
MISSING = "a" * 32


def bp(guid, kind, **data):
    return {"AssetId": guid, "Data": {"$type": "f" * 32 + ", " + kind, **data}}


class BlueprintInventoryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.export = self.root / "export"
        self.export.mkdir()

    def tearDown(self):
        self.temp.cleanup()

    def write(self, name, value, bom=False):
        target = self.export / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(value), encoding="utf-8-sig" if bom else "utf-8")
        return target

    def run_cli(self, *args):
        with contextlib.redirect_stdout(io.StringIO()) as out, contextlib.redirect_stderr(io.StringIO()):
            try:
                code = inventory.main([str(x) for x in args])
            except SystemExit as exc:
                code = exc.code
        return code, out.getvalue()

    def test_roster_links_progression_deltas_and_prerequisites(self):
        self.write("Class.jbp", bp(CLASS, "BlueprintCharacterClass", PrestigeClass=True,
                   IsMythic=False, HideInUI=False, m_Progression="!bp_" + PROGRESSION,
                   m_Archetypes=["!bp_" + ARCHETYPE], m_Spellbook=None))
        self.write("Arch.jbp", bp(ARCHETYPE, "BlueprintArchetype",
                   AddFeatures=[{"Level": 6, "m_Features": ["!bp_" + FEATURE]}],
                   RemoveFeatures=[{"Level": 1, "m_Features": ["!bp_" + FEATURE]}],
                   Components=[{"$type": "f" * 32 + ", PrerequisiteDLC", "Group": "All",
                                "Not": False, "m_Dlc": "!bp_" + MISSING, "Comment": "not copied"}]))
        self.write("Progression.jbp", bp(PROGRESSION, "BlueprintProgression",
                   m_Classes=[{"m_Class": "!bp_" + CLASS}],
                   LevelEntries=[{"Level": 20, "m_Features": ["!bp_" + FEATURE]}]))
        self.write("Feature.jbp", bp(FEATURE, "BlueprintFeature"))
        result = inventory.inventory([self.export])
        self.assertEqual(result["summary"]["loaded"], 4)
        self.assertEqual(result["summary"]["classes"], 1)
        self.assertTrue(result["classes"][0]["flags"]["PrestigeClass"])
        arch = result["archetypes"][0]
        self.assertEqual(arch["parent_classes_from_export_links"][0]["asset_id"], CLASS)
        self.assertEqual(arch["add_features"][0]["level"], 6)
        self.assertEqual(arch["remove_features"][0]["level"], 1)
        self.assertEqual(arch["add_features"][0]["features"][0]["candidates"][0]["name"], "Feature")
        self.assertEqual(result["progressions"][0]["level_entries"][0]["level"], 20)
        self.assertEqual(arch["prerequisites"][0]["references"][0]["resolution"], "missing")
        self.assertNotIn("Comment", json.dumps(arch["prerequisites"]))
        self.assertEqual(result["reference_closure"]["unresolved_distinct"], 1)
        self.assertEqual(result["status"], "partial")
        self.assertNotIn(str(self.root), json.dumps(result))

    def test_bom_case_normalization_zero_reference_and_ignored_json(self):
        self.write("Class.JBP", bp(CLASS.upper(), "BlueprintCharacterClass",
                   m_Progression="!bp_" + MISSING.upper(), m_Archetypes=["!bp_" + "0" * 32]), bom=True)
        self.write("Ignore.json", {"not": "a blueprint"})
        result = inventory.inventory([self.export])
        self.assertEqual(result["summary"]["loaded"], 1)
        closure = result["reference_closure"]
        self.assertEqual(closure["distinct_references"], 1)
        self.assertEqual(closure["zero_reference_occurrences_ignored"], 1)
        self.assertEqual(closure["unresolved"][0]["asset_id"], MISSING)
        self.assertEqual(result["classes"][0]["archetypes"], [])

    def test_duplicate_ids_remain_ambiguous_and_bad_inputs_are_counted(self):
        self.write("A.jbp", bp(FEATURE, "BlueprintFeature"))
        self.write("B.jbp", bp(FEATURE, "BlueprintBuff"))
        self.write("Class.jbp", bp(CLASS, "BlueprintCharacterClass", m_Archetypes=["!bp_" + FEATURE]))
        self.write("Zero.jbp", bp("0" * 32, "BlueprintFeature"))
        self.write("MissingData.jbp", {"AssetId": PROGRESSION})
        (self.export / "Broken.jbp").write_text('{"broken":', encoding="utf-8")
        result = inventory.inventory([self.export])
        self.assertEqual(result["summary"]["loaded"], 3)
        self.assertEqual(result["summary"]["failed"], 3)
        self.assertEqual(result["summary"]["duplicate_asset_ids"], 1)
        self.assertEqual(result["reference_closure"]["ambiguous_distinct"], 1)
        ref = result["classes"][0]["archetypes"][0]
        self.assertEqual(ref["resolution"], "ambiguous")
        self.assertEqual(len(ref["candidates"]), 2)
        self.assertEqual(result["reference_closure"]["unresolved_distinct"], 0)

    def test_missing_reference_counts_are_exact_while_examples_are_bounded(self):
        self.write("A.jbp", bp(CLASS, "BlueprintCharacterClass", Links=["!bp_" + MISSING] * 4))
        self.write("B.jbp", bp(FEATURE, "BlueprintFeature", Link="!bp_" + MISSING))
        result = inventory.inventory([self.export], example_limit=2)
        missing = result["reference_closure"]["unresolved"][0]
        self.assertEqual(missing["occurrences"], 5)
        self.assertEqual(missing["source_files"], 2)
        self.assertEqual(len(missing["examples"]), 2)
        self.assertEqual(missing["examples"][0]["field"], "$.Data.Links[0]")

    def test_determinism_digest_changes_and_overlapping_inputs(self):
        target = self.write("Feature.jbp", bp(FEATURE, "BlueprintFeature"))
        first = inventory.inventory([self.export])
        self.assertEqual(first, inventory.inventory([self.export]))
        overlap = inventory.inventory([self.export, target])
        self.assertEqual(overlap["summary"]["loaded"], 1)
        self.assertEqual(overlap["corpus_sha256"], first["corpus_sha256"])
        self.write("Feature.jbp", bp(FEATURE, "BlueprintFeature", Changed=True))
        self.assertNotEqual(first["corpus_sha256"], inventory.inventory([self.export])["corpus_sha256"])

    def test_cli_never_overwrites_inputs_or_existing_report_without_force(self):
        target = self.write("Feature.jbp", bp(FEATURE, "BlueprintFeature"))
        before = target.read_bytes()
        self.assertEqual(self.run_cli(self.export, "--output", target, "--force")[0], 2)
        self.assertEqual(self.run_cli(self.export, "--output", self.export / "report.json")[0], 2)
        report = self.root / "report.json"
        self.assertEqual(self.run_cli(self.export, "--output", report)[0], 0)
        old_report = report.read_bytes()
        self.assertEqual(self.run_cli(self.export, "--output", report)[0], 2)
        self.assertEqual(report.read_bytes(), old_report)
        self.assertEqual(self.run_cli(self.export, "--output", report, "--force")[0], 0)
        self.assertEqual(target.read_bytes(), before)

    def test_cli_strict_reports_partial_and_stdout_is_json(self):
        self.write("Feature.jbp", bp(FEATURE, "BlueprintFeature", Missing="!bp_" + MISSING))
        code, output = self.run_cli(self.export)
        self.assertEqual(code, 0)
        self.assertEqual(json.loads(output)["status"], "partial")
        self.assertEqual(self.run_cli(self.export, "--strict")[0], 1)
        self.assertEqual(self.run_cli(self.export, "--reference-examples", -1)[0], 2)
        self.assertEqual(self.run_cli(self.root / "absent")[0], 2)

    def test_empty_directory_is_an_input_error(self):
        self.assertEqual(self.run_cli(self.export)[0], 2)
        with self.assertRaises(ValueError):
            inventory.inventory([self.export])

    def test_symlink_inputs_are_not_read(self):
        target = self.write("Feature.jbp", bp(FEATURE, "BlueprintFeature"))
        link = self.export / "Linked.jbp"
        try:
            link.symlink_to(target)
        except OSError:
            self.skipTest("symlinks unavailable on this platform")
        result = inventory.inventory([self.export])
        self.assertEqual(result["summary"]["loaded"], 1)
        self.assertEqual(result["summary"]["skipped_symlinks"], 1)
        self.assertEqual(result["status"], "partial")
        self.assertEqual(self.run_cli(link)[0], 2)


if __name__ == "__main__":
    unittest.main()
