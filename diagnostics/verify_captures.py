import json
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
paths = [root / 'evidence' / name for name in sys.argv[1:]]
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
        errors=[]
        for eye,offset in enumerate(runtime['eyeOffsetX']):
            expected_eye = base + base_rotation @ (head - [0,1.7,0] + head_rotation @ [offset,0,0])
            error = np.max(np.abs(expected_eye - captured[f'eye{eye}Position']))
            assert error < 2e-5, (case,'eye transform',error)
            assert np.max(np.abs(rotation(captured[f'eye{eye}Rotation']) - base_rotation@head_rotation)) < 2e-5
            errors.append(float(error))
        ipd = float(np.linalg.norm(np.asarray(captured['eye0Position']) - captured['eye1Position']))
        assert abs(ipd-.064) < 1e-5, (case,'eye separation',ipd)
        left=np.asarray(Image.open(folder/f"eye-0-{captured['frame']}.png").convert('RGB'),dtype=np.float32)
        right=np.asarray(Image.open(folder/f"eye-1-{captured['frame']}.png").convert('RGB'),dtype=np.float32)
        image_difference = float(np.mean(np.abs(left-right)))
        reports.append(dict(game=path.name,case=case,frame=captured['frame'],
            camera_restored=True,eye_separation_m=ipd,eye_position_error_m=max(errors),
            left_right_image_mean_absolute_difference=image_difference,
            image_luma_mean=float(left.mean())))
output=root/'evidence'/'capture-verification.json'
output.write_text(json.dumps(dict(passed=True,cases=len(reports),results=reports),indent=2))
print(json.dumps(dict(passed=True,cases=len(reports),output=str(output))))
