#!/bin/sh
# Builds the prebuilt tool taskman_bhl runs instead of building it (dist/, framework-dependent: one for every OS).
# The release workflow (.github/workflows/release.yml) puts it into the package zip, it's not in git.
set -e
cd "$(dirname "$0")"
rm -rf dist
dotnet publish bindgen.csproj -c Release --nologo -v q -p:UseAppHost=false -o dist
rm -f dist/*.pdb
