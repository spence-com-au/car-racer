# Car Racer

An arcade, bump-heavy 3D kart racer for Android. 2 to 6 friends in the same room race on their
own phones over local Wi-Fi: one phone hosts, the others find it automatically, and bots fill
the empty slots. Built with Unity 6.3 LTS and Netcode for GameObjects.

- **Design:** [`docs/design.md`](docs/design.md) records every decision so far and the milestone plan.
- **CI and releases:** [`docs/ci-setup.md`](docs/ci-setup.md) covers secrets, signing and cutting a release.
- **Linux host:** [`docs/server.md`](docs/server.md) covers hosting on a Linux server (Docker or a tarball) instead of a phone.

## Status

**M19: TV viewer** is the latest milestone; [the milestone plan](docs/design.md#16-milestones)
lists each one. LAN multiplayer for 2–6 phones, with bots filling empty slots:
- **Host game**, **Join game** (games found on the Wi-Fi, by IP or by QR code) and **Practice**
  (an offline free drive). Everyone drives around the lobby, picks a car and votes for the track.
- **Events:** races on 8 tracks, Sumo and Battle arenas, Monster Jam, and Grand Prix and Mixed
  cups with points, standings and resume.
- Arcade drift-and-boost handling, car-to-car bumping, nine items, damage, and twelve Kenney
  Car Kit bodies with their own handling. The host turns damage, items and car stats on or off.
- A dedicated host on a Linux server (Docker or a tarball) can cast the race to a Chromecast.
- The engine-independent core (protocol, rules, physics, tracks, bots, scoring) is unit tested
  with `dotnet test`.

## Layout

```
Unity/                        Unity project
  Assets/CarRacer/Core/       engine-independent game logic (no UnityEngine references)
  Assets/CarRacer/Game/       Unity runtime code
  Assets/CarRacer/Editor/     project settings, build script, Android manifest tweaks
  Assets/CarRacer/Tests/      Unity tests
dotnet/                       builds and tests Core with plain .NET
tools/                        helper scripts
docs/                         design and setup docs
```

## Quick start

```sh
# Core tests (needs the .NET 8 SDK; no Unity required)
dotnet test dotnet/CarRacer.sln
```

To work in Unity, open `Unity/` with Unity **6000.3.26f1** (with Android Build Support) and follow
[first-time setup](docs/ci-setup.md#5-first-time-unity-editor-setup-local).

### Trying multiplayer on phones

Install the development APK (from a PR's **Unity** workflow artifacts, or built locally) on two
or more phones on the same Wi-Fi. Tap **Host game** on one; on the others tap **Join game**, and
the host should be listed within a couple of seconds. If it isn't (some networks block
discovery), type the address shown in the host's lobby.

Extract the APK from the artifact zip before tapping it; installing from inside the zip fails.
Development builds are signed with a shared debug key (`Unity/Keystores/debug.keystore`), so
newer ones install over older ones. Builds made before that key existed must be uninstalled once.
