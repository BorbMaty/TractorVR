"""Build environment assets: Blender -> Assets/Models/Env/<Asset>.fbx (+ .json with the numbers).

Headless (reproducible, like ProjectSetup):
    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup -P tools/blender/build.py -- barn
    ... -- all
In a running Blender (blender-mcp): exec(open(r"<repo>/tools/blender/build.py").read(), {"ASSETS": ["barn"]})
"""
import importlib
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent if "__file__" in globals() else None
if HERE is None:   # exec() from blender-mcp: the caller passes the repo path in REPO
    HERE = pathlib.Path(globals()["REPO"]) / "tools" / "blender"
sys.path.insert(0, str(HERE))

import tvr  # noqa: E402

ALL = ["barn", "shed", "farmhouse"]


def main(names):
    importlib.reload(tvr)
    if names == ["all"]:
        names = ALL
    results = {}
    for name in names:
        mod = importlib.import_module(name)
        importlib.reload(mod)
        results[name] = tvr.build_asset(name.capitalize(), mod.describe, getattr(mod, "NOTES", ""))
    return results


if __name__ == "__main__" and "--" in sys.argv:
    main(sys.argv[sys.argv.index("--") + 1:] or ["all"])
elif "ASSETS" in globals():
    main(globals()["ASSETS"])
