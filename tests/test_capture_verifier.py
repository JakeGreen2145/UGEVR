"""Exercise the verifier CLI with independent recorded geometry, without a runtime."""
import json
import math
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]


class CaptureVerifierChecks(unittest.TestCase):
    def fixture(self, directory, version):
        run = directory / 'run'
        for name in ('x', 'y', 'z', 'yaw', 'pitch', 'roll', 'reset'):
            folder = run / name
            folder.mkdir(parents=True)
            runtime = dict(valid=True, serial='unityvr-diagnostic-simulated-hmd-001',
                           transform=[1, 0, 0, 0, 0, 1, 0, 1.7, 0, 0, 1, 0],
                           eyeOffsetX=[-.032, .032])
            captured = dict(frame=1, headPosition=[0, 1.7, 0], headRotation=[0, 0, 0, 1],
                            basePosition=[0, 0, 0], baseRotation=[0, 0, 0, 1],
                            eye0Position=[-.032, 0, 0], eye1Position=[.032, 0, 0],
                            eye0Rotation=[0, 0, 0, 1], eye1Rotation=[0, 0, 0, 1],
                            gameCameraRestored=True)
            if version == 2:
                # Head is centered, world scale is two, origin yaw is 90 degrees.
                # Default removal of a 45-degree game pitch leaves a horizontal anchor.
                captured.update(evidenceVersion=2, vrFrameId=7, generation=2,
                                unitsPerMeter=2, retainGameTilt=False,
                                originPosition=[0, 1.7, 0],
                                originRotation=[0, math.sqrt(.5), 0, math.sqrt(.5)],
                                baseRotation=[math.sin(math.pi/8), 0, 0, math.cos(math.pi/8)],
                                eye0Position=[0, 0, -.064], eye1Position=[0, 0, .064])
                runtime['eyeToHead'] = []
                for eye, yaw in enumerate((10, -10)):
                    angle = math.radians(yaw)
                    # Raw OpenVR right-handed yaw is the inverse Unity yaw.
                    runtime['eyeToHead'].append([math.cos(angle), 0, -math.sin(angle),
                                                -.032 if eye == 0 else .032,
                                                0, 1, 0, 0, math.sin(angle), 0, math.cos(angle), 0])
                    total = math.radians(yaw-90)
                    captured[f'eye{eye}Rotation'] = [0, math.sin(total/2), 0, math.cos(total/2)]
            (folder/'runtime.json').write_text(json.dumps(runtime))
            (folder/'capture-1.json').write_text(json.dumps(captured))
            for eye in (0, 1):
                Image.new('RGB', (8, 8), ('red', 'blue')[eye]).save(folder/f'eye-{eye}-1.png')
        return run

    def verify(self, run, directory):
        return subprocess.run([sys.executable, str(ROOT/'diagnostics/verify_captures.py'),
                               str(run), '--output', str(directory/'report.json')],
                              capture_output=True, text=True, timeout=20)

    def test_legacy_records_remain_valid(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            result = self.verify(self.fixture(directory, 1), directory)
            self.assertEqual(result.returncode, 0, result.stderr)
            report = json.loads(result.stdout)
            self.assertEqual(report['cases'], 7)
            self.assertFalse(report['visual_stereo_validated'])
            self.assertFalse(report['physical_gameplay_validated'])

    def test_canted_scaled_origin_and_tilt_policy(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            result = self.verify(self.fixture(directory, 2), directory)
            self.assertEqual(result.returncode, 0, result.stderr)
            report = json.loads((directory/'report.json').read_text())
            self.assertAlmostEqual(report['results'][0]['eye_separation_m'], .064)

    def test_wrong_canted_orientation_fails(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            run = self.fixture(directory, 2)
            capture = run/'x/capture-1.json'
            value = json.loads(capture.read_text())
            value['eye0Rotation'] = [0, 0, 0, 1]
            capture.write_text(json.dumps(value))
            self.assertNotEqual(self.verify(run, directory).returncode, 0)

    def test_missing_independent_eye_transform_fails(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            run = self.fixture(directory, 2)
            probe = run/'x/runtime.json'
            value = json.loads(probe.read_text())
            del value['eyeToHead']
            probe.write_text(json.dumps(value))
            self.assertNotEqual(self.verify(run, directory).returncode, 0)


if __name__ == '__main__':
    unittest.main()
