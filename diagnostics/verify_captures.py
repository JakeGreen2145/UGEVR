import json
import argparse
import sys
from pathlib import Path
import numpy as np
from PIL import Image

def rotation(q):
    x,y,z,w = q
    return np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],
                     [2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],
                     [2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description='Check recorded pose/camera transforms; image metrics do not prove visual stereo.')
parser.add_argument('runs', nargs='+')
parser.add_argument('--output', type=Path, default=root/'evidence'/'capture-verification.json')
arguments = parser.parse_args()
paths = [root / 'evidence' / name for name in arguments.runs]
reports = []
S = np.diag([1.,1.,-1.])
for path in paths:
    for case in ('x','y','z','yaw','pitch','roll','reset'):
        folder = path / case
        captures = list(folder.glob('capture-*.json'))
        assert captures, folder
        pin = folder/'current-frame.txt'
        capture_file = folder / f'capture-{pin.read_text().strip()}.json' if pin.exists() else max(captures,key=lambda p:p.stat().st_mtime)
        captured = json.loads(capture_file.read_text(encoding='utf-8-sig'))
        runtime = json.loads((folder/'runtime.json').read_text(encoding='utf-8-sig'))
        assert runtime['valid'] and runtime['serial'] == 'unityvr-diagnostic-simulated-hmd-001'
        raw = np.asarray(runtime['transform']).reshape((3,4))
        expected_position = S @ raw[:,3]
        expected_rotation = S @ raw[:,:3] @ S
        head = np.asarray(captured['headPosition'])
        head_rotation = rotation(captured['headRotation'])
        assert np.max(np.abs(head-expected_position)) < 1e-5, (case,'runtime translation')
        assert np.max(np.abs(head_rotation-expected_rotation)) < 2e-5, (case,'runtime orientation')
        assert captured['gameCameraRestored'], (case,'camera not restored')
        base = np.asarray(captured['basePosition'])
        base_rotation = rotation(captured['baseRotation'])
        version = captured.get('evidenceVersion', 1)
        assert version in (1, 2), ('unsupported capture schema', version)
        scale = captured.get('unitsPerMeter', 1.)
        assert np.isfinite(scale) and scale > 0
        center = np.asarray(captured.get('originPosition', [0,1.7,0]))
        origin_rotation = rotation(captured.get('originRotation', [0,0,0,1]))
        if version == 2 and not captured['retainGameTilt']:
            forward = base_rotation[:,2]
            if forward[0]**2 + forward[2]**2 < 1e-12:
                right_axis = base_rotation[:,0]
                yaw = np.arctan2(-right_axis[2], right_axis[0])
            else:
                yaw = np.arctan2(forward[0], forward[2])
            base_rotation = rotation([0,np.sin(yaw/2),0,np.cos(yaw/2)])
        reference = base_rotation @ origin_rotation.T
        if version == 2:
            assert 'eyeToHead' in runtime, 'Version 2 needs full independently probed eye transforms.'
            assert captured['vrFrameId'] > 0 and captured['generation'] > 0
        errors=[]
        for eye,offset in enumerate(runtime['eyeOffsetX']):
            eye_raw = np.asarray(runtime['eyeToHead'][eye]).reshape((3,4)) if version == 2 else None
            eye_position = S @ eye_raw[:,3] if version == 2 else np.asarray([offset,0,0])
            eye_rotation = S @ eye_raw[:,:3] @ S if version == 2 else np.eye(3)
            expected_eye = base + reference @ ((head - center + head_rotation @ eye_position) * scale)
            error = np.max(np.abs(expected_eye - captured[f'eye{eye}Position']))
            tolerance = max(2e-5, float(np.max(np.abs(expected_eye))) * 2e-7)
            assert error < tolerance, (case,'eye transform',error)
            assert np.max(np.abs(rotation(captured[f'eye{eye}Rotation']) - reference@head_rotation@eye_rotation)) < 2e-5
            errors.append(float(error))
        ipd = float(np.linalg.norm(np.asarray(captured['eye0Position']) - captured['eye1Position']))
        if version == 2:
            expected_ipd = np.linalg.norm(S @ (np.asarray(runtime['eyeToHead'][0]).reshape((3,4))[:,3] - np.asarray(runtime['eyeToHead'][1]).reshape((3,4))[:,3])) * scale
        else:
            expected_ipd = .064
        assert abs(ipd-expected_ipd) < tolerance*2, (case,'eye separation',ipd)
        left=np.asarray(Image.open(folder/f"eye-0-{captured['frame']}.png").convert('RGB'),dtype=np.float32)
        right=np.asarray(Image.open(folder/f"eye-1-{captured['frame']}.png").convert('RGB'),dtype=np.float32)
        image_difference = float(np.mean(np.abs(left-right)))
        reports.append(dict(game=path.name,case=case,frame=captured['frame'],
            camera_restored=True,evidence_version=version,eye_separation_m=ipd/scale,eye_position_error_m=max(errors)/scale,
            left_right_image_mean_absolute_difference=image_difference,
            image_luma_mean=float(left.mean())))
output=arguments.output
scope=dict(scope='recorded_pose_and_camera_restoration',visual_stereo_validated=False,physical_gameplay_validated=False)
output.write_text(json.dumps(dict(passed=True,cases=len(reports),results=reports,**scope),indent=2))
print(json.dumps(dict(passed=True,cases=len(reports),output=str(output),**scope)))
