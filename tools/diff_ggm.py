"""Compares two globalgamemanagers object by object, field by field."""
import sys

import UnityPy


def dump(path):
    env = UnityPy.load(path)
    out = []
    for obj in env.objects:
        try:
            out.append((obj.type.name, obj.read_typetree()))
        except Exception as exc:  # noqa: BLE001
            out.append((obj.type.name, {"__ERROR__": str(exc)}))
    return out


def walk(a, b, path, diffs):
    if type(a) is not type(b):
        diffs.append("%s: type %s != %s" % (path, type(a).__name__, type(b).__name__))
        return
    if isinstance(a, dict):
        for k in sorted(set(a) | set(b)):
            if k not in a:
                diffs.append("%s.%s: missing from A" % (path, k))
            elif k not in b:
                diffs.append("%s.%s: missing from B" % (path, k))
            else:
                walk(a[k], b[k], "%s.%s" % (path, k), diffs)
    elif isinstance(a, (list, tuple)):
        if len(a) != len(b):
            diffs.append("%s: len %d != %d  (A=%r B=%r)" % (path, len(a), len(b), a, b))
            return
        for i, (x, y) in enumerate(zip(a, b)):
            walk(x, y, "%s[%d]" % (path, i), diffs)
    elif a != b:
        diffs.append("%s: %r != %r" % (path, a, b))


def main():
    orig, patched = sys.argv[1], sys.argv[2]
    A, B = dump(orig), dump(patched)
    print("objects: %d vs %d" % (len(A), len(B)))
    if len(A) != len(B):
        raise SystemExit("different object count!")

    diffs = []
    for i, ((na, da), (nb, db)) in enumerate(zip(A, B)):
        if na != nb:
            diffs.append("obj[%d]: type %s != %s" % (i, na, nb))
            continue
        walk(da, db, na, diffs)

    if not diffs:
        print("IDENTICAL (no differences)")
        return
    print("%d difference(s):" % len(diffs))
    for d in diffs:
        print("  -", d)


if __name__ == "__main__":
    main()
