"""Read-only measurement of generated sprite sources; never writes image pixels."""
import importlib.util
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[4]
source = root / "Docs/Art/PlayerMotionV2/CalmRevision/inspect_geometry.py"
spec = importlib.util.spec_from_file_location("motion_geometry", source)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
result = module.inspect(sys.argv[1], int(sys.argv[2]), int(sys.argv[3]))
for frame in result["frames"]:
    frame.pop("faces", None)
print(json.dumps(result, ensure_ascii=False, separators=(",", ":")))
