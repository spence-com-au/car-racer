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

- Find the latest release and tag (`list_releases` / `list_tags`). Versions are `vMAJOR.MINOR.PATCH`.
- Default to the next minor version (`v0.12.0` → `v0.13.0`) for new features or a milestone, and
  to the next patch version for fixes only. Use the version the user names if they give one.
- Never reuse a version that has a **published** release, even one with no assets. A **draft**
  left by a failed run can be released again under the same version (step 4).
- Confirm the version with the user before starting, unless they already named it.

## 2. Check `master`

- The latest `master` commit's `Lint`, `Test` and `Build` runs should be green (`list_workflow_runs`
  with branch `master`). If they're red or still running, tell the user and ask before going on.
- Mention any open PR that looks meant for this release (for example, the rest of a milestone
  stack) so the user can wait for it.

## 3. Start the run

Run the workflow with the GitHub MCP tool:

```
actions_run_trigger
  method: run_workflow
  owner: spence-com-au
  repo: car-racer
  workflow_id: release.yaml
  ref: master
  inputs: { "version": "v0.13.0" }
```

Without the GitHub tools, a person can use **Actions → Release → Run workflow** in the browser
or push the tag (`git tag v0.13.0 && git push origin v0.13.0`). Don't create the release on the
Releases page: the published release would be immutable before the workflow could attach
anything.

Then find the run (`list_workflow_runs` for `release.yaml`, event `workflow_dispatch`, newest
first) and give the user its link.

## 4. Follow it through

A healthy run takes about 30–40 minutes: `check` → `android` (~20 min) → `linux-server` →
`publish`. Don't poll in a tight loop. Use `send_later` (or `ScheduleWakeup`) to check back about
every 15 minutes.

- **Success:** the release page `https://github.com/spence-com-au/car-racer/releases/tag/<version>`
  lists `CarRacer.apk` and `CarRacer-server-<version>-linux-x86_64.tar.gz`, and it is marked
  Latest. Tell the user it's out.
- **Failure:** read the failed job's log (`get_job_logs` with `failed_only`) and explain the cause.
  - `check` says the release is already published: that version is used up, so pick the next one.
  - Version format or branch errors: start the run again with `vX.Y.Z` on `master`.
  - A build or upload failure leaves the tag and a draft release. Once it's fixed (re-run the
    failed jobs for a transient error, or merge a fix PR first), run the workflow again with the
    **same** version: the existing tag is kept and the draft is filled in and published.
    A fix merged after the tag isn't in that tag. To ship it, delete the draft and the tag first
    (ask the user), or release the next version.
