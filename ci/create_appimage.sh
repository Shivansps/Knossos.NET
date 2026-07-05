#!/bin/bash

fail_msg() {
    echo "$1"
    exit 1
}

#
# This script should be run in, or with the working directory set to, the root project directory
#

pushd "$(readlink -f "$(dirname "${BASH_SOURCE[0]}")"/..)" > /dev/null

# grab default variables
source ./packaging/vars

[ -n "$NAME" ] || fail_msg "ERROR: Name isn't specified!"
[ -n "$ID" ]   || fail_msg "ERROR: ID isn't specified!"
[ -n "$RIDS" ] || fail_msg "ERROR: Runtime IDs not specified!"
[ -n "$GITHUB_ORG" ] || fail_msg "ERROR: GitHub Project not specified!"

PUBLISH_DIR="$(readlink -f ${PUBLISH_DIR:="./build/publish"})" 

[ -n "$PUBLISH_DIR" ] || fail_msg "ERROR: Publish directory isn't set!"
[ -d "$PUBLISH_DIR" ] || fail_msg "ERROR: Publish location isn't a directory!"

OUTPUT_DIR="$(readlink -f ${OUTPUT_DIR:="./build/packages"})" 

[ -n "$OUTPUT_DIR" ] || fail_msg "ERROR: Output directory isn't set!"
[ -d "$OUTPUT_DIR" ] || fail_msg "ERROR: Output location isn't a directory!"

# there should already be a usable binary, so just figure out what we should use
DOTNET_ARCH=

case "$ARCH" in
    "x86_64" | "x64")
        DOTNET_ARCH="x64"
        ;;
    "aarch64" | "arm64")
        DOTNET_ARCH="arm64"
        ;;
    *)
        fail_msg "Unknown arch! Aborting!"
        ;;
esac

[ -n "$DOTNET_ARCH" ] || fail_msg "ERROR: No arch specified!"

# make sure needed archive exists (should have checked for this already!!)
[ -f "$OUTPUT_DIR/Linux_$DOTNET_ARCH.tar.gz" ] || fail_msg "ERROR: Linux $DOTNET_ARCH archive not found!"


# VERSION="$(grep string\ AppVersion -rI $NAME | cut -d \" -f 2)" # this will surely come back to bite me
VERSION="${VERSION:="0.0.0"}"

BUILD_DIR="./build/appimage"
APPDIR="$BUILD_DIR/AppDir"

FILE_ROOT="./packaging/linux"

APPRUN_FILE="AppRun"
METAINFO_FILE="knossos.metainfo.xml.in"
DESKTOP_FILE="knossos.desktop.in"

ICON_SIZES="512 256 128 96 64 48"    # must include 256x256 at a minimum
ICON_FILE="knossos"

APPIMAGE_TOOL_URL="https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$(uname -m).AppImage"

UPDATE_SCHEME="gh-releases-zsync|$GITHUB_ORG|$NAME|latest|$NAME-*$ARCH.AppImage.zsync"


# grab appimagetool
mkdir -p "$BUILD_DIR"
wget -O "$BUILD_DIR/appimagetool" "$APPIMAGE_TOOL_URL" || fail_msg "ERROR: Failed to get appimagetool!"
chmod +x "$BUILD_DIR/appimagetool"


# configure templates
export _NAME="$NAME"
export _ID="$ID"
export _VERSION="$VERSION"

envsubst < "$FILE_ROOT/$METAINFO_FILE" > "$BUILD_DIR/$ID.appdata.xml"
envsubst < "$FILE_ROOT/$DESKTOP_FILE" > "$BUILD_DIR/$ID.desktop"

unset _NAME _ID _VERSION


# create and populate AppDir
if [ -d "$APPDIR" ]
then
    rm -Rf "$APPDIR"
fi

install -d "$APPDIR"
install -m 755 "$FILE_ROOT/$APPRUN_FILE" "$APPDIR"

install -d "$APPDIR/usr/bin"
tar -xzvf "$OUTPUT_DIR/Linux_$DOTNET_ARCH.tar.gz" -C "$APPDIR/usr/bin"
chmod +x "$APPDIR/usr/bin/$NAME"

install -d "$APPDIR/usr/share/applications"
install -m 644 "$BUILD_DIR/$ID.desktop" "$APPDIR/usr/share/applications"

install -d "$APPDIR/usr/share/metainfo"
install -m 644 "$BUILD_DIR/$ID.appdata.xml" "$APPDIR/usr/share/metainfo"

for size in $ICON_SIZES; do
    install -d "$APPDIR/usr/share/icons/hicolor/${size}x${size}/apps"
    install -m 644 "$FILE_ROOT/$ICON_FILE-$size.png" "$APPDIR/usr/share/icons/hicolor/${size}x${size}/apps/$ID.png"
done

# provide symlinks to .desktop and app icon in root as required by AppImage spec
ln -s "usr/share/applications/$ID.desktop" "$APPDIR"
ln -s "usr/share/icons/hicolor/256x256/apps/$ID.png" "$APPDIR"


#
# create AppImage
#

ARCH=$ARCH "$BUILD_DIR/appimagetool" -u "$UPDATE_SCHEME" "$APPDIR" "$OUTPUT_DIR/$NAME-$ARCH.AppImage"

# zsync file is saved to current directory so move it to proper place
[ -f "$NAME-$ARCH.AppImage.zsync" ] && mv "$NAME-$ARCH.AppImage.zsync" "$OUTPUT_DIR"
