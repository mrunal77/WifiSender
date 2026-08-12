#!/usr/bin/env bash
# Generates release notes (grouped commit summary) from git history.
#
# Usage: release-notes.sh <current-tag> [previous-tag]
#
# When previous-tag is omitted, notes cover all commits reachable from the
# current tag. Commits are grouped by conventional-commit prefix.
set -euo pipefail

CURRENT="${1:?usage: release-notes.sh <current-tag> [previous-tag]}"
PREV="${2:-}"

RANGE="HEAD"
if [ -n "$PREV" ] && git rev-parse -q --verify "refs/tags/$PREV" >/dev/null 2>&1; then
    RANGE="$PREV..$CURRENT"
elif git rev-parse -q --verify "refs/tags/$CURRENT" >/dev/null 2>&1; then
    RANGE="$CURRENT"
fi

# shellcheck disable=SC2016
git log --no-merges --first-parent --format='%s%x09%b' "$RANGE" |
    awk -F '\t' '
        function emit(group, label) {
            if (count[group] > 0) {
                print ""
                print "### " label
                for (i = 0; i < order[group]; i++) {
                    print "- " lines[group, i]
                }
            }
        }
        {
            line = $1
            body = $2
            if (body != "" && index(line, "(") == 0 && index(line, ":") == 0) {
                line = line ": " body
            }
            key = ""
            if (line ~ /^feat(\(.*\))?:/) key = "features"
            else if (line ~ /^fix(\(.*\))?:/) key = "fixes"
            else if (line ~ /^perf(\(.*\))?:/) key = "performance"
            else if (line ~ /^docs(\(.*\))?:/) key = "docs"
            else if (line ~ /^refactor(\(.*\))?:/) key = "refactor"
            else if (line ~ /^build|^ci|^chore|^style|^test|^revert(\(.*\))?:/) key = "maintenance"
            else key = "other"
            if (count[key] == 0) order[key] = 0
            lines[key, order[key]] = line
            order[key]++
            count[key]++
        }
        END {
            print "## What changed"
            emit("features", "New features")
            emit("fixes", "Bug fixes")
            emit("performance", "Performance")
            emit("docs", "Documentation")
            emit("refactor", "Refactoring")
            emit("maintenance", "Maintenance")
            emit("other", "Other")
        }
    ' | sed '/^$/d'
