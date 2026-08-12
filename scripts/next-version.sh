#!/usr/bin/env bash
# Computes the next release version from the latest git tag.
#
# Usage: next-version.sh <patch|minor|major|prerelease> [tag-prefix]
#
#   patch       bump the patch component:  v1.4.3        -> v1.4.4
#   minor       bump the minor component:  v1.4.3        -> v1.5.0
#   major       bump the major component:  v1.4.3        -> v2.0.0
#   prerelease  next release candidate:    v1.4.3        -> v1.4.3-rc.1
#                                          v1.4.3-rc.1   -> v1.4.3-rc.2
#
# A pending prerelease (e.g. v1.4.3-rc.2) resolves to its stable base when
# patch/minor/major is requested: patch -> v1.4.3, minor -> v1.5.0.
# With no tags at all, versioning starts at 1.0.0 (the MinVer minimum).
set -euo pipefail

BUMP="${1:?usage: next-version.sh <patch|minor|major|prerelease> [tag-prefix]}"
PREFIX="${2:-v}"
MIN_MAJOR=1
MIN_MINOR=0

case "$BUMP" in
    patch|minor|major|prerelease) ;;
    *) echo "error: bump must be one of patch|minor|major|prerelease" >&2; exit 1 ;;
esac

parse() {
    # parses a tag into "major minor patch prerel" on stdout, empty if invalid
    local tag="$1"
    if [[ "$tag" =~ ^v([0-9]+)\.([0-9]+)\.([0-9]+)(-[0-9A-Za-z.-]+)?$ ]]; then
        printf '%s %s %s %s' "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}" "${BASH_REMATCH[3]}" "${BASH_REMATCH[4]:-}"
    fi
}

# All tags sorted highest-first (SemVer-aware), newest release candidates first.
mapfile -t ALL_TAGS < <(git tag --list "$PREFIX*" --sort=-version:refname || true)

declare -a STABLE_TAGS=()
for t in "${ALL_TAGS[@]:-}"; do
    [[ "$t" != *-* ]] && STABLE_TAGS+=("$t")
done

latest_all="${ALL_TAGS[0]:-}"
latest_stable="${STABLE_TAGS[0]:-}"

read -r mj mn pt pre <<<"$(parse "$latest_all")"
if [ -z "$mj" ]; then
    # No tags at all: start at the MinVer minimum.
    case "$BUMP" in
        patch)      echo "1.0.0" ;;
        minor)      echo "1.1.0" ;;
        major)      echo "2.0.0" ;;
        prerelease) echo "1.0.0-rc.1" ;;
    esac
    exit 0
fi

case "$BUMP" in
    patch)
        if [ -n "$pre" ]; then
            # Pending prerelease: stable release is its base version.
            echo "$mj.$mn.$pt"
        else
            echo "$mj.$mn.$((pt + 1))"
        fi
        ;;
    minor)
        echo "$mj.$((mn + 1)).0"
        ;;
    major)
        echo "$((mj + 1)).0.0"
        ;;
    prerelease)
        if [ -n "$pre" ] && [[ "$pre" =~ ^-rc\.([0-9]+)$ ]]; then
            # Bump an existing release candidate on the same base.
            echo "$mj.$mn.$pt-rc.$((10#${BASH_REMATCH[1]} + 1))"
        else
            # Latest tag is stable or a non-rc prerelease: start rc.1 on a
            # patch bump of the latest stable (or the latest tag's base).
            if [ -z "$pre" ]; then
                echo "$mj.$mn.$((pt + 1))-rc.1"
            else
                echo "$mj.$mn.$pt-rc.1"
            fi
        fi
        ;;
esac
