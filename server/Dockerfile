# The Car Racer host on a Linux server (docs/server.md). Built by release.yaml from the Linux player
# in build/StandaloneLinux64, with this directory's files copied in beside it.
#
#   docker run -d --network host --restart unless-stopped \
#     -v car-racer:/home/carracer/.config ghcr.io/spence-com-au/car-racer -name "Lounge"
#
# Host networking is needed: phones find the server by UDP broadcast and multicast on the LAN,
# which don't cross Docker's bridge network.
FROM ubuntu:22.04

# ~/.config is where the settings volume mounts: made here with the right owner, so Docker gives a
# new volume that owner too (otherwise it would be root's, and the server couldn't save).
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates libglib2.0-0 ffmpeg \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --create-home carracer \
    && mkdir -p /home/carracer/.config \
    && chown carracer:carracer /home/carracer/.config

# The program stays root's, so a flaw in the network-facing server can't rewrite it: only ~/.config
# needs to be writable.
COPY . /opt/car-racer/
RUN chmod +x /opt/car-racer/CarRacer /opt/car-racer/run-server.sh

USER carracer
WORKDIR /opt/car-racer

# Discovery announcements go out from 47777; guests connect to the game port; a TV fetches the
# viewer stream from 47780 (docs/server.md). ffmpeg above makes that stream.
EXPOSE 47777/udp 47778/udp 47780/tcp

# Settings and the saved cup live in PlayerPrefs under ~/.config, so mount a volume there to keep them.
ENTRYPOINT ["/opt/car-racer/run-server.sh"]
