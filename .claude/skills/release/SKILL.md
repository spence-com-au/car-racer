---
name: release
description: Release a new version of Car Racer (signed APK, Linux server tarball and Docker image on GitHub Releases). Use when asked to release, cut, ship or publish a version, or to retry a failed release.
---

# Releasing a version

Releases come from the `Release` workflow (`.github/workflows/release.yaml`; background in
`docs/ci-setup.md` §4). You start it by hand on `master` with a version. It tags `master`, builds
the release-signed APK and the Linux server, uploads them to a **draft** release, pushes the
Docker image and then publishes the release. The repository's releases are **immutable**: once
published, a release can't take assets and its version can't be reused.

## 1. Pick the version

- Find the latest release and tag: `gh release list --limit 5` shows each release as draft or
  published, and `git ls-remote --tags origin 'v*'` lists the tags. Versions are
  `vMAJOR.MINOR.PATCH`.
- Default to the next minor version (`v0.12.0` → `v0.13.0`) for new features or a milestone, and
  to the next patch version for fixes only. Use the version the user names if they give one.
- Never reuse a version that has a **published** release, even one with no assets. A **draft**
  left by a failed run can be released again under the same version (step 4).
- Confirm the version with the user before starting, unless they already named it.

## 2. Check `master`

- The latest `master` commit's `Lint`, `Test` and `Build` runs should be green
  (`gh run list --branch master`). If they're red or still running, tell the user and ask before
  going on.
- Mention any open PR that looks meant for this release (for example, the rest of a milestone
  stack) so the user can wait for it.

## 3. Start the run

Run the workflow with the `gh` CLI:

```sh
started=$(date -u +%FT%TZ)
gh workflow run release.yaml --ref master -f version=v0.13.0
```

Without `gh`, a person can use **Actions → Release → Run workflow** in the browser or push the
tag (`git tag v0.13.0 && git push origin v0.13.0`). Don't create the release on the Releases page:
the published release would be immutable before the workflow could attach anything.

`gh workflow run` doesn't return the run's ID, and the new run can take a few seconds to show up,
so the newest run straight afterwards may be the previous release. Wait until the newest
`workflow_dispatch` run was created after `$started`, then give the user its link:

```sh
gh run list --workflow release.yaml --event workflow_dispatch --limit 1 \
  --json databaseId,url,createdAt --jq "map(select(.createdAt >= \"$started\"))"
```

An empty list (`[]`) means it isn't there yet: run it again in a few seconds.

## 4. Follow it through

A healthy run takes about 30–40 minutes: `check` → `android` (~20 min) → `linux-server` →
`publish`. Don't poll in a tight loop: run
`gh run watch <run-id> --exit-status --interval 60 --compact` in the background, which returns
when the run finishes, non-zero if it failed.

- **Success:** the release page `https://github.com/spence-com-au/car-racer/releases/tag/<version>`
  lists `CarRacer.apk` and `CarRacer-server-<version>-linux-x86_64.tar.gz`, and it is marked
  Latest. Tell the user it's out.
- **Failure:** read the failed steps' logs (`gh run view <run-id> --log-failed`) and explain the
  cause.
  - `check` says the release is already published: that version is used up, so pick the next one.
  - Version format or branch errors: start the run again with `vX.Y.Z` on `master`.
  - A build or upload failure leaves the tag and a draft release. Once it's fixed (re-run the
    failed jobs for a transient error, or merge a fix PR first), run the workflow again with the
    **same** version: the existing tag is kept and the draft is filled in and published.
    A fix merged after the tag isn't in that tag. To ship it, delete the draft and the tag first
    (ask the user), or release the next version.
