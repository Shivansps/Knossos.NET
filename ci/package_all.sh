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

PUBLISH_DIR="$(readlink -f ${PUBLISH_DIR:="./build/publish"})" 

[ -n "$PUBLISH_DIR" ] || fail_msg "ERROR: Publish directory isn't set!"
[ -d "$PUBLISH_DIR" ] || fail_msg "ERROR: Publish location isn't a directory!"

OUTPUT_DIR="$(readlink -f ${OUTPUT_DIR:="./build/packages"})" 

[ -n "$OUTPUT_DIR" ] || fail_msg "ERROR: Output directory isn't set!"
[ -d "$OUTPUT_DIR" ] || fail_msg "ERROR: Output location isn't a directory!"


# Package up all builds
for rid in $RIDS; do
    # create archive filename based on RID
    fname=${rid/-/_}
    fname=${fname/win/Windows}
    fname=${fname/linux/Linux}
    fname=${fname/osx/MacOS}

    echo
    echo "Archiving $rid ..."
    echo

    pushd "$PUBLISH_DIR/$rid" > /dev/null

    if [ ${rid:0:3} = "win" ]; then
        zip "$OUTPUT_DIR/$fname.zip" * -x \*.pdb
    elif [ $rid = "android" ]; then
        find . -maxdepth 1 -type f \( -name *-Signed.apk -o -name *.aab \) | while read file; do
          echo "  copying: ${file:2}"
          cp -a "$file" "$OUTPUT_DIR"
        done
    else
        chmod +x "$NAME"  # make sure binary is executable
        tar -czvf "$OUTPUT_DIR/$fname.tar.gz" --exclude='*.pdb' *
    fi

    popd > /dev/null
done

echo
echo "Done."
echo
