#!/usr/bin/env bash
set -euo pipefail

L="${1:-DebugTrace.log}"
if [[ ! -f "$L" ]]; then
  echo "Log not found: $L" >&2
  exit 2
fi

echo "A minScore unique:"
grep -oE 'minScore=[0-9.]+' "$L" | sort -u || true

echo
echo "B mode/clientCoords:"
grep -oE 'mode=[a-z]+' "$L" | sort | uniq -c || true
grep -oE 'clientCoords=(True|False)' "$L" | sort | uniq -c || true

echo
echo "C gate:"
grep -oE 'raw=[0-9]+ entities=[0-9]+' "$L" \
 | awk -F'[= ]' '{if($2>$4)g++; if($2<$4)b++; t++} END{printf "frames=%d raw>ent=%d raw<ent=%d => %s\n",t+0,g+0,b+0,(b?"FAIL":(g?"OK":"SUSPECT"))}' || true

echo
echo "D attackable subset samples:"
grep -oE 'entities=[0-9]+ .*attackable=[0-9]+' "$L" | sort | uniq -c | head || true

echo
echo "F track lock (top ids):"
grep -oE 'trk#[0-9]+' "$L" | sort | uniq -c | sort -rn | head || true
