# OHH-250: Merge order for the PRs the VR project depends on

Checked 14 October 2026 against GitHub and by test-merging each pull request, in the order below, onto a local scratch branch. Scratch branches were not pushed. No pull request was merged and no other PR branch was edited.

Linear: https://linear.app/ohho-robotics/issue/OHH-250

**Merges are Varun-only.** This document is a plan. It does not authorize an agent to merge, approve, rotate credentials, or rewrite `.gitleaksignore` on a PR branch.

## Recommended order

Each row is one merge into that repo's `main`, after the rebase in the last column. "Clean" means `git merge` completed with no conflict markers on top of the previous rows in the same repo.

| Step | Repo | PR | Linear | Merge when | Stack result on 14 Oct 2026 |
| --- | --- | --- | --- | --- | --- |
| 1 | [ohho-sdk #1](https://github.com/ohho-robotics/ohho-sdk/pull/1) | `ohh-172-ts-build-ci` | OHH-172 | After review | Clean onto `main` `493378937fe4` |
| 2 | [ohho-sdk #3](https://github.com/ohho-robotics/ohho-sdk/pull/3) | `ohh-174-protocol-version` | OHH-174 | Immediately after step 1 | Clean. #3 already contains #1 (`15b3991220` is an ancestor of `630d781f8c`). After #1 is on `main`, the remaining delta is 24 files: `PINNED_PROTOCOL_VERSION = "1.0.0"`, `PINNED_OHHO_OS_VERSION = "1.1.3"`, and a CI step `npm run codegen:schemas -- --check` |
| 3 | [OmniBot #48](https://github.com/ohho-robotics/OmniBot/pull/48) | `ohh-190-gitleaks-allowlist` | OHH-190 | Before any other OmniBot PR in this list | Clean onto `main` `d296c9be6cc8`. gitleaks stays red. See [OHH-190](#ohh-190-gitleaks-stays-red) |
| 4 | [OmniBot #35](https://github.com/ohho-robotics/OmniBot/pull/35) | `ohh-175-cross-links` | OHH-175 | After step 3 | Clean |
| 5 | [OmniBot #43](https://github.com/ohho-robotics/OmniBot/pull/43) | `ohh-54-node-names-coverage` | OHH-54 | After step 4 | Clean, including `README.md`. This PR replaces the inline CPU pytest step with `scripts/run_cpu_coverage.py`. Every later PR that edits `.github/workflows/ci.yml` has to be rebased onto that. See [ci.yml rebases](#omnibot-ciyml-rebases) |
| 6 | [OmniBot #42](https://github.com/ohho-robotics/OmniBot/pull/42) | `ohh-104-frame-launch-doc` | OHH-104 | After step 5, after the ci.yml rebase | Conflict: `.github/workflows/ci.yml` only. `docs/reference.md` auto-merged |
| 7 | [OmniBot #30](https://github.com/ohho-robotics/OmniBot/pull/30) | `ohh-43-architecture-docs` | OHH-43 | After step 6, after the same style of ci.yml rebase | Conflict: `.github/workflows/ci.yml` only. `README.md` auto-merged |
| 8 | [OmniBot #33](https://github.com/ohho-robotics/OmniBot/pull/33) | `ohh-62-gazebo-nav-regression` | OHH-62 | After step 7 | Clean. Adds `.github/workflows/nav-regression-nightly.yml` and edits `nav-sim.yml`. It does not edit `ci.yml` |
| 9 | [OmniBot #38](https://github.com/ohho-robotics/OmniBot/pull/38) | `ohh-34-mcap-foxglove` | OHH-34 | After step 8 | Clean. `.gitignore` and `README.md` auto-merged |
| 10 | [OmniBot #34](https://github.com/ohho-robotics/OmniBot/pull/34) | `ohh-97-late-action-stop` | OHH-97 | After step 9. This is the code PR. Dataset capture stays with Varun | Clean |
| 11 | [OmniBot #40](https://github.com/ohho-robotics/OmniBot/pull/40) | `ohh-33-docker-sim-ghcr` | OHH-33 | After step 10. The smoke-step quoting fix is already the PR head | Conflict: `.github/workflows/ci.yml` only. `sim_navigation.launch.py` and `README.md` auto-merged. See [PR 40 quoting](#omnibot-40-quoting-fix) |
| 12 | [OmniBot #41](https://github.com/ohho-robotics/OmniBot/pull/41) | `ohh-53-serve-policy-mux` | OHH-53 | When Varun wants the mock Serve path. Real Serve and robot data are still open | Conflict: `.gitleaksignore` (add/add with #48). `policy_node.py` auto-merged with #34. See [allowlist](#gitleaksignore-on-41-and-36) |
| 13 | [OmniBot #36](https://github.com/ohho-robotics/OmniBot/pull/36) | `ohh-48-lerobot-recorder` | OHH-48 | When a real teleop capture exists. The code can be reviewed before that | Conflicts: `.github/workflows/ci.yml`, `.gitleaksignore`, `docs/reference.md` |
| 14 | [OmniBot #37](https://github.com/ohho-robotics/OmniBot/pull/37) | `ohh-51-act-train-config` | OHH-51 | When #36's dataset layout is accepted. GPU training, W&B, and the Hub upload stay with Varun | Conflicts: `.github/workflows/ci.yml`, `system_probe.py`, `lerobot_engine/models/registry.py` |
| 15 | [ohho-os #34](https://github.com/ohho-robotics/ohho-os/pull/34) | `ohh-130-go2-teleop-robot-backend` | OHH-130 | Any time after review. Different repo, so it does not wait on the OmniBot rebases | Clean onto `main` `85db84826816` |
| 16 | [OhhO-VR #6](https://github.com/ohho-robotics/OhhO-VR/pull/6) | `ohh-59-gameci-quest-teleop` | OHH-59 | After OHH-193's remaining safety fixes are on the branch that gets merged | Clean onto `main` `4ebc555a7495`. Do not merge it on that fact alone. See [OHH-193](#ohh-193-blocks-ohho-vr-6) |

Steps 12–14 can wait for data. Their conflicts with steps 3 and 5 do not depend on waiting: rebasing them the day after #43 and #48 land is still required.

OmniBot #53 (`ohh-197-arm-teleop-mux`, OHH-197) is **not** in the numbered list. It collides with step 5. Rebase it after #43 and before any headset build that publishes `/arm/joint_commands/teleop`. See [arm mux](#arm-mux-topic-rename-omnibot-53).

## GitHub "mergeable" is per PR against today's main

Every PR in the table is a **draft**, open, and `mergeable: MERGEABLE` against its current base (`main`, except where noted). That flag means the PR applies cleanly to today's `main`. It does not mean it applies cleanly after the previous row merges.

OmniBot `main` at the time of the test was `d296c9be6cc8` ("Merge pull request #29 from ohho-robotics/ohh-31-mux-test-fix"). The stack test branched from that commit and merged the PR heads below, in order.

## Per-PR status

CI is the latest rollup on each PR as of 14 October 2026. "gitleaks red" means the `gitleaks` check failed and the other listed jobs passed. On OmniBot that failure is the historical jwt finding described under OHH-190, unless the PR allowlists it.

| PR | Head | Draft | Base | Checks | Blocker | Who acts |
| --- | --- | --- | --- | --- | --- | --- |
| sdk #1 | `15b3991220` | yes | `main` | `verify` success. Merge state CLEAN | Review | Varun merges |
| sdk #3 | `630d781f8c` | yes | `main` (contains #1) | `verify` success. Merge state CLEAN | Merge #1 first so the GitHub diff shrinks to the protocol commit | Varun merges |
| OmniBot #48 | `8ed59506a5` | yes | `main` | gitleaks **fail** (1 leak, gitleaks 8.30.1, 69 commits, run [38556289274](https://github.com/ohho-robotics/OmniBot/actions/runs/38556289274)). CPU, README docs, colcon, sim-smoke pass. Merge state UNSTABLE | Varun rotates the Supabase anon key, then appends the one fingerprint documented in that PR. Agents do not allowlist it early | Varun |
| OmniBot #35 | `9e289e084a` | yes | `main` | gitleaks red. CPU, README docs, colcon, sim-smoke pass | None beyond #48 landing first and the shared gitleaks red | Varun merges |
| OmniBot #43 | `c055d22cc3` | yes | `main` | gitleaks red. CPU, nav-sim, README docs, colcon, sim-smoke pass | Fix the Linear URL (below). Rebase everything after it that touches `ci.yml` | Varun merges, then rebases the later PRs |
| OmniBot #42 | `2b60562c23` | yes | `main` | gitleaks red. CPU, nav-sim, README docs, colcon, sim-smoke pass | ci.yml rebase after #43 | Varun |
| OmniBot #30 | `2d52670b07` | yes | `main` | gitleaks red. CPU, README docs, colcon, sim-smoke pass | ci.yml rebase after #43/#42 | Varun |
| OmniBot #33 | `c4172b457d` | yes | `main` | gitleaks red. CPU, nav-sim, README docs, colcon, sim-smoke pass | None in the tested order. Nightly nav job is new and was not re-run as part of this docs task | Varun merges. Sim runtime check is his if he wants a Gazebo pass beyond the existing nav-sim check |
| OmniBot #38 | `8f117d399b` | yes | `main` | gitleaks red. CPU, nav-sim, README docs, colcon, sim-smoke pass | Sample MCAP is not in git. Publishing it is Varun's | Varun |
| OmniBot #34 | `17173cda89` | yes | `main` | gitleaks red. CPU, nav-sim, README docs, colcon, sim-smoke pass | None in the tested order. Re-test `policy_node` after #41 because the two edits auto-merged in the same function | Varun |
| OmniBot #40 | `76437fce5f` | yes | `main` | gitleaks red. **docker-sim job success** ([38547867848](https://github.com/ohho-robotics/OmniBot/actions/runs/38547867848)). CPU, nav-sim, README docs, colcon, sim-smoke pass | ci.yml rebase after #43. Quoting fix is already on the head. GHCR publish is manual | Varun |
| OmniBot #41 | `dc5df6c838` | yes | `main` | All green, including gitleaks, **because** commit `dc5df6c838` allowlists the historical jwt. Merge state CLEAN | Drop that allowlist commit when rebasing onto #48. Real Serve is not up. Body ID fix below | Varun |
| OmniBot #36 | `abe98b9e86` | yes | `main` | All green, including gitleaks, **because** commit `abe98b9e86` allowlists the same jwt. Merge state CLEAN | Same allowlist drop. Real-robot recording, sync-jitter numbers, and the dataset card are open | Varun records; code review can proceed |
| OmniBot #37 | `5f78e984c1` | yes | `main` | gitleaks red. CPU, README docs, colcon, sim-smoke pass | ci.yml, `system_probe.py`, and `registry.py` rebases. No GPU train, no W&B project, no Hub upload | Varun for the GPU run |
| ohho-os #34 | `910b598a9c` | yes | `main` | py3.10–3.13 on ubuntu, macos, and windows success; `demo_drive.py` vs fake ROS 2 success. Merge state CLEAN | On-bench Go2 DDS check was not done | Varun for hardware. Merge itself is his |
| OhhO-VR #6 | `458d2a8f5b` | yes | `main` | license, readme, editmode, build-android success (two runs each). "Test Results" is neutral. Merge state CLEAN | OHH-193 fixes are not committed yet. Headset confirmation remains | OHH-193 job writes the code. Varun merges and does the headset check |

## Conflicts found

Method: a throwaway worktree per repo, scratch branch at the `main` SHA above, then `git merge --no-ff` of each PR head in the table order. Conflicted paths are the unmerged paths `git diff --diff-filter=U` printed. Where a later PR needed a resolved tree to keep testing, the scratch worktree kept the earlier PR's behaviour and replayed the new PR's intent locally. Those scratch commits were not pushed.

### ohho-sdk #1 then #3

No conflicts. #3's merge-base with `main` includes all of #1, so the second merge only brings in `630d781f8c`.

### OmniBot, clean steps

- #48, #35, #43, #33, #38, #34: no conflict markers.
- #43 auto-merged `README.md` on top of #35.
- #38 auto-merged `.gitignore` and `README.md`.
- #40 auto-merged `omnibot-ros2/omnibot_navigation/launch/sim_navigation.launch.py` on top of #33. Both edits are present together: #33's `spawn_x` / `spawn_y` / `spawn_yaw` arguments, and #40's `autonavigate` / `foxglove` / `route` arguments. They sit in different regions of `_launch`.
- #41 auto-merged `policy_node.py` on top of #34. #34's full-tick timer (`tick_start` / `tick_dur` passed to `is_latency_exceeded`) is still in the function after #41's Serve wiring. Re-run `test_policy_guard.py` and `test_serve_policy_mux.py` after the rebase.

### OmniBot ci.yml rebases

#43 deletes the inline "CPU suites" pytest invocation and runs `python scripts/run_cpu_coverage.py`, and it adds `coverage` to the pip install. #42, #30, #40, #36, and #37 were all branched from the same pre-#43 `ci.yml` (`index e06cb15`). Each one edits that same step, so each conflicts once #43 is in the tree.

The conflict is one hunk: "ours" is the coverage job, "theirs" is the old pytest shell block with one added test path or one added pip package. Resolution for every one of these PRs:

1. Keep #43's step names: "CPU suites and line coverage" running `scripts/run_cpu_coverage.py`, plus the coverage artifact upload.
2. Do not restore the inline `pytest mecanum-kinematics/tests ...` shell step.
3. Port the new test into `scripts/run_cpu_coverage.py` instead.

Paths to add to the `pytest_tests` list in `run_cpu_coverage.py`:

| PR | Add |
| --- | --- |
| #42 | `omnibot-ros2/omnibot_bringup/test/test_frame_launch_docs.py` |
| #30 | `scripts/test_architecture_launch_table.py` |
| #40 | `omnibot-ros2/omnibot_bringup/test/test_docker_sim.py` |

#36 does not add a pytest path. Its hunk is the install block. Keep `coverage` from #43 and add #36's packages on that same install:

```yaml
          python -m pip install \
            -e mecanum-kinematics \
            -e omnibot-ai-engines/learning_engine \
            -e omnibot-ai-engines/agent_engine \
            -e yahboom-python-driver \
            ohho-os \
            pytest numpy pyarrow click pyyaml ruff coverage opencv-python-headless
```

#37 conflicts with that combined install and with the coverage step. Keep the coverage step. Extend the install with `pandas` and with #37's CPU torch line:

```yaml
            ohho-os \
            pytest numpy pyarrow click pyyaml ruff coverage opencv-python-headless pandas
          python -m pip install torch --index-url https://download.pytorch.org/whl/cpu
```

Also add a second coverage pytest invocation for `omnibot-ai-engines/lerobot_engine/tests` (that is the line #37 appended under the old "CPU suites" step). #37's `PYTHONPATH=omnibot-ai-engines` requirement is already how `run_cpu_coverage.py` launches the data-engine tests; set the same env for the lerobot-engine tests.

Suggested rebase for a branch that only touched `ci.yml` to add one test file. Example for #42 after #43 is on `main`:

```bash
git fetch origin
git checkout ohh-104-frame-launch-doc
git rebase origin/main
# when .github/workflows/ci.yml conflicts:
#   keep origin/main's coverage step
#   add omnibot-ros2/omnibot_bringup/test/test_frame_launch_docs.py
#   to pytest_tests in scripts/run_cpu_coverage.py
git add .github/workflows/ci.yml scripts/run_cpu_coverage.py
git rebase --continue
```

These PR branches are already on GitHub. Updating them after a rebase takes a force-with-lease push. That push is Varun's. This plan does not do it.

### docs/reference.md on #36

One hunk, two table rows that both changed since the branch point.

- Keep #43's CPU-job sentence (the cell that mentions `test-coverage.md` and line coverage).
- Take #36's LeRobot row, which points at `lerobot_record.launch.py` and keeps `teleop_record.launch.py`.

#42's edit to `docs/reference.md` auto-merged and did not need a hand resolution.

### gitleaksignore on #41 and #36

Both PRs **add** `.gitleaksignore`. #48 also **adds** it. Git reports `CONFLICT (add/add)`.

#48's file allowlists one fingerprint and explains it as a non-credential DevAgent GUID:

```text
187dec2cf0daa2145ef771bf696a0e5bed6dd865:omnibot-vr/Assets/Resources/DevAgentSettings.asset:generic-api-key:24
```

#41 (`dc5df6c838`, "allowlist 2 historical gitleaks findings") and #36 (`abe98b9e86`, same message) allowlist that line **and**:

```text
187dec2cf0daa2145ef771bf696a0e5bed6dd865:omnibot-vr/Assets/Resources/vr_manifest.json:jwt:21
```

That second fingerprint is the historical Supabase anon key #48 deliberately left un-ignored. Keep #48's file when rebasing #41 and #36. Drop those two allowlist commits from the rebased branches. After the drop, gitleaks on #41 and #36 goes red again until the rotation in #48 is done. Their current green gitleaks checks are not a reason to merge them ahead of #48.

### registry.py and system_probe.py on #37

`omnibot-ai-engines/lerobot_engine/models/registry.py` conflicts with #41's `make_policy`:

- #41 types `**kwargs: object` and, before `adapter.load(checkpoint, device)`, copies `serve_url`, `timeout_sec`, and `api_key` onto the adapter.
- #37 changes `checkpoint` to `Optional[str] = None` and calls `adapter.load(checkpoint, device, **kwargs)`.

Rebase result that keeps both:

- Leave `checkpoint` optional, as #37 has it.
- Pop `serve_url`, `timeout_sec`, and `api_key` out of `kwargs`, `setattr` them on the adapter, and pass only the remaining kwargs to `adapter.load`. Those three names are Serve client settings from #41. Forwarding them into `load()` would hand #37's loader unexpected keyword arguments.

`omnibot-ai-engines/learning_engine/benchmarks/system_probe.py` conflicts with #40. Both sides truncate the slug to 24 characters and strip a trailing hyphen. #40 writes it as two statements; #37 writes the same expression inline. Keep #40's two statements. Behaviour matches.

### Arm-mux topic rename (OmniBot #53)

Tested separately: merge `ohh-197-arm-teleop-mux` (`111ffbdbfe`) onto the scratch **after #43** (and after #48 and #35, which do not touch these files).

Unmerged file:

- `omnibot-ros2/omnibot_bringup/launch/master/omnibot_pi.launch.py`

The hunk is the comment above `arm_cmd_mux`:

- After #43: `Routes: policy | rl_arm → /arm/joint_commands/out → arm_driver` (`smolvla` renamed to `policy`).
- #53: `Routes: teleop | policy | rl_arm → /arm/joint_commands/out → arm_driver`.

Resolved comment: `Routes: teleop | policy | rl_arm → /arm/joint_commands/out → arm_driver`.

These shared files auto-merged (no markers): `docs/reference.md`, `omnibot_workstation.launch.py`, `mobile_manipulation.launch.py`, `omnibot-vr/Assets/Scripts/Core/RobotConfig.cs`. #53's topic change in `RobotConfig.cs` is `TOPIC_ARM_COMMANDS` from `/arm/joint_commands` to `/arm/joint_commands/teleop`. #43 only changes a nearby comment from `smolvla_node` to `policy_node`. #53's `mobile_manipulation.launch.py` comment describes `/arm/joint_commands/teleop` for VR/Android and `/arm/joint_commands/policy` (alias `/arm/joint_commands`) for the policy source.

#53 is `MERGEABLE` against today's `main`. The comment conflict appears only after #43. OhhO-VR #8 (`ohh-197-arm-teleop-mux`, headset side) is outside this sequence. Rebase it after OmniBot #53 so the headset publishes `/arm/joint_commands/teleop`.

### ohho-os #34 and OhhO-VR #6

No conflicts. OhhO-VR #6's merge touched 22 files (GameCI workflow, deadman/e-stop in the drive schemes, tests, README, scene setup). That clean merge is why the GitHub UI will offer a merge button. The safety follow-up is still a blocker. See the next section.

## OHH-190 gitleaks stays red

#48 removes the live anon key from the tree and documents the historical finding in `docs/security/gitleaks-pending-rotation.md`. The 14 October run still reports `leaks found: 1` and exits 1. The PR body says gitleaks stays red until Varun rotates that key. Do not clear it by taking the two-fingerprint allowlist from #36 or #41.

After rotation, #48's own doc says to append exactly one ignore line (the `vr_manifest.json` jwt fingerprint) and re-run gitleaks. This plan does not repeat the key material or the project host.

Until that check is green, every other OmniBot PR in this list that does not carry its own allowlist will stay UNSTABLE on gitleaks even when CPU, colcon, and sim-smoke pass. Merging them does not fix that inherited failure.

## OHH-193 blocks OhhO-VR #6

Local branch `ohh-193-pr6-safety` and PR #6 head `ohh-59-gameci-quest-teleop` are the same commit: `458d2a8f5b068a1fc5b5beba1c9eb036dab015b6`. There is no GitHub pull request for OHH-193 yet. The OHH-193 worktree had no extra commits on 14 October 2026.

#6's second commit (`458d2a8`, "deadman on by default, hard 0.2 m/s turbo cap") already does some of what OHH-193 asked for:

- `RequireDeadman` defaults to true. Turning it off goes through `DisableDeadmanForSimulation`, and the setter ignores `false` outside `UNITY_EDITOR`.
- Turbo linear speed is `min(profile.MaxLinVel, 0.2)`. `HardTurboLinCapMps` is `0.2f`.
- Both grips latch e-stop until left-controller X. The README table matches that.

OHH-193, as specified for the follow-up branch, still has to land before #6 merges:

- Deadman opt-out only for a `kind=sim` session, with that state on the HUD. Today's opt-out is "any editor build", which is wider than a sim session.
- Hard linear and angular caps per robot class, applied after calibration and after turbo. `CalibrationManager.ApplyTo` still assigns `maxLinVelOverride` / `maxAngVelOverride` whenever they are `> 0`, so a stored override can raise the non-turbo cap. The 0.2 m/s constant currently limits turbo only.
- Control remap: deadman on left grip, turbo on right trigger (or turbo disabled on a real robot), e-stop on A+B or a HUD button, latched, cleared only by an explicit reset plus a deadman release/re-press. #6 still uses either grip as deadman, right grip as turbo, and both grips as e-stop.
- Tests for those four behaviours, plus the onboarding hint in `UI/OnboardingController.cs`.

Merge #6 only when those commits are on `ohh-59-gameci-quest-teleop` or on a PR stacked on it that Varun merges in the same step. Merging #6 at `458d2a8` ships the editor-wide deadman opt-out, the calibration override, and the both-grips e-stop mapping.

## OmniBot #40 quoting fix

The bash quoting bug was nested double quotes: `docker run ... bash -c " ... python3 -c "import ..." ..."`. Commit `76437fce5f` ("OHH-33: fix smoke-step quoting") on the PR head replaces that with `shell: bash` and `docker run --rm -i "$IMAGE_NAME" bash -s <<'SMOKE'`. The docker-sim workflow run [38547867848](https://github.com/ohho-robotics/OmniBot/actions/runs/38547867848) completed the "Build, smoke test and publish sim container" job successfully. No further quoting patch is required before the ci.yml rebase.

The same commit publishes to GHCR on pushes to `main` (`github.ref == 'refs/heads/main'`). The PR body still says publishing is manual for Varun. He should confirm that workflow change before merge. This docs task did not push an image.

## Linear ID mismatches and proposed body text

Do not edit these bodies from an agent session. Varun (or whoever he asks) pastes the replacement.

### OmniBot #41 — body says OHH-96, the PR is OHH-53

Title: "OHH-53: Serve policy endpoint drives arm through mux policy mode".

Current body:

```markdown
## Summary
Wires `policy_node` to OhhO Serve so the policy endpoint drives the arm through mux `policy` mode, with a mock CI path and mux safety tests.

## Model
composer-2.5 (Cursor)

## Manual steps
- Review mux mode-switch + OHH-96 stop interaction
- Confirm CI mock path is sufficient until real Serve is up

Linear: OHH-53
```

The manual-step bullet names OHH-96. There is no open OmniBot PR titled OHH-96. The stop change that shares `policy_node.py` with this PR is OHH-97, OmniBot #34. Replace the body with:

```markdown
## Summary
Wires `policy_node` to OhhO Serve so the policy endpoint drives the arm through mux `policy` mode, with a mock CI path and mux safety tests.

## Linear
https://linear.app/ohho-robotics/issue/OHH-53

## Model
composer-2.5 (Cursor)

## Manual steps
- Review the mux mode switch together with the late-action stop in OHH-97 (OmniBot PR 34). This pull request is OHH-53.
- Confirm the CI mock path is sufficient until real Serve is up.
```

### OmniBot #43 — right issue, wrong Linear host

Current line:

```text
https://linear.app/ohho/issue/OHH-54
```

Replace with:

```text
https://linear.app/ohho-robotics/issue/OHH-54
```

The issue id OHH-54 matches the title. Only the workspace slug is wrong, and that link does not open the OhhO Robotics issue.

### Other open PRs scanned the same day

A read-only pass over open PRs in OhhO-VR, OmniBot, ohho-sdk, and ohho-os found these further body fixes. They are outside the numbered merge order. Cross-references to a parent epic (OHH-168 on the sdk/os split PRs) match the titles and do not need a rewrite.

| PR | What to change |
| --- | --- |
| [OmniBot #45](https://github.com/ohho-robotics/OmniBot/pull/45) (OHH-72) | The body mentions OHH-33 only as the GHCR image this demo script falls back to, and it never states its own issue. Add a line: `Linear: https://linear.app/ohho-robotics/issue/OHH-72`. Leave the OHH-33 sentence in place. |
| [ohho-sdk #5](https://github.com/ohho-robotics/ohho-sdk/pull/5) | Replace `https://linear.app/ohho/issue/OHH-45` with `https://linear.app/ohho-robotics/issue/OHH-45`. |
| [ohho-os #31](https://github.com/ohho-robotics/ohho-os/pull/31) | Replace `https://linear.app/ohho/issue/OHH-125` with `https://linear.app/ohho-robotics/issue/OHH-125`. |
| [ohho-os #32](https://github.com/ohho-robotics/ohho-os/pull/32) | Replace `https://linear.app/ohho/issue/OHH-128` with `https://linear.app/ohho-robotics/issue/OHH-128`. |
| [ohho-os #33](https://github.com/ohho-robotics/ohho-os/pull/33) | Replace `https://linear.app/ohho/issue/OHH-126` with `https://linear.app/ohho-robotics/issue/OHH-126`. |

OhhO-VR #6's body already says `https://linear.app/ohho-robotics/issue/OHH-59`, matching its title.

## How to rebase after each OmniBot merge

`ci.yml` is the file that conflicts most often, and it conflicts because #43 rewrites the CPU job that later PRs still patch. Practical loop for Varun:

1. Merge the next green-to-merge PR in the table (after review).
2. For each still-open OmniBot PR in steps 6–14, rebase onto the new `main` before merging it.
3. On a `ci.yml` conflict, keep the coverage runner and port pip packages and test paths as in the table above.
4. On a `.gitleaksignore` add/add, keep #48's file.
5. On `registry.py`, combine #41 and #37 as described above.
6. Run the CPU job's tests locally for the ported path (`python scripts/run_cpu_coverage.py`, or the single new file under pytest) before pushing the rebase.
7. Force-with-lease the PR branch. Only Varun does that push. Then merge.

#33, #38, and #34 did not conflict in this order. They still should be rebased if `main` moves under them for any other reason, because GitHub's current "mergeable" flag will go stale. They do not need a `ci.yml` hand merge unless a later commit on `main` touches that file again.

## Left for Varun

- Merge every PR in this plan. Agents do not merge.
- Rotate the Supabase anon key and finish #48's gitleaks allowlist step. Do not substitute the #36/#41 allowlist commits.
- Paste the Linear body corrections. They were not applied from this branch.
- Rebase and force-with-lease the OmniBot PR branches after each merge.
- Headset check for OhhO-VR #6 / OHH-193: deadman release publishes zero on that tick, e-stop stays latched until the explicit reset, hard caps hold after a calibration override and after turbo. Quest recording of that session.
- ohho-os #34: on-bench Go2 DDS check of `ohho sim-serve --backend robot`. This task used no robot.
- OmniBot #36: record on the robot, run `measure_sync_jitter`, fill the dataset card, run `validate_lerobot_load.py --require-lerobot` on a real capture.
- OmniBot #37: GPU training run, public W&B project, Hugging Face upload. Credentials stay with Varun.
- OmniBot #41: real OhhO Serve endpoint. CI covers the mock path only.
- OmniBot #38: generate the 30 s sample MCAP and attach it to a release. The PR names `scripts/record_sample_run.py --duration 30`.
- OmniBot #40: decide whether pushes to `main` should publish `ghcr.io/<owner>/omnibot-sim`. Confirm `docker compose` on a machine he trusts. The quoting fix is already on the PR and the docker-sim job passed.
- OmniBot #53 / OhhO-VR #8: rebase after #43 so teleop uses `/arm/joint_commands/teleop`. Not part of the numbered order.
- OHH-193 code is another job's branch (`ohh-193-pr6-safety`). It had not moved past PR #6's head when this plan was written.
