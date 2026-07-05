#!/bin/bash

fail_msg() {
    echo "$1"
    exit 1
}

#
# This script should be run in, or with the working directory set to, the root project directory
#

REPO_ROOT="$(readlink -f "$(dirname "${BASH_SOURCE[0]}")"/..)"
pushd "$REPO_ROOT" > /dev/null

# grab default variables
source ./packaging/vars

[ -n "$NAME" ] || fail_msg "ERROR: Name isn't specified!"
[ -n "$ID" ]   || fail_msg "ERROR: ID isn't specified!"
[ -n "$RIDS" ] || fail_msg "ERROR: Runtime IDs not specified!"

PUBLISH_DIR="$(readlink -f ${PUBLISH_DIR:="./build/publish"})" 

[ -n "$PUBLISH_DIR" ] || fail_msg "ERROR: Publish directory isn't set!"
[ -d "$PUBLISH_DIR" ] || fail_msg "ERROR: Publish location isn't a directory!"

OUTPUT_DIR="$(readlink -f ${OUTPUT_DIR:="./build/packages"})" 

[ -n "$OUTPUT_DIR" ] || fail_msg "ERROR: Output directory isn't set!"
[ -d "$OUTPUT_DIR" ] || fail_msg "ERROR: Output location isn't a directory!"


# check for makensis
[ -x "$(which makensis)" ] || fail_msg "ERROR: makensis is missing or unusable!"


ARCHS="x86 x64 arm64"

for arch in $ARCHS; do
    echo
    echo "Creating installer for Windows $arch..."
    echo

    if [ ! -d "$PUBLISH_DIR/win-$arch" ]; then
        echo "  $arch build not found! Skipping..."
        continue
    fi

    if [ ! -f "$PUBLISH_DIR/win-$arch/$NAME.exe" ]; then
      echo "  $NAME binary for $arch not found! Skipping..."
      continue
    fi

    if [ -n "$VERSION" ]; then
        makensis -DARCH=$arch -DVERSION="$VERSION" -DPUBLISH_DIR="$PUBLISH_DIR" -DOUTPUT_DIR="$OUTPUT_DIR" "packaging/windows/knossos.nsi"
    else
        makensis -DARCH=$arch -DPUBLISH_DIR="$PUBLISH_DIR" -DOUTPUT_DIR="$OUTPUT_DIR" "packaging/windows/knossos.nsi"
    fi
done

echo
echo "Done."
echo
