#!/bin/sh
# Packages the Linux player in build/StandaloneLinux64 as the server release (docs/server.md): copies
# this directory's files in beside it and writes the tarball to the path given. release.yaml publishes
# the package and build.yaml checks it on every PR, so both run this rather than a copy of it.
# Run from the repository root.
set -eu
tarball="${1:?Usage: server/package.sh TARBALL}"
cp server/run-server.sh server/Dockerfile build/StandaloneLinux64/
tar -czf "${tarball}" --transform "s,^build/StandaloneLinux64,car-racer-server," build/StandaloneLinux64
