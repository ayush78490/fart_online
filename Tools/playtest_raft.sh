#!/usr/bin/env bash
# Enters Play Mode, samples the TestRaft every few seconds (position, distance along the river, floating state),
# then stops Play Mode. Usage: bash Tools/playtest_raft.sh [seconds=20] [interval=4]
cd "$(dirname "$0")/.."
U="python Tools/umcp.py"
DUR=${1:-20}; STEP=${2:-4}
$U call manage_editor '{"action":"play"}' >/dev/null
sleep 4
# keep simulating while the editor is unfocused (runtime flag only, project settings untouched)
$U call execute_code '{"action":"execute","code":"UnityEngine.Application.runInBackground = true; return \"bg on\";"}' >/dev/null
PROBE='var r = UnityEngine.GameObject.Find("TestRaft"); if (r == null) return "no raft"; var p = UnityEngine.Object.FindAnyObjectByType<FartOnline.Environment.RiverPath>(); float lat; var s = p.Closest(r.transform.position, out lat); var b = r.GetComponent<FartOnline.Water.RaftBuoyancy>(); var rb = r.GetComponent<UnityEngine.Rigidbody>(); return "t=" + UnityEngine.Time.time.ToString("F1") + " dist=" + s.distance.ToString("F1") + "m sec=" + s.section + " lateral=" + lat.ToString("F2") + " y=" + r.transform.position.y.ToString("F2") + " water=" + s.position.y.ToString("F2") + " inWater=" + b.InWater + " sub=" + b.SubmergedFraction.ToString("F2") + " speed=" + rb.linearVelocity.magnitude.ToString("F2") + " tilt=" + UnityEngine.Vector3.Angle(r.transform.up, UnityEngine.Vector3.up).ToString("F1");'
ARGS=$(python -c "import json,sys; print(json.dumps({'action':'execute','code':sys.argv[1]}))" "$PROBE")
for ((t=0; t<DUR; t+=STEP)); do
  $U call execute_code "$ARGS" | python -c "import sys,json; d=json.load(sys.stdin); print(d.get('data',{}).get('result', d))" 2>/dev/null
  sleep "$STEP"
done
$U call manage_editor '{"action":"stop"}' >/dev/null
echo "stopped"
