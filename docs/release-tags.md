# Tag and GitHub Release commands (do not run)

**Do not run these commands.** They are the list a human would use after the commits below actually exist. This file does not create tags. `scripts/print-release-commands.sh` only prints this file.

Nothing here is tagged yet. `gh release create` creates a tag when the tag is missing, so running it against the wrong checkout publishes a release. Check `git status` and `git rev-parse HEAD` first.

## OhhO-VR `v1.0.0`

Repository: `https://github.com/ohho-robotics/OhhO-VR`

`origin/main` today is `4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b`. That commit has no `.github/workflows/release.yml` and no `protocolVersion` check. Pushing `v1.0.0` there does not build a Quest APK and does not mean the headset accepts protocol `1.0.0`.

No commit currently contains all three of:

- `.github/workflows/release.yml` (branch `ohh-232-release-pipeline`, tip `6f87566db190045c2281333cbb703e486121c316`)
- `Assets/Scripts/Control/Preflight/ConnectionPreflight.cs` (branch `ohh-218-connection-preflight`, tip `b427a2d11055e3c1e817960369945a501234e9e9`)
- `Assets/Scripts/Telemetry/TelemetryV1.generated.cs` (branch `ohh-222-hud-v2`, tip `71ada80b723469b9900fa5246f75e16caf7f2074`)

When one commit does, and only then:

```bash
git fetch origin
git checkout <COMMIT>
test -f .github/workflows/release.yml
test -f Assets/Scripts/Control/Preflight/ConnectionPreflight.cs
test -f Assets/Scripts/Telemetry/TelemetryV1.generated.cs
git tag -a v1.0.0 <COMMIT> -m "OhhO-VR v1.0.0"
git push origin v1.0.0
```

If `<COMMIT>` contains `.github/workflows/release.yml`, that workflow creates the GitHub Release and attaches the APK. Do not also run `gh release create` for the same tag.

If you are cutting a notes-only release from a commit that does **not** contain `release.yml` (this is not the coordinated tag):

```bash
gh release create v1.0.0 \
  --repo ohho-robotics/OhhO-VR \
  --target <COMMIT> \
  --title "OhhO-VR v1.0.0" \
  --notes-file CHANGELOG.md
```

Tag push of a commit that contains `release.yml` needs the Unity and keystore secrets named in that branch's `docs/release.md`. This paperwork does not create a keystore.

## OmniBot VR profile `vr-profile-v1.0.0`

Repository: `https://github.com/ohho-robotics/OmniBot`

`origin/main` today is `d296c9be6cc898e11e8f671c2457292b30cec905`. It has no `sim_vr.launch.py`.

No commit currently contains all three of `sim_vr.launch.py`, `robot_vr.launch.py`, and `docker-compose.gpu.yml`.

- `b010b84c2bab7ec4f11f928af1b20be1fc31cc95` (`ohh-220-real-vr-profile`) has `sim_vr.launch.py` and `robot_vr.launch.py`. It has no `docker-compose.gpu.yml`.
- `ddb4beefd3e81f9c6ae0d21f75a9b0820023de05` (`ohh-204-sim-gpu-container`) has `sim_vr.launch.py` and `docker-compose.gpu.yml`. It has no `robot_vr.launch.py`.

When one commit has all three:

```bash
git fetch origin
git checkout <COMMIT>
test -f omnibot-ros2/omnibot_bringup/launch/sim_vr.launch.py
test -f omnibot-ros2/omnibot_bringup/launch/robot_vr.launch.py
test -f docker-compose.gpu.yml
git tag -a vr-profile-v1.0.0 <COMMIT> -m "OmniBot VR profile v1.0.0"
git push origin vr-profile-v1.0.0
gh release create vr-profile-v1.0.0 \
  --repo ohho-robotics/OmniBot \
  --target <COMMIT> \
  --title "OmniBot VR profile v1.0.0" \
  --notes-file CHANGELOG.md
```

Do not tag this `v1.0.0`. Do not push the tag to `main`.

The container tag is separate. The GPU compose file still pulls `ghcr.io/ohho-robotics/omnibot-sim:latest`. The intended coordinated name, still a placeholder, is:

`ghcr.io/ohho-robotics/omnibot-sim:vr-1.0.0`

This sheet does not include `docker push`. CI on `ohh-204` does not push a `vr-*` tag.

## ohho-os protocol pin `protocol-v1.0.0`

Repository: `https://github.com/ohho-robotics/ohho-os`

`origin/main` is `85db848268168fa27b4ebb1b5992220b3bfacfcd`, package version `1.1.3`. `PROTOCOL_VERSION` is not in that commit. It is on `ohh-174-protocol-version` at `6f690d50b5991b13a35430581f84626529ab870d`.

Do not run `git tag v1.0.0` or any other `v*` tag for this pin. A `v*` push runs `.github/workflows/release.yml` and publishes PyPI when the tag matches `pyproject.toml`.

```bash
git fetch origin
git checkout <COMMIT>
python -c "import ast,pathlib; t=pathlib.Path('ohho/protocol.py').read_text(); assert 'PROTOCOL_VERSION = \"1.0.0\"' in t"
git tag -a protocol-v1.0.0 <COMMIT> -m "ohho-os protocolVersion 1.0.0"
git push origin protocol-v1.0.0
gh release create protocol-v1.0.0 \
  --repo ohho-robotics/ohho-os \
  --target <COMMIT> \
  --title "ohho-os protocolVersion 1.0.0" \
  --notes-file CHANGELOG.md
```

`<COMMIT>` must contain `ohho/protocol.py` and `schemas/manifest.json` with `"protocolVersion": "1.0.0"`. That manifest is the engine wire pin. It is not `ohho.telemetry.v1`.

The ohho-os loader for `RobotDescriptor` is a different branch tip: `389c589cd00619cd12b18b97c636d6fac556337e` (`ohh-214-robot-descriptor`). It is not an ancestor check you can skip. Do not assume `6f690d50` contains it.

## ohho-sdk schemas `schemas-v1.0.0`

Repository: `https://github.com/ohho-robotics/ohho-sdk` (npm `@ohho/schemas`, not the ohho-os Python package).

Branch `ohh-214-robot-descriptor` at `5a20f1c23c10c814b8a790536a80b21695b3ed82` has `ohho.telemetry.v1` version `1.0.0`, `ohho.descriptor.v1`, and `PINNED_PROTOCOL_VERSION` `1.0.0`. `@ohho/schemas` `package.json` on that commit says `"version": "1.0.0"`. This checkout has no git tags. Confirm npm before treating that version as published; this paperwork does not publish it.

```bash
git fetch origin
git checkout <COMMIT>
test -f packages/schemas/ohho.telemetry.v1/manifest.json
test -f packages/schemas/ohho.descriptor.v1/descriptor.json
git tag -a schemas-v1.0.0 <COMMIT> -m "ohho-sdk schemas 1.0.0"
git push origin schemas-v1.0.0
gh release create schemas-v1.0.0 \
  --repo ohho-robotics/ohho-sdk \
  --target <COMMIT> \
  --title "ohho-sdk schemas 1.0.0" \
  --notes-file CHANGELOG.md
```

Do not `npm publish` from this sheet.
