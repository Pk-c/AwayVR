Third-party binaries redistributed with AwayVR
=============================================

`tools/pack.ps1` takes these from here rather than from the game folder, so that a
fresh clone of the repository can build the archive without a working install.

| File | Origin | Licence |
|---|---|---|
| `openvr_api.dll` | Valve OpenVR SDK | BSD-3-Clause, see `openvr-LICENSE.txt` |

Both licences require the notice to travel with the binary, so the licence files are
shipped inside the archive as well, not only kept here.

BepInEx is **not** vendored. Its own release archive is the supported way to obtain it,
and pinning a copy here would leave users on whatever version we happened to commit.
`tools/pack.ps1` takes it from the game folder; see the script for how to obtain it if
you are packaging from a clean machine.
