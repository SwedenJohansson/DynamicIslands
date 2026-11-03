#!/usr/bin/env bash
# sweep.sh - sweep tool T6 (_ProjectDocs/plans/SWEEP_PLAN.md): the full test sweep's runner, one rq.sh slot job per island.
#
#   bash tools/sweep.sh [--dry-run] <host|box|box3> <island> [<island> ...]
#   <island> = the recipe/play name in Mods\DynamicIslands\recipes (big_top_atoll). --dry-run prints the steps, runs nothing
#   (no rq, no Raft). Env: ME (agent name, default sweep), OUT (default D:/Temp/scratch_8e762ddb/sweep), MAXMIN (rq take, 90).
#
# Per island, on the slot held: restart.sh (45 min / 6000 MB rule), main menu, a fresh "CI Sweep" world, then
#   1 walks     CIReachOn + CIPlay <island>: the quest played, the reach-and-escape grid + walks on what it leaves
#   2 pictures  CIReachOff + CIPlay <island> in a second fresh world: the play's pictures (4 sides + every target, +h)
#   3 checks    CIGroundIslands, CIOverlapIslands, CIInsideIslands (T2), CIReachTraps (T1), CIQuestNeeds (T5),
#               CIDensityIslands (T8), CICaveInside when the island has cave pieces (island next to the raft first)
#   4 save/load CIQuestCountCheck, CISave, main menu, CILoadWorld, CIQuestCountCheck again: the counts must match (M2/M3)
#   5 montage   the play's jpgs -> contactsheet.ps1 -> OUT/<island>/sheet_*.jpg (for the T7 judges, SWEEP_JUDGE_BRIEF.md)
#   6 row       one line into OUT/SWEEP_RESULTS.csv; the whole log in OUT/<island>/sweep.log
# Stall rule: a CI step that times out (ci.ps1 exit 3) or loses the slot ends the island as STALL: restart.sh <slot> 0
# (restarts that Raft now), the row says which step, and the runner goes on with the next island. watch.sh (HB alarms)
# should run beside it; the runner warns when it doesn't.
DRY=0; [ "$1" = "--dry-run" ] && { DRY=1; shift; }
RAFT="$1"; shift
case "$RAFT" in host) P=1; BX=""; MODS=/d/Games/SteamLibrary/steamapps/common/Raft/Mods/DynamicIslands;;
  box) P=2; BX=Raft2; MODS=/d/Sandbox/Johan/Raft2/drive/D/Games/SteamLibrary/steamapps/common/Raft/Mods/DynamicIslands;;
  box3) P=3; BX=Raft3; MODS=/d/Sandbox/Johan/Raft3/drive/D/Games/SteamLibrary/steamapps/common/Raft/Mods/DynamicIslands;;
  *) echo "usage: sweep.sh [--dry-run] <host|box|box3> <island> [...]" >&2; exit 2;; esac
[ $# -gt 0 ] || { echo "sweep.sh: no island given" >&2; exit 2; }
ME="${ME:-sweep}"; S=/d/Temp/scratch_8e762ddb; RQ=$S/rq/rq.sh; RESTART=$S/rq/restart.sh
OUT="${OUT:-$S/sweep}"; CSV=$OUT/SWEEP_RESULTS.csv
TOOLS='D:\Source K2H Final\trunkRaft\Raft\_ProjectDocs\tools'; CI="$TOOLS\\ci.ps1"
REC=/d/Games/SteamLibrary/steamapps/common/Raft/Mods/DynamicIslands/recipes   # (the plays and recipes: the host copy names the island)

LOG=/dev/null; STALL=""
say(){ echo "$*" | tee -a "$LOG"; }
# ci <label> <commands> <until> <timeout>: one CI step; keeps the PASS/FAIL lines in R[label]; a timeout or a lost slot = STALL
declare -A R
ci(){ local label=$1 cmd=$2 until=$3 t=$4 out rc
  if [ $DRY = 1 ]; then say "  [$label] ci.ps1 -player $P \"$cmd\" until \"$until\" (${t}s)"; R[$label]=dry; return 0; fi
  [ -n "$STALL" ] && return 1
  bash $RQ check $RAFT $ME || { STALL="$label: slot lost"; return 1; }
  out=$(powershell -NoProfile -ExecutionPolicy Bypass -File "$CI" "$cmd" "$until" "$t" -player $P ${BX:+-box $BX} 2>&1; echo "rc=$?"); rc=$(echo "$out" | tail -1 | tr -dc '0-9'); out=$(echo "$out" | sed '$d' | tr -d '\r')
  echo "$out" >> "$LOG"
  R[$label]=$(echo "$out" | grep -oE "(PASS|FAIL): .{0,90}" | tail -1 | tr ',;' '  ')
  [ -z "${R[$label]}" ] && R[$label]="rc $rc"
  echo "$out" | grep -q "timed out\|TIMEOUT" && rc=3
  [ $rc = 3 ] && { STALL="$label: timed out after ${t}s"; return 1; }
  return 0; }
fresh(){ ci menu "CIMainMenu" "main menu|Main menu|PASS|FAIL" 60
  ci world "CINewWorld CI Sweep $(date +%H%M%S)" "Hotbar key tabs|FAIL" 150
  ci ready "CIBackground;CIAlive;CIClearInventory;CIOnRaft" "On the raft|FAIL" 90; }

if [ $DRY = 0 ]; then
  mkdir -p "$OUT"
  [ -f "$CSV" ] || echo "island,raft,date,minutes,result,walks,pictures,ground,overlap,inside,traps,needs,density,caves,saveload,sheets,stall" > "$CSV"
  pgrep -f watch.sh > /dev/null 2>&1 || ps -ef 2>/dev/null | grep -q "[w]atch.sh" || echo "sweep: watch.sh is not running - hangs won't raise an alarm (bash $S/watch.sh)" >&2
fi

for isl in "$@"; do
  R=(); STALL=""; t0=$(date +%s)
  [ $DRY = 0 ] && { mkdir -p "$OUT/$isl"; LOG=$OUT/$isl/sweep.log; : > "$LOG"; }
  # (the saved island's name: the play's island line, else the recipe name; _ stands for a space in a CI <which>)
  name=$(grep -m1 "^island " "$REC/$isl.play" 2>/dev/null | cut -d' ' -f2- | tr -d '\r'); [ -z "$name" ] && name=$isl
  which=$(echo "$name" | tr ' ' '_')
  say "== $isl ($name) on $RAFT $(date +%H:%M)"
  if [ $DRY = 1 ]; then say "  [slot] rq.sh join $RAFT $ME 60; rq.sh take $RAFT $ME --max ${MAXMIN:-90}; restart.sh $RAFT 45 6000"
  else bash $RQ join $RAFT $ME 60 > /dev/null; bash $RQ take $RAFT $ME --max ${MAXMIN:-90} || { say "  no slot"; continue; }; bash $RESTART $RAFT 45 6000 >> "$LOG" 2>&1; fi
  fresh
  ci walks "CIReachOn sweep;CIPlay $isl" "(PASS|FAIL): play $isl" 1500
  fresh
  ci pictures "CIReachOff;CIPlay $isl" "(PASS|FAIL): play $isl" 1200
  ci ground "CIGroundIslands $which 1" "PASS: ground|FAIL" 600
  ci overlap "CIOverlapIslands $which 1" "PASS: overlap|FAIL" 600
  ci inside "CIInsideIslands $which 1" "PASS: inside|FAIL" 600
  ci traps "CIReachTraps $which 1" "PASS: reach traps|FAIL" 900
  ci needs "CIQuestNeeds $which 1" "PASS: quest needs|FAIL" 300
  ci density "CIDensityIslands $which 1" "PASS: density|FAIL" 120
  # (caves: the island next to the raft and kept, then looked round from inside; only when its recipe has cave pieces)
  if [ $DRY = 1 ] || grep -qiE "cave|tunnel" "$REC/$isl.recipe" 2>/dev/null; then
    ci caves "CIGroundIslands $which 1 0 keep;CICaveInside" "PASS: cave|FAIL" 600
  else R[caves]="no caves"; fi
  ci count1 "CIQuestCountCheck" "QCOUNT|FAIL" 60; c1=$(grep -oE "QCOUNT [0-9]+/[0-9]+" "$LOG" 2>/dev/null | tail -1)
  ci save "CISave" "PASS|FAIL|aved" 60
  ci menu2 "CIMainMenu" "main menu|Main menu|PASS|FAIL" 60
  ci load "CILoadWorld CI Sweep" "Hotbar key tabs|FAIL" 180
  ci count2 "CIQuestCountCheck" "QCOUNT|FAIL" 60; c2=$(grep -oE "QCOUNT [0-9]+/[0-9]+" "$LOG" 2>/dev/null | tail -1)
  if [ $DRY = 1 ]; then R[saveload]=dry; elif [ -n "$c1" ] && [ "$c1" = "$c2" ]; then R[saveload]="same $c1"; else R[saveload]="FAIL: $c1 -> $c2"; fi
  if [ $DRY = 1 ]; then say "  [slot] rq.sh release $RAFT $ME"
  else
    [ -n "$STALL" ] && { say "  STALL $STALL - restarting $RAFT"; bash $RESTART $RAFT 0 >> "$LOG" 2>&1; }
    bash $RQ release $RAFT $ME > /dev/null
  fi
  # montage: the play's pictures (play_<island>*.jpg in this Raft's Mods folder) on contact sheets for the judges
  if [ $DRY = 1 ]; then say "  [montage] copy $MODS/play_${isl}*.jpg -> \$OUT/$isl/pics; contactsheet.ps1 -> \$OUT/$isl/sheet_*.jpg"; sheets=dry
  else
    mkdir -p "$OUT/$isl/pics"; cp "$MODS"/play_${isl}*.jpg "$OUT/$isl/pics/" 2>/dev/null
    powershell -NoProfile -ExecutionPolicy Bypass -File "$TOOLS\\contactsheet.ps1" -src "$(cygpath -w "$OUT/$isl/pics")" -out "$(cygpath -w "$OUT/$isl")" -filter "*.jpg" -per 6 > /dev/null 2>&1
    sheets=$(ls "$OUT/$isl" 2>/dev/null | grep -ci "sheet")
  fi
  bad=0; for k in walks pictures ground overlap inside traps needs density caves saveload; do case "${R[$k]}" in FAIL*|rc*) bad=1;; esac; done
  res=PASS; [ $bad = 1 ] && res=FAIL; [ -n "$STALL" ] && res=STALL
  row="$isl,$RAFT,$(date +%F),$(( ($(date +%s) - t0) / 60 )),$res,${R[walks]},${R[pictures]},${R[ground]},${R[overlap]},${R[inside]},${R[traps]},${R[needs]},${R[density]},${R[caves]},${R[saveload]},$sheets,$STALL"
  if [ $DRY = 1 ]; then say "  [row] -> SWEEP_RESULTS.csv: $row"; else echo "$row" >> "$CSV"; say "  $res ($(( ($(date +%s) - t0) / 60 )) min)${STALL:+ - $STALL}"; fi
done
