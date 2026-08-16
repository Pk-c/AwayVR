"""
Prepares a Unity 2017.x build for VR by editing globalgamemanagers.

Two fields, both inside BuildSettings:

  * enabledVRDevices: the list of VR SDKs compiled into the build. Empty in a non-VR
    build, and XRSettings.LoadDeviceByName fails for as long as it stays that way.

  * m_GraphicsAPIs: Unity 2017's built-in VR only works under Direct3D 11. Away exposes
    [18, 2] = [D3D12, D3D11] and therefore starts on D3D12, where the VR subsystem
    refuses to initialise without any error message at all.

Optionally --no-graphics-jobs turns off PlayerSettings.graphicsJobs, known to be
unstable with VR on this generation of Unity.

Usage:
    python patch_ggm.py <path to globalgamemanagers> [options]
    python patch_ggm.py <path to globalgamemanagers> --restore
"""
import argparse
import os
import shutil
import sys

import UnityPy

# UnityEngine.Rendering.GraphicsDeviceType
GFX = {
    "OpenGL2": 0,
    "Direct3D9": 1,
    "Direct3D11": 2,
    "Metal": 16,
    "OpenGLCore": 17,
    "Direct3D12": 18,
    "Vulkan": 21,
}
GFX_NAME = dict((v, k) for k, v in GFX.items())


def _objects(env):
    out = {}
    for obj in env.objects:
        if obj.type.name in ("BuildSettings", "PlayerSettings"):
            out[obj.type.name] = obj
    return out


def describe(path):
    env = UnityPy.load(path)
    objs = _objects(env)
    info = {}
    if "BuildSettings" in objs:
        t = objs["BuildSettings"].read_typetree()
        info["enabledVRDevices"] = t.get("enabledVRDevices")
        apis = t.get("m_GraphicsAPIs") or []
        info["m_GraphicsAPIs"] = [(a, GFX_NAME.get(a, "?")) for a in apis]
    if "PlayerSettings" in objs:
        t = objs["PlayerSettings"].read_typetree()
        info["graphicsJobs"] = t.get("graphicsJobs")
        info["m_StereoRenderingPath"] = t.get("m_StereoRenderingPath")

    for obj in env.objects:
        if obj.type.name == "InputManager":
            names = [a.get("m_Name") for a in obj.read_typetree().get("m_Axes", [])]
            info["axes_vr"] = [n for n, _ in AXES_VR if n in names]
            break
    return info


# Extra joystick axes.
#
# The Touch grips are ANALOG: SteamVR's legacy binding declares them in "trigger" mode,
# with a continuous axis2_value1 and an axis2_press that only flips at the very end of
# their travel. Hence grips that seem unresponsive unless you crush them. Reading the
# continuous value means declaring an axis on the Unity side.
#
# Indices confirmed with the controller probe: sweeping axes 10 through 19 moved only 10
# and 11, across their full travel up to 1.00. That follows the game's own triggers,
# already on 8 and 9.
AXES_VR = [("AwayVR_GripL", 10), ("AwayVR_GripR", 11)]


def add_axes(tree):
    """Adds the missing VR axes, cloning the schema of an existing joystick axis."""
    axes = tree.get("m_Axes") or []

    model = None
    for a in axes:
        if a.get("type") == 2:
            model = a
            break
    if model is None:
        raise SystemExit("no joystick axis to clone in the InputManager")

    existing = set(a.get("m_Name") for a in axes)
    added = []
    for name, index in AXES_VR:
        if name in existing:
            continue
        a = dict(model)
        a["m_Name"] = name
        a["descriptiveName"] = "AwayVR"
        a["axis"] = index
        a["joyNum"] = 0
        # Near-zero dead zone: we want the RAW value. The useful threshold is applied in
        # the mod, where it is adjustable, not frozen into the game's own file.
        a["dead"] = 0.001
        a["invert"] = False
        axes.append(a)
        added.append("%s -> joystick axis %d" % (name, index))

    tree["m_Axes"] = axes
    return added


def patch(path, devices, apis, disable_graphics_jobs):
    backup = path + ".orig"
    if not os.path.exists(backup):
        shutil.copy2(path, backup)
        print("backup -> %s" % backup)

    # Always start again from the original, which keeps the patch idempotent.
    env = UnityPy.load(backup)
    objs = _objects(env)

    bs = objs.get("BuildSettings")
    if bs is None:
        raise SystemExit("BuildSettings not found")
    tree = bs.read_typetree()
    tree["enabledVRDevices"] = list(devices)
    if apis:
        tree["m_GraphicsAPIs"] = list(apis)
    bs.save_typetree(tree)

    im = None
    for obj in env.objects:
        if obj.type.name == "InputManager":
            im = obj
            break
    if im is not None:
        t = im.read_typetree()
        added = add_axes(t)
        im.save_typetree(t)
        for a in added:
            print("axis added: %s" % a)

    if disable_graphics_jobs:
        ps = objs.get("PlayerSettings")
        if ps is None:
            raise SystemExit("PlayerSettings not found")
        t = ps.read_typetree()
        t["graphicsJobs"] = False
        ps.save_typetree(t)

    data = env.file.save(packer="none")
    # Written in place: MoveFileEx is refused on some Steam folders, and the .orig backup
    # covers the risk.
    with open(path, "wb") as fh:
        fh.write(data)
        fh.flush()
        os.fsync(fh.fileno())


def verify(path, devices, apis):
    """Re-reads the patched file in full and checks the targeted fields."""
    env = UnityPy.load(path)
    count = 0
    for obj in env.objects:
        try:
            tree = obj.read_typetree()
        except Exception as exc:  # noqa: BLE001
            print("  UNREADABLE %s: %s" % (obj.type.name, exc))
            return False
        count += 1
        if obj.type.name == "BuildSettings":
            if list(tree.get("enabledVRDevices")) != list(devices):
                print("  enabledVRDevices=%s" % (tree.get("enabledVRDevices"),))
                return False
            if apis and list(tree.get("m_GraphicsAPIs")) != list(apis):
                print("  m_GraphicsAPIs=%s" % (tree.get("m_GraphicsAPIs"),))
                return False
    print("  %d objects re-read without error" % count)
    return True


def show(prefix, path):
    info = describe(path)
    print("%s:" % prefix)
    for k, v in info.items():
        print("    %-22s %s" % (k, v))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("path")
    ap.add_argument("--devices", default="None,OpenVR")
    ap.add_argument("--apis", default="Direct3D11,Direct3D12",
                    help="Graphics API order; the first supported one wins.")
    ap.add_argument("--no-graphics-jobs", action="store_true",
                    help="Turns off PlayerSettings.graphicsJobs.")
    ap.add_argument("--restore", action="store_true")
    ap.add_argument("--show", action="store_true")
    args = ap.parse_args()

    if args.show:
        show("state", args.path)
        return

    if args.restore:
        backup = args.path + ".orig"
        if not os.path.exists(backup):
            raise SystemExit("no backup at %s" % backup)
        shutil.copy2(backup, args.path)
        show("restored", args.path)
        return

    devices = [d for d in args.devices.split(",") if d]
    apis = []
    for name in args.apis.split(","):
        name = name.strip()
        if not name:
            continue
        if name not in GFX:
            raise SystemExit("unknown API '%s' (expected: %s)" % (name, ", ".join(sorted(GFX))))
        apis.append(GFX[name])

    show("before", args.path)
    patch(args.path, devices, apis, args.no_graphics_jobs)
    print("verification:")
    if not verify(args.path, devices, apis):
        shutil.copy2(args.path + ".orig", args.path)
        raise SystemExit("VERIFICATION FAILED - file restored")
    show("after", args.path)
    print("OK")


if __name__ == "__main__":
    sys.exit(main())
