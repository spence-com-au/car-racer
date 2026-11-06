# Running the host on a Linux server

A phone normally hosts a game, but an x86-64 Linux machine on the same network (a home server,
a NAS, a spare laptop) can host instead, so no phone pays the cost of
hosting. The server has no car of its own; the **first player to join runs the room** from their
phone, with the same settings and Start button a phone host has (design doc §2.3). When they
leave, whoever has been there longest takes over.

Each release (`v*` tag) publishes the server two ways:

- **Docker image:** `ghcr.io/spence-com-au/car-racer:<tag>` (and `:latest`)
- **Tarball:** `CarRacer-server-<tag>-linux-x86_64.tar.gz` on the Releases page

Use the same version as the phones: a different protocol version shows as "Update required".

## Docker

```sh
docker run -d --name car-racer --network host --restart unless-stopped \
  -v car-racer:/home/carracer/.config \
  ghcr.io/spence-com-au/car-racer:latest -name "Lounge"
```

- **`--network host` is required.** Phones find the server by UDP broadcast and multicast,
  which don't cross Docker's default bridge network. (Docker Desktop on macOS and Windows
  doesn't support host networking; use a Linux host.)
- The volume keeps the room's last settings and a saved cup between restarts.
- `docker logs -f car-racer` shows who joins, leaves and runs the room, and each event.

## Tarball

```sh
tar -xzf CarRacer-server-v0.11.0-linux-x86_64.tar.gz
./car-racer-server/run-server.sh -name "Lounge"
```

It needs an x86-64 Linux with glibc (any recent Ubuntu, Debian or Fedora). To keep it running,
a systemd unit along these lines works:

```ini
[Unit]
Description=Car Racer host
After=network-online.target

[Service]
User=carracer
ExecStart=/opt/car-racer-server/run-server.sh -name "Lounge"
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

## Options

| Option | Default | What it does |
|---|---|---|
| `-name <name>` | `Car Racer server` | What phones list the game as (up to 24 characters) |
| `-port <port>` | `47778` | The game port guests connect to (UDP) |
| `-cast [TV]` | off | Show the room on a Chromecast by itself (see below): the TV's name, id or address, or none for the first one found |
| `-ffmpeg <path>` | `ffmpeg` | The ffmpeg the TV stream is made with |
| `-viewer-port <port>` | `47780` | The TCP port the TV fetches the stream from |

Open UDP **47777** (discovery) and the game port in any firewall. If phones can't see the server
(some networks block broadcast), they can still **join by IP** with the server's address.

The server exits with a non-zero status if it can't host (for example, the port is taken), so a
service manager or Docker restarts it.

## Showing the race on a TV

The server can cast the room to a **Chromecast** (or a TV with Chromecast built in) on the same
network: a broadcast view with a map of the track and every car on it, the standings, the
countdown and the results, and in the lobby a QR code to join (design doc §2.4). It plays in the
TV's own media player, a few seconds behind the race.

- The **room leader's phone** has a **Cast to TV** button in the lobby, listing the Chromecasts
  the server found, with Stop casting. A leader's pick takes over the TV even if something else
  is on it. Everyone else's lobby says "On TV: …".
- Or start the server with **`-cast "Living Room TV"`** (the name the TV has in the Google Home app), or
  `-cast` alone for the first Chromecast found. The server casts once the stream is ready, and
  again at each event's countdown if nothing is being cast, but only while the TV is idle: it
  never takes over something someone else is watching.
- It needs **ffmpeg**. The Docker image has it; with the tarball, install it (`sudo apt install
  ffmpeg`) or point `-ffmpeg` at one. Without it the server logs "TV viewer off" and carries on.
- The TV fetches the stream over HTTP from TCP **47780** (`-viewer-port`), so allow that in any
  firewall. Chromecasts are found by mDNS (UDP 5353 to `224.0.0.251`), which needs host
  networking in Docker, as discovery already does. The log says which TVs it found and how each
  cast goes.
- Anything that plays HLS can watch too: `vlc http://<server>:47780/live.m3u8`.

## How it's built

The server is the ordinary Linux player (`BuildScript.BuildLinuxServerFromCommandLine`) run with
`-batchmode -nographics -server` by `server/run-server.sh`, rather than Unity's Dedicated Server
subtarget, which needs an editor module the CI images may lack. `build.yaml` builds it on every PR
and starts it for a few seconds to check it hosts; `release.yaml` packages it and pushes the image
built from `server/Dockerfile`.
