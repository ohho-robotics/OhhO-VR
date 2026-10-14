# OhhO VR release pipeline

Tag `vMAJOR.MINOR.PATCH` or `vMAJOR.MINOR.PATCH-rcN` (example: `v1.0.0-rc1`) runs [`.github/workflows/release.yml`](../.github/workflows/release.yml). The job builds a Quest APK (Android ARM64, IL2CPP), signs it with the release keystore, and attaches the APK plus a SHA-256 file to a GitHub Release. Release-candidate tags are marked prerelease.

The workflow file is on the tagged commit. A tag of an older commit that does not contain `release.yml` does not run this job.

This job does not create a keystore, does not install a headset, and does not submit to the Meta Quest store.

## Secret checklist

Add repository secrets at `https://github.com/ohho-robotics/OhhO-VR/settings/secrets/actions`. Names must match exactly. Values are never committed.

| Secret | Purpose |
|---|---|
| `OHHO_VR_RELEASE_KEYSTORE_BASE64` | Base64 of the release keystore file, one line |
| `OHHO_VR_RELEASE_KEYSTORE_PASSWORD` | Keystore store password |
| `OHHO_VR_RELEASE_KEY_ALIAS` | Key alias inside that keystore |
| `OHHO_VR_RELEASE_KEY_PASSWORD` | Key password for that alias |
| `UNITY_LICENSE` | Unity personal licence `.ulf` contents (same secret as edit-mode tests) |
| `UNITY_EMAIL` | Unity ID that owns the licence |
| `UNITY_PASSWORD` | Password for that Unity ID |

`unity-builder` mapping:

| Secret | `game-ci/unity-builder` input |
|---|---|
| `OHHO_VR_RELEASE_KEYSTORE_BASE64` | `androidKeystoreBase64` |
| `OHHO_VR_RELEASE_KEYSTORE_PASSWORD` | `androidKeystorePass` |
| `OHHO_VR_RELEASE_KEY_ALIAS` | `androidKeyaliasName` |
| `OHHO_VR_RELEASE_KEY_PASSWORD` | `androidKeyaliasPass` |

The decoded file on the runner is `ohho-vr-release.keystore`. The job deletes it after the build. `*.keystore` is gitignored.

## Create the keystore offline

Run this on a machine that is not committing the file. Do not use alias `androiddebugkey`. Do not use the password `android`. Those are the public Android debug credentials. The workflow rejects them.

Back up the keystore and both passwords outside the repo. A lost key cannot update `com.ohhorobotics.ohhovr` on a device or in a store.

Windows (PowerShell), JDK `keytool` on `PATH`:

```powershell
keytool -genkeypair -v `
  -keystore ohho-vr-release.keystore `
  -alias ohho-vr `
  -keyalg RSA `
  -keysize 2048 `
  -validity 10000
```

`keytool` prompts for the store password, key password, and certificate name. Then copy a single-line base64 value into `OHHO_VR_RELEASE_KEYSTORE_BASE64`:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("$pwd\ohho-vr-release.keystore")) | Set-Clipboard
```

Clear the clipboard after the secret is saved. Keep `ohho-vr-release.keystore` on encrypted offline storage. Do not add it to the git tree.

Linux or macOS:

```bash
keytool -genkeypair -v \
  -keystore ohho-vr-release.keystore \
  -alias ohho-vr \
  -keyalg RSA \
  -keysize 2048 \
  -validity 10000
base64 -w 0 ohho-vr-release.keystore
```

GitHub secret values are limited to 48 KB. A normal RSA keystore base64 string fits. The file may be JKS (`FEEDFEED`) or PKCS12. The alias in `OHHO_VR_RELEASE_KEY_ALIAS` must be the alias you just created (`ohho-vr` in the example). Store password and key password may differ; put each in its own secret.

## Debug keystore stays separate

CI debug sideload builds, when that job is present, generate `ci/debug.keystore` with alias `androiddebugkey` and the public password `android`. That key is not a release key. This workflow never generates it and never passes it to `unity-builder`.

## What the tag build produces

- Editor image: `unityci/editor:ubuntu-6000.5.2f1-android-3` (Unity **6000.5.2f1**)
- Scene: `Assets/scene1.unity` (the enabled build scene)
- Scripting: IL2CPP. Architecture: ARM64 only
- Package id: `com.ohhorobotics.ohhovr`
- Player name: `OhhO-VR` (`ohho-robotics`)
- Export: APK (`androidPackage`), not an Android App Bundle
- Asset: `OhhO-VR-<tag>.apk` and `OhhO-VR-<tag>.apk.sha256`
- Notes: rendered from [`release-notes-template.md`](release-notes-template.md)

On the runner only, the job writes IL2CPP, ARM64, the package id, and the player name into `ProjectSettings/ProjectSettings.asset` before `unity-builder`. Those edits are not committed. `allowDirtyBuild` is set because that patch dirties the checkout. `versioning: Custom` still takes the version from the tag, not from git describe.

`game-ci/unity-builder`'s default build method applies `-buildVersion` and `-androidVersionCode` and turns on the custom keystore. A custom build method that ignores those flags is not used.

## bundleVersion and versionCode

`bundleVersion` (Android `versionName`) is the tag without the leading `v`. `v1.0.0-rc1` becomes `1.0.0-rc1`.

`versionCode` is monotonic across release candidates and the final tag of the same triple. GameCI's default formula ignores the `-rc` suffix, so `v1.0.0-rc1` and `v1.0.0` would collide. This job passes an explicit code:

```text
versionCode = major * 10000000 + minor * 100000 + patch * 1000 + channel
channel     = rc number (1..998), or 999 when the tag has no -rc suffix
```

| Tag | bundleVersion | versionCode |
|---|---|---|
| `v1.0.0-rc1` | `1.0.0-rc1` | `10000001` |
| `v1.0.0-rc2` | `1.0.0-rc2` | `10000002` |
| `v1.0.0` | `1.0.0` | `10000999` |
| `v1.0.1-rc1` | `1.0.1-rc1` | `10001001` |
| `v1.0.1` | `1.0.1` | `10001999` |

Accepted tags: `vMAJOR.MINOR.PATCH` and `vMAJOR.MINOR.PATCH-rcN` with no leading zeros. `v1.0.0-rc.1`, `v1.0.0-beta1`, and `v1.0` do not build. Limits: major 0–200, minor 0–99, patch 0–99, rc 1–998. That stays under Android's 2100000000 cap.

Re-running the same tag keeps the same `versionCode`. Ship a fix by pushing a new tag (`v1.0.0-rc2`), not by rebuilding `v1.0.0-rc1`.

## Cut `v1.0.0-rc1`

After the secrets above are set and `release.yml` is on the commit you want to ship:

```bash
git tag v1.0.0-rc1
git push origin v1.0.0-rc1
```

The Actions run is red until every secret in the table is set. A green run publishes the GitHub Release. `-rc` tags are prereleases. Confirm the checksum before sideload:

```bash
sha256sum -c OhhO-VR-v1.0.0-rc1.apk.sha256
```

Windows:

```powershell
Get-FileHash -Algorithm SHA256 .\OhhO-VR-v1.0.0-rc1.apk
```

Compare that hash to the release body and to `OhhO-VR-v1.0.0-rc1.apk.sha256`. The body also lists the signing certificate SHA-256 from `keytool`.

Headset install is a separate human step (`adb install -r OhhO-VR-v1.0.0-rc1.apk` with a Quest in developer mode). This workflow does not talk to a headset.

## Human TODO

- [ ] Create `ohho-vr-release.keystore` offline and store it outside git
- [ ] Add `OHHO_VR_RELEASE_KEYSTORE_BASE64`
- [ ] Add `OHHO_VR_RELEASE_KEYSTORE_PASSWORD`
- [ ] Add `OHHO_VR_RELEASE_KEY_ALIAS`
- [ ] Add `OHHO_VR_RELEASE_KEY_PASSWORD`
- [ ] Confirm `UNITY_LICENSE`, `UNITY_EMAIL`, and `UNITY_PASSWORD` are already set
- [ ] Tag a commit that contains `.github/workflows/release.yml` (`v1.0.0-rc1`) and push the tag
- [ ] Check the Actions log, the GitHub Release APK, and the SHA-256
- [ ] Sideload the APK onto a Quest once
