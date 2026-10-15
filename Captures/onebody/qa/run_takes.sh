#!/bin/bash
# Usage: run_takes.sh <project dir> <action>...  Fresh Debug session per action, then guen_take.cs with that action.
P="$1"; shift
export UNITY_PROJECT_PATH="$P"
S="C:/Users/Farza/AppData/Local/Temp/claude/C--Game-projects-LoomRoom/bb964bc9-abc6-43bf-80a8-772c0dea1768/scratchpad"
cd "$P"
for a in "$@"; do
  rm -rf "Temp/FirstPerson/gt_$a"
  unity command eval --no-banner 'DebugDungeonToolbar.StartDebug(); return "ok";' >/dev/null 2>&1
  sleep 6
  ok=0
  for i in $(seq 1 40); do
    r=$(unity command eval --no-banner 'var p = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null; var g = TableManager.Loader != null ? TableManager.Loader.Dungeon : null; return UnityEditor.EditorApplication.isPlaying + " " + (p != null ? p.kind.ToString() : "none") + " " + (g != null) + " " + (UnityEngine.Time.time > 2.5f);' 2>&1 | tail -1)
    echo "$r" | grep -q '"True Table True True"' && { ok=1; break; }
    sleep 3
  done
  [ $ok = 1 ] || { echo "$a: session did not start"; continue; }
  sed "s/__ACTION__/$a/" "C:/Game projects/LoomRoom/Captures/onebody/qa/guen_take.cs" > "$S/take_now.cs"
  res=$(unity command eval_file --no-banner --file "$(cygpath -w "$S/take_now.cs")" 2>&1 | tail -1 | grep -o '"result":"[^"]*"\|diagnostics":\[[^]]*\]\|Error.*' | head -2)
  for i in $(seq 1 30); do [ -f "Temp/FirstPerson/gt_$a/frames.csv" ] && break; sleep 3; done
  echo "$a: $res frames $( [ -f "Temp/FirstPerson/gt_$a/frames.csv" ] && wc -l < "Temp/FirstPerson/gt_$a/frames.csv" || echo none)"
done
unity command editor_stop --no-banner >/dev/null 2>&1
