# CI setup

Four GitHub Actions workflows live in `.github/workflows/`:

| Workflow | Runs on | Needs secrets? | What it does |
|---|---|---|---|
| `lint.yaml` | every push to `master`, every PR | No | Checks every file under `Unity/Assets` has its `.meta` file; lints C# (`dotnet format` against `.editorconfig`), Python (`ruff`), shell (`shellcheck`) and the workflows themselves (`actionlint`) |
| `test.yaml` | every push to `master`, every PR | No | `dotnet test` for the engine-independent core |
| `build.yaml` | every push to `master`, every PR | Unity licence | Unity EditMode/PlayMode tests; builds a development APK and the Linux server (started briefly to check it hosts) and attaches them to the run |
| `release.yaml` | pushing a `v*` tag, or run by hand with a version | Unity licence + keystore | Builds a release-signed APK and the Linux server, publishes them to GitHub Releases, and pushes the server's Docker image to GHCR ([`server.md`](server.md)) |

Claude reviews every non-draft PR into `master` on each push, from `claude-review.yaml`, which the
org's `Workflows` ruleset runs from [`spence-com-au/.github`](https://github.com/spence-com-au/.github).

Until the Unity secrets exist, `build.yaml` skips its jobs with a notice instead of failing.

Add secrets under **GitHub → repository → Settings → Secrets and variables → Actions → New
repository secret**.

## 1. Claude review (for `claude-review.yaml`)

Run `claude setup-token` locally and save the token it prints as the `CLAUDE_CODE_OAUTH_TOKEN`
secret. Drafts aren't reviewed, and neither is a push Dependabot makes, since its run is passed no
secrets.

## 2. Unity licence (for `build.yaml` and `release.yaml`)

Unity Personal is free, but every machine that runs Unity, including CI, needs an activated licence.

1. Install **Unity Hub**, sign in, and activate a **Personal** licence (Hub → Preferences →
   Licenses → Add → Get a free personal license).
2. Find the licence file it created:
   - Windows: `C:\ProgramData\Unity\Unity_lic.ulf`
   - macOS: `/Library/Application Support/Unity/Unity_lic.ulf`
   - Linux: `~/.local/share/unity3d/Unity/Unity_lic.ulf`
3. Create these secrets:

   | Secret | Value |
   |---|---|
   | `UNITY_LICENSE` | The **entire contents** of `Unity_lic.ulf` |
   | `UNITY_EMAIL` | Your Unity account email |
   | `UNITY_PASSWORD` | Your Unity account password |

If activation fails in CI (for example because of two-factor authentication on the Unity
account), follow GameCI's activation guide: <https://game.ci/docs/github/activation>.

## 3. Android signing keystore (for `release.yaml`)

Every update of an Android app must be signed with **the same key**, or phones refuse to
install it over the previous version. Create the key once and **back it up somewhere safe**
(a password manager is ideal). If it's lost, everyone has to uninstall and reinstall.

```sh
keytool -genkeypair -v \
  -keystore car-racer-release.keystore \
  -alias carracer \
  -keyalg RSA -keysize 4096 -validity 10000
```

Then base64-encode it for the secret:

```sh
# Linux
base64 -w0 car-racer-release.keystore > keystore.b64
# macOS
base64 -i car-racer-release.keystore -o keystore.b64
```

| Secret | Value |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | Contents of `keystore.b64` |
| `ANDROID_KEYSTORE_PASS` | The keystore password you chose |
| `ANDROID_KEYALIAS_NAME` | `carracer` (or whatever you passed to `-alias`) |
| `ANDROID_KEYALIAS_PASS` | The key password (the same as the keystore password unless you set a different one) |

Delete `keystore.b64` afterwards. `*.keystore` is git-ignored; never commit it.

## 4. Cutting a release

Either push a tag:

```sh
git tag v0.1.0
git push origin v0.1.0
```

or, from the browser or phone, open **Actions → Release → Run workflow**, keep the branch on
`master` and enter the version (`v0.1.0`). The run tags `master` itself and then does the same as
a tag push. Claude can start it too (the `release` skill in `.claude/skills`).

Don't create the release on the Releases page. The repository's releases
are immutable, so assets can only be attached while a release is a draft: the workflow uploads
the APK and the server tarball to a draft and publishes it once both are up. A release published
by hand already exists when its tag fires the workflow, so the workflow stops early; that version
can't take assets, so release the next one. A failed run leaves a draft behind, which a re-run
fills in and publishes.

The tag becomes the app version (`0.1.0`), and the Android version code is derived from it.
The APK is published as `CarRacer.apk` on the repository's Releases page, so
<https://github.com/spence-com-au/car-racer/releases/latest/download/CarRacer.apk> always
downloads the newest release. Friends can install it from there, or use
[Obtainium](https://github.com/ImranR98/Obtainium) pointed at the repository to get updates
automatically.

The same release carries the Linux server as `CarRacer-server-v0.1.0-linux-x86_64.tar.gz`, and
the image `ghcr.io/spence-com-au/car-racer:v0.1.0` (and `:latest`) goes to GitHub's container
registry using the workflow's own token. The first time, the package is private: make it public
under **GitHub → the spence-com-au organisation → Packages → car-racer → Package settings** so
servers can pull it without logging in.

## 5. First-time Unity editor setup (local)

The Unity project is in `Unity/`. Settings that matter are applied from code, so the first open
is mostly automatic:

1. Install **Unity 6000.3.26f1** with **Android Build Support** (including OpenJDK and the
   Android SDK & NDK) through Unity Hub.
2. Open the `Unity/` folder as a project. Unity generates `ProjectSettings/*` and
   `Packages/packages-lock.json`. If it adjusts any built-in package version in
   `Packages/manifest.json`, accept the change.
3. Run **Car Racer → Apply Project Settings**, then **Car Racer → Create Bootstrap Scene**.
4. Commit the generated `ProjectSettings/`, `Packages/packages-lock.json` and
   `Assets/Scenes/` files so CI and other machines use the same configuration.
5. **Car Racer → Build → Development APK** writes `build/Android/CarRacer-dev.apk`.

## 6. Running the core tests locally

```sh
dotnet test dotnet/CarRacer.sln
```

Requires the .NET 8 SDK. No Unity needed.

## 7. Adding files outside the Unity editor

Unity tracks every asset by a GUID stored in a `.meta` file next to it. When you add files
without opening the editor, run:

```sh
tools/generate_unity_meta.py
```

It creates missing `.meta` files with GUIDs derived from the path, so every machine agrees. CI
fails if any are missing (`--check`).
