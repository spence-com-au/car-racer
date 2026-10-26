# CLAUDE.md

A LAN-multiplayer arcade kart racer for Android, built with Unity 6.3 LTS and Netcode for
GameObjects. Read [`docs/design.md`](docs/design.md) before changing behaviour: it records the
decisions already made (host-as-referee, owner-authoritative cars, sphere physics, items, bots…)
and the milestone plan. Update it in the same PR when a decision changes.

## Pull requests

- **`master` is the default branch.** Branch from `origin/master` and open a PR; don't push to
  `master`.
- **Open PRs ready for review, not as drafts.** Claude reviews every non-draft PR into `master` on
  each push, from the org `Workflows` ruleset's `claude-review.yaml`. A draft gets no review, and
  the ruleset reviews one marked ready only on its next push.
- **Keep PRs small enough to review in full** — roughly under 1,500 changed lines and one
  concern each. The automated reviewer reads only part of a larger PR and won't approve it.
  Split big work into a stack of PRs, each based on the one before; when a lower one is
  squash-merged, rebase the rest onto `master` so each diff shows only its own change.
- **A PR is not done when it is opened.** Watch it: fix failing checks, and address each review
  finding or reply with why not, then resolve the thread.
- **Enable squash auto-merge on every PR** right after opening it (`gh pr merge --auto --squash`).
  The `master` ruleset requires green CI, an approval (Claude's counts) and resolved
  conversations, so auto-merge only fires once all of those are met — don't merge by hand.

## Code layout

- **`Unity/Assets/CarRacer/Core`** — engine-independent logic (`noEngineReferences`). Anything
  that can be plain C# goes here: protocol, rules, scoring, track geometry, physics formulas, bot
  decisions. It must have `dotnet test` coverage in `dotnet/CarRacer.Core.Tests`.
- **`Unity/Assets/CarRacer/Game`** — thin Unity glue (MonoBehaviours, NGO, UI, platform code).
- **`Unity/Assets/CarRacer/Editor`** — player settings (`ProjectConfigurator`), build script, Android
  manifest post-processing. Player settings are applied from code, not hand-edited.
- Core uses its own `Vec3`/`Vec2`; convert with `UnityConversions`.
- C# 9 (Unity's compiler): no newer language features in `Core`, `Game` or `Editor`.

## Before pushing

```sh
dotnet test dotnet/CarRacer.sln          # core unit tests
python3 tools/generate_unity_meta.py     # create .meta files for any new Unity assets
python3 tools/generate_unity_meta.py --check

# linters (the Lint workflow runs the same)
dotnet format dotnet/CarRacer.sln --severity warn                   # C# style and naming, Core + tests
dotnet format whitespace --folder Unity/Assets/CarRacer             # C# layout, Game/Editor/Unity tests
ruff check tools && ruff format tools                               # Python
shellcheck server/run-server.sh
```

- **C# style lives in `.editorconfig`**: Allman braces, braces always, `_camelCase` private
  fields, no unused usings. The commands above fix in place; CI checks with `--verify-no-changes`.

- **Every file under `Unity/Assets` needs a `.meta` file.** Files written outside the editor get
  one from `tools/generate_unity_meta.py` (deterministic GUIDs); CI fails if any are missing.
- **Unity code can't be compiled outside CI here.** The `Build` workflow compiles it, runs the
  EditMode/PlayMode tests and builds a development APK on every PR; treat that check as the
  compile check for `Game`, `Editor` and Unity tests.
- **Bump `DiscoveryConstants.ProtocolVersion`** whenever any network message format changes.

## Builds and devices

- Development APKs are signed with the committed `Unity/Keystores/debug.keystore` so they
  install over each other; release APKs are signed with the keystore in GitHub secrets
  (`release.yaml`, on `v*` tags). Never sign a release with the debug key.
- Minimum Android 8.0 (API 26), ARM64. The Pixel 2 XL is the low-end performance baseline.
