# MGSV ReShade Anti-Hook Patch

For the English Steam executable of **Metal Gear Solid V: The Phantom Pain
1.0.15.4** only.

## What the hell is happening?

FOX Engine checks whether its D3D11 interfaces have been hooked. ReShade has to
hook/proxy those interfaces, so the check fires and MGSV deliberately terminates
itself during startup. This looks like a ReShade crash, but it is the game
closing itself before normal swap-chain initialization.

This patch changes the branch after `fox::gr::dg::CheckModuleHook` so MGSV ignores
the hook-detection result:

- file offset: `0x2B90AB`
- original bytes: `75 2D`
- patched bytes: `EB 2D`

It does not patch ReShade, the NVIDIA driver, MGSVFix, or any system DLL.

## Installation

1. Close MGSV.
2. Put `MGSV-ReShade-AntiHook-Patcher.exe` next to `mgsvtpp.exe`.
3. Run the patcher once.
4. Install ReShade with full add-on support for `mgsvtpp.exe` using DirectX
   10/11/12. ReShade should be installed as `dxgi.dll`.

The patcher accepts only this original executable:

- size: `166,517,760` bytes
- SHA-256: `085C2F82D1C963C40B3D2D55786661DFEE2B18CBBF388A710C00FA76C5E9BB45`

Expected patched SHA-256:

- `184E0D1ABEC30561EEE4650CB7F913E838692BA30233E8AAB5DCBCE522D8C297`

It creates `mgsvtpp.exe.anti-hook-backup` before changing anything. Unknown or
already modified executables are rejected.

Steam verification or a game update may restore the original executable, in
which case the patch must be applied again after confirming the game version.

## Credits

The FOX Engine hook check and version-specific offsets were documented by the
MGSV modding community:

https://mgsvmoddingwiki.github.io/Attaching_graphics_debuggers/
