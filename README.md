# Car Racer

An arcade, bump-heavy 3D kart racer for Android. 2 to 6 friends in the same room race on their
own phones over local Wi-Fi: one phone hosts, the others find it automatically, and bots fill
the empty slots. Built with Unity 6.3 LTS and Netcode for GameObjects.

- **Design:** [`docs/design.md`](docs/design.md) records every decision so far and the milestone plan.
- **CI and releases:** [`docs/ci-setup.md`](docs/ci-setup.md) covers secrets, signing and cutting a release.
- **Linux host:** [`docs/server.md`](docs/server.md) covers hosting on a Linux server (Docker or a tarball) instead of a phone.

## Status

**M1: core loop.** LAN multiplayer races on the sample oval for 2–6 phones:
- The main menu asks for your name on first launch, then offers **Host game**, **Join game**
  (games found on the Wi-Fi, or join by IP) and **Practice** (a free drive).
- In a session everyone drives around in the lobby and taps **Ready**. The host then starts a
  race with a synced countdown, laps, positions, car-to-car bumping and results, with a rematch.
- Arcade drift-and-boost handling with touch, gamepad and keyboard controls, a chase camera, a
  HUD with drift meter, mini-map and "Wrong way!" warning, and haptics.
- The engine-independent core (protocol, race rules, physics, tracks, scoring) is unit tested
  with `dotnet test`. Graphics are placeholders.

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
