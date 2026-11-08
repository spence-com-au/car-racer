#!/bin/sh
# Starts the Car Racer host with no player of its own (docs/server.md), logging to stdout.
# Extra arguments are passed on: -name "Lounge" (what phones list it as), -port 47778 and the rest
# --help lists.
cd "$(dirname "$0")" || exit 1

# With no display, Unity's Linux player selects no window backend ("Selected window backend: (null)")
# and then segfaults in `strcasecmp(NULL, "x11")`, -nographics or not. SDL's dummy driver is one it
# can select without a display.
export SDL_VIDEODRIVER=dummy
exec ./CarRacer -batchmode -nographics -server -logFile - "$@"
