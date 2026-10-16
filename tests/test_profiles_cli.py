"""Real offline controller commands. No Unity, runtime, testbench or game files."""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import time
import unittest

CONTROLLER = Path(__file__).resolve().parents[1] / "controller/UGEVR.Controller/bin/Release/net10.0/UGEVR.Controller.dll"


class Profiles(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="ugevr-profile-check-")
        self.root = Path(self.temp.name)
        self.game = "steam:383870"
        self.digest = hashlib.sha256(self.game.encode()).hexdigest()
        self.game_dir = self.root / "games" / self.digest
        self.personal = self.game_dir / "personal/config.txt"
        self.shared = self.game_dir / "shared"

    def tearDown(self):
        self.temp.cleanup()

    def cli(self, action, *args, success=True, game=None):
        child = subprocess.run(["dotnet", str(CONTROLLER), "profile", action, "--storage-root", str(self.root), "--game-id", game or self.game, *map(str, args)], capture_output=True, text=True, timeout=8)
        self.assertEqual(child.stderr, "")
        self.assertEqual(child.returncode, 0 if success else 2, child.stdout)
        return {key: json.loads(value.strip()) for key, value in (line.split(":", 1) for line in child.stdout.splitlines())}

    def save(self, scope, revision, key, value, **kwargs):
        return self.cli("save", "--scope", scope, "--expected-revision", revision, "--setting", key, "--value", value, **kwargs)

    def shared_fixture(self, **changes):
        self.shared.mkdir(parents=True, exist_ok=True)
        data = {"schema_version": "1", "profile_id": "f683aa26-63e1-42ee-bc27-d6c3b41bc61d", "game_id": self.game, "build_sha256": "a" * 64}
        data.update(changes)
        (self.shared / "profile.yaml").write_text("".join(key + ": " + json.dumps(value) + "\n" for key, value in data.items()))
        (self.shared / "config.txt").write_text("[Tracking]\nUnitsPerMeter=3\nRetainGamePitchAndRoll=true\n")

    def test_all_five_layers_and_reset_reveal_the_lower_value(self):
        state = self.cli("resolve")
        self.assertEqual((state["Tracking_UnitsPerMeter"], state["Tracking_UnitsPerMeter_source"]), ("1", "built_in"))
        self.assertEqual(state["Tracking_RetainGamePitchAndRoll"], "false")
        self.assertEqual(self.save("global", 0, "Tracking.UnitsPerMeter", "2")["revision"], "1")
        self.assertEqual(self.cli("resolve")["Tracking_UnitsPerMeter_source"], "global")
        self.shared_fixture()
        shared_before = {p.name: p.read_bytes() for p in self.shared.iterdir()}
        self.assertEqual(self.cli("resolve")["Tracking_UnitsPerMeter_source"], "shared")
        self.save("personal", 0, "Tracking.UnitsPerMeter", "4")
        state = self.cli("resolve")
        self.assertEqual((state["Tracking_UnitsPerMeter"], state["Tracking_UnitsPerMeter_source"]), ("4", "personal"))
        self.assertEqual(state["Tracking_RetainGamePitchAndRoll_source"], "shared")
        session = self.root / "session-config.txt"
        session.write_text("[Tracking]\nUnitsPerMeter=5\n")
        before = self.personal.read_bytes()
        state = self.cli("resolve", "--session-config", session)
        self.assertEqual((state["Tracking_UnitsPerMeter"], state["Tracking_UnitsPerMeter_source"]), ("5", "session"))
        self.assertEqual(self.personal.read_bytes(), before)
        self.cli("reset", "--scope", "personal", "--expected-revision", 1)
        self.assertEqual(self.cli("resolve")["Tracking_UnitsPerMeter"], "3")
        self.assertEqual(shared_before, {p.name: p.read_bytes() for p in self.shared.iterdir()})
        self.assertEqual(self.cli("resolve")["runtime_applied"], "false")

    def test_same_named_games_have_distinct_explicit_identity(self):
        # Names never enter the storage key. Two assigned titles called Game.exe
        # are separate by identity, regardless of their basename or path.
        first = "assigned:11111111-1111-4111-8111-111111111111"
        second = "assigned:22222222-2222-4222-8222-222222222222"
        self.save("personal", 0, "Tracking.UnitsPerMeter", "8", game=first)
        self.assertEqual(self.cli("resolve", game=first)["Tracking_UnitsPerMeter"], "8")
        self.assertEqual(self.cli("resolve", game=second)["Tracking_UnitsPerMeter"], "1")

    def test_roundtrip_and_stale_edits(self):
        self.save("personal", 0, "Tracking.UnitsPerMeter", "0.125")
        self.save("personal", 1, "Tracking.RetainGamePitchAndRoll", "TRUE")
        state = self.cli("validate")
        self.assertEqual(state["Tracking_UnitsPerMeter"], "0.125")
        self.assertEqual(state["Tracking_RetainGamePitchAndRoll"], "true")
        old = self.personal.read_bytes()
        self.save("personal", 1, "Tracking.UnitsPerMeter", "6", success=False)
        self.assertEqual(old, self.personal.read_bytes())
        self.cli("reset", "--scope", "personal", "--expected-revision", 2, "--setting", "Tracking.UnitsPerMeter")
        self.assertEqual(self.cli("resolve")["Tracking_UnitsPerMeter"], "1")
        self.assertEqual(self.cli("resolve")["Tracking_RetainGamePitchAndRoll"], "true")

    def test_invalid_settings_do_not_change_existing_bytes(self):
        self.save("personal", 0, "Tracking.UnitsPerMeter", "2")
        old = self.personal.read_bytes()
        for key, value in [("Tracking.UnitsPerMeter", value) for value in ("0", "-1", "NaN", "Infinity", "1e300", "1e-300", "1,5")] + [("Tracking.RetainGamePitchAndRoll", "1"), ("VR_RenderingMethod", "NativeStereo"), ("Input.AllowRewiredInputDiagnostic", "true")]:
            with self.subTest(key=key, value=value):
                self.save("personal", 1, key, value, success=False)
                self.assertEqual(old, self.personal.read_bytes())

    def test_future_schema_unknown_keys_duplicate_and_identity_mismatch(self):
        self.save("personal", 0, "Tracking.UnitsPerMeter", "2")
        old = self.personal.read_bytes()
        for changes in ({"schema_version": "2"}, {"game_id": "steam:753640"}, {"profile_id": "bad"}, {"unsupported": "value"}, {"build_sha256": "bad"}):
            with self.subTest(changes=changes):
                self.shared_fixture(**changes)
                self.cli("validate", success=False)
                self.assertEqual(old, self.personal.read_bytes())
        self.shared_fixture()
        metadata = self.shared / "profile.yaml"
        metadata.write_text(metadata.read_text() + 'game_id: "steam:383870"\n')
        self.cli("validate", success=False)
        self.shared_fixture()
        self.cli("validate", "--build-sha256", "b" * 64, success=False)
        self.assertEqual(self.cli("validate", "--build-sha256", "a" * 64)["build_evidence"], "fingerprint_match_not_runtime_validation")

    def test_ambiguous_ini_never_admits_a_prefix(self):
        self.personal.parent.mkdir(parents=True)
        for content in ("[Tracking]\nUnitsPerMeter=2\nUnitsPerMeter=3\n", "[Tracking]\nUnitsPerMeter=2\n[Tracking]\n", "[Unreal]\nUnitsPerMeter=1", "[Tracking]\nRetainGamePitchAndRoll=maybe", "# UGEVR profile revision: 1\n# UGEVR profile revision: 2\n", "x" * 65537):
            with self.subTest(content=content[:60]):
                self.personal.write_text(content)
                self.cli("resolve", success=False)
                self.assertEqual(self.personal.read_text(), content)

    def test_scope_and_identity_cannot_escape_storage(self):
        for game in ("../other", "Game.exe", "steam:01", "steam:0", "assigned:../escape"):
            self.cli("resolve", game=game, success=False)
        for scope in ("shared", "session", "../shared", "C:/outside"):
            self.save(scope, 0, "Tracking.UnitsPerMeter", "2", success=False)
        self.assertFalse(self.personal.exists())
        self.cli("resolve", "--session-config", self.root / "missing", success=False)

    def test_revision_width_is_consistent_and_overflow_preserves_old_bytes(self):
        self.personal.parent.mkdir(parents=True)
        self.personal.write_text("# UGEVR profile revision: 2147483647\n[Tracking]\nUnitsPerMeter=2\n")
        self.assertEqual(self.save("personal", 2147483647, "Tracking.UnitsPerMeter", "3")["revision"], "2147483648")
        self.cli("reset", "--scope", "personal", "--expected-revision", 2147483648)
        self.assertEqual(self.cli("resolve")["personal_revision"], "2147483649")
        self.personal.write_text("# UGEVR profile revision: 9223372036854775807\n[Tracking]\nUnitsPerMeter=2\n")
        old = self.personal.read_bytes()
        self.save("personal", 9223372036854775807, "Tracking.UnitsPerMeter", "3", success=False)
        self.assertEqual(self.personal.read_bytes(), old)

    def test_concurrent_layer_changes_cannot_mix_effective_value_and_revision(self):
        self.personal.parent.mkdir(parents=True)
        self.personal.write_text("# UGEVR profile revision: 1\n[Tracking]\nUnitsPerMeter=1\n")
        stop = threading.Event()
        errors = []

        def publisher():
            revision = 2
            stage = self.personal.with_suffix(".publisher-stage")
            try:
                while not stop.is_set():
                    stage.write_text(f"# UGEVR profile revision: {revision}\n[Tracking]\nUnitsPerMeter={revision}\n")
                    try:
                        os.replace(stage, self.personal)
                        revision += 1
                    except PermissionError:
                        # Read leases may briefly deny replacement on Windows.
                        time.sleep(.001)
            except BaseException as error:
                errors.append(error)
            finally:
                stage.unlink(missing_ok=True)

        thread = threading.Thread(target=publisher)
        thread.start()
        try:
            for _ in range(16):
                state = self.cli("resolve")
                self.assertEqual(state["Tracking_UnitsPerMeter"], state["personal_revision"])
                self.assertEqual(state["Tracking_UnitsPerMeter_source"], "personal")
        finally:
            stop.set()
            thread.join(timeout=3)
        self.assertFalse(thread.is_alive())
        self.assertEqual(errors, [])

    def test_existing_directory_is_not_an_absent_settings_layer(self):
        self.personal.mkdir(parents=True)
        self.cli("validate", success=False)
        self.assertTrue(self.personal.is_dir())

    def test_existing_file_cannot_be_a_storage_root_or_layer_directory(self):
        target = self.root / "not-a-directory"
        target.write_bytes(b"preserve")
        previous = self.root
        try:
            self.root = target
            self.assertIn("non-directory", self.cli("validate", success=False)["error"])
        finally:
            self.root = previous
        self.assertEqual(target.read_bytes(), b"preserve")
        global_path = self.root / "global"
        global_path.write_bytes(b"preserve global")
        self.cli("validate", success=False)
        self.assertEqual(global_path.read_bytes(), b"preserve global")

    def test_invalid_utf8_and_oversize_metadata_refuse_without_rewriting(self):
        self.personal.parent.mkdir(parents=True)
        raw = b"# invalid: \xff\n[Tracking]\nUnitsPerMeter=2\n"
        self.personal.write_bytes(raw)
        self.cli("validate", success=False)
        self.assertEqual(self.personal.read_bytes(), raw)
        self.personal.unlink()
        self.shared_fixture()
        metadata = self.shared / "profile.yaml"
        for raw in (metadata.read_bytes() + b"# invalid: \xff\n", b"#" + b"x" * 65536):
            with self.subTest(size=len(raw)):
                metadata.write_bytes(raw)
                self.cli("resolve", success=False)
                self.assertEqual(metadata.read_bytes(), raw)

    def test_trailing_nul_cannot_be_silently_canonicalized(self):
        self.personal.parent.mkdir(parents=True)
        for key, value in (("UnitsPerMeter", "2\x00"), ("RetainGamePitchAndRoll", "true\x00")):
            with self.subTest(key=key):
                raw = f"[Tracking]\n{key}={value}\n".encode()
                self.personal.write_bytes(raw)
                self.cli("resolve", success=False)
                self.assertEqual(self.personal.read_bytes(), raw)

    @unittest.skipUnless(os.name == "nt", "Windows directory junction")
    def test_reparse_destination_refuses_without_touching_link_target(self):
        target = self.root / "external-target"
        target.mkdir()
        sentinel = target / "sentinel.txt"
        sentinel.write_text("preserve")
        link = self.root / "global"
        # Structured arguments through PowerShell, no interpolated command text.
        script = "param($profileLink,$profileTarget) New-Item -ItemType Junction -Path $profileLink -Target $profileTarget -ErrorAction Stop | Out-Null"
        helper = self.root / "junction.ps1"
        helper.write_text(script)
        child = subprocess.run(["pwsh", "-NoProfile", "-File", str(helper), str(link), str(target)], capture_output=True, text=True, timeout=8)
        self.assertEqual(child.returncode, 0, child.stderr)
        self.save("global", 0, "Tracking.UnitsPerMeter", "2", success=False)
        self.cli("resolve", success=False)
        self.assertEqual(list(target.iterdir()), [sentinel])
        self.assertEqual(sentinel.read_text(), "preserve")

    @unittest.skipUnless(os.name == "nt", "Windows replacement sharing failure")
    def test_atomic_replacement_failure_preserves_old_profile_and_cleans_stage(self):
        self.save("personal", 0, "Tracking.UnitsPerMeter", "2")
        old = self.personal.read_bytes()
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
        kernel.CreateFileW.restype = ctypes.c_void_p
        kernel.CloseHandle.argtypes = [ctypes.c_void_p]
        handle = kernel.CreateFileW(str(self.personal), 0x80000000, 1, None, 3, 0, None)
        self.assertNotEqual(handle, ctypes.c_void_p(-1).value)
        try:
            self.save("personal", 1, "Tracking.UnitsPerMeter", "3", success=False)
            self.assertEqual(old, self.personal.read_bytes())
            self.assertEqual(list(self.personal.parent.glob("*.tmp")), [])
        finally:
            kernel.CloseHandle(handle)
        self.save("personal", 1, "Tracking.UnitsPerMeter", "3")
        self.assertEqual(self.cli("resolve")["Tracking_UnitsPerMeter"], "3")

    @unittest.skipUnless(os.name == "nt", "Windows CreateFiles ACL")
    def test_staging_creation_failure_preserves_old_profile_and_restores_acl(self):
        helper = Path(__file__).with_name("profile_staging_failure.ps1")
        child = subprocess.run(["pwsh", "-NoProfile", "-File", str(helper), "-Controller", str(CONTROLLER)], capture_output=True, text=True, timeout=15)
        self.assertEqual(child.returncode, 0, child.stderr + child.stdout)
        result = json.loads(child.stdout)
        self.assertTrue(result["prior_profile_unchanged"])
        self.assertTrue(result["acl_restored"])
        self.assertEqual(result["staged_files"], 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
