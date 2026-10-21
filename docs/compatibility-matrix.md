# Coordinated v1 compatibility matrix

**Status: not yet tagged.** Drafted 2026-10-14 on branch `ohh-249-release-tags`.

This page is paperwork. It does not create git tags, GitHub Releases, container images, or package publishes. No OhhO-VR tag exists. OmniBot has no git tags. ohho-os already has package tags `v1.1.1` and `v1.1.2` only; those are not this pin. The sim image tag below is a placeholder.

There is no single commit that contains every row. Tagging `origin/main` of any of these repos does not produce the coordinated set.

| Piece | Intended name | Version pin in code | Where that pin lives today | On `origin/main`? |
|---|---|---|---|---|
| OhhO-VR app | git tag `v1.0.0` | `bundleVersion: 1.0` in `ProjectSettings/ProjectSettings.asset` on this commit. A later tag workflow (not in this commit) would set `bundleVersion` from the tag, so `v1.0.0` becomes `1.0.0`. | This paperwork commit is `4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b` (`origin/main`, merge of PR #5). Quest APK workflow: OhhO-VR `ohh-232-release-pipeline` at `6f87566db190045c2281333cbb703e486121c316` (has `.github/workflows/release.yml`, does not check `protocolVersion`). | The headset client is on `origin/main`. The tag workflow is not. |
| `protocolVersion` | wire string `1.0.0` | `PROTOCOL_VERSION = "1.0.0"` in ohho-os `ohho/protocol.py`. `@ohho/schemas` exports `PINNED_PROTOCOL_VERSION = "1.0.0"`. Headset preflight constant `PinnedProtocolVersion = "1.0.0"` with pattern `^1\.0\.(0\|[1-9]\d*)$`. | ohho-os branch `ohh-174-protocol-version` at `6f690d50b5991b13a35430581f84626529ab870d`. ohho-sdk branch `ohh-214-robot-descriptor` at `5a20f1c23c10c814b8a790536a80b21695b3ed82` (`packages/schemas/src/protocol-version.generated.ts`). Headset check: OhhO-VR `ohh-218-connection-preflight` at `b427a2d11055e3c1e817960369945a501234e9e9`. | No. ohho-os `origin/main` is `85db848268168fa27b4ebb1b5992220b3bfacfcd` and its package version is `1.1.3` with no `PROTOCOL_VERSION`. |
| `ohho.telemetry.v1` | schema id `ohho.telemetry.v1` | `packages/schemas/ohho.telemetry.v1/manifest.json` says `"version": "1.0.0"`. | Added on ohho-sdk in `d3b54df9ec8310f56c77e34ce31c98887bfbf5b5`, contained in `ohh-214-robot-descriptor` at `5a20f1c23c10c814b8a790536a80b21695b3ed82`. Headset HUD reader: OhhO-VR `ohh-222-hud-v2` at `71ada80b723469b9900fa5246f75e16caf7f2074`. | No. Not in OhhO-VR `origin/main`. Not in ohho-os `origin/main`. |
| `RobotDescriptor` | schema `ohho.descriptor.v1` | `descriptor.json` title `RobotDescriptor`. Field `v` accepts `1.0.x` only. Samples name `schema_id` `ohho.telemetry.v1`. | ohho-sdk `5a20f1c23c10c814b8a790536a80b21695b3ed82`. OmniBot publisher: `ohh-214-robot-descriptor` at `764e10a49c43d8c3a290a6f70a8c35d1c3179216`. ohho-os loader: `ohh-214-robot-descriptor` at `389c589cd00619cd12b18b97c636d6fac556337e`. Headset parse: OhhO-VR `b427a2d11055e3c1e817960369945a501234e9e9`. | No. |
| OmniBot VR profile | git tag `vr-profile-v1.0.0` | Launch files `omnibot_bringup` `sim_vr.launch.py` (Gazebo, `vr:=true`) and `robot_vr.launch.py` (real robot). | `ohh-220-real-vr-profile` at `b010b84c2bab7ec4f11f928af1b20be1fc31cc95` contains both launch files. It includes the earlier sim profile `ohh-198-sim-vr-profile` at `fd1dcdcc227d08589f28c61bfc2cbc58a6ce8d32`. It does not contain `docker-compose.gpu.yml`. | No. OmniBot `origin/main` is `d296c9be6cc898e11e8f671c2457292b30cec905` and has neither launch file. |
| Sim container | `ghcr.io/ohho-robotics/omnibot-sim:vr-1.0.0` | Placeholder for this paperwork. The compose file on the GPU branch still says `ghcr.io/ohho-robotics/omnibot-sim:latest`. That file's comment says CI does not push, and the manual form it names is `ghcr.io/ohho-robotics/omnibot-sim:vr-<sha>`. | OmniBot `ohh-204-sim-gpu-container` at `ddb4beefd3e81f9c6ae0d21f75a9b0820023de05` (`docker-compose.gpu.yml` service `sim-vr` runs `sim_vr.launch.py`). That commit is not an ancestor of `b010b84` and does not contain `robot_vr.launch.py`. `.github/workflows/docker-sim.yml` on that branch publishes `:latest` and `:<sha>` only for a push to `main`. The workflow is not on OmniBot `origin/main`. | No. `vr-1.0.0` is not a published image tag. |

## How the rows are meant to match

A coordinated set, once a human has integrated it, is:

1. An OhhO-VR commit tagged `v1.0.0` whose preflight accepts `protocolVersion` `1.0.x` and whose HUD reads `ohho.telemetry.v1`.
2. ohho-os `PROTOCOL_VERSION` `1.0.0` and ohho-sdk `PINNED_PROTOCOL_VERSION` `1.0.0`, with `RobotDescriptor` / `ohho.descriptor.v1` and `ohho.telemetry.v1` at schema `1.0.0`.
3. An OmniBot commit tagged `vr-profile-v1.0.0` that contains `sim_vr.launch.py`, `robot_vr.launch.py`, and the GPU compose service.
4. A container published as `ghcr.io/ohho-robotics/omnibot-sim:vr-1.0.0` built from that OmniBot commit.

None of those four exist today. `ohh-218`, `ohh-222`, and `ohh-232` on OhhO-VR do not contain each other. `ohh-220` and `ohh-204` on OmniBot do not contain each other.

## Tags that must not be reused

- Do not push a `v*` tag on ohho-os for `protocolVersion` `1.0.0`. `.github/workflows/release.yml` on ohho-os `origin/main` publishes to PyPI on `v*` and requires the tag to match `pyproject.toml` (`1.1.3`). Existing tags: `v1.1.1`, `v1.1.2`. The paperwork name for the protocol commit is `protocol-v1.0.0`.
- Do not use git tag `v1.0.0` on OmniBot for the VR profile. That name is the whole-repo tag other drafts talk about. This profile uses `vr-profile-v1.0.0`, which does not start with `v` and does not match a `v*` workflow filter.
- ohho-os `schemas/manifest.json` on `ohh-174` (`protocolVersion` `1.0.0`, package note `ohhoOsVersion` `1.1.3`) is the engine wire pin. It is not the `ohho.telemetry.v1` package. That package lives in the ohho-sdk repository.

## Commands

The copy-paste commands a human would run later are in [release-tags.md](release-tags.md). Do not run them from this draft.
