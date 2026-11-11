#!/usr/bin/env bash
# Endless-mode online test with real headless players (WebSocket transport):
# a host and N clients on this PC. Clients join by ROOM CODE (127.0.0.1 -> B9S-AAAB) to test the code path.
# The host restarts 3 s after every game over, so river changes and replicated crash reasons are exercised.
# usage: Tools/net_endless_test.sh <outdir> [clients=1] [seconds=70]
set -u
OUT="${1:?outdir}"; N="${2:-1}"; SECS="${3:-70}"
EXE="D:/FOBuild/FartOnline.exe"
mkdir -p "$OUT"
"$EXE" -batchmode -nographics -logFile "$OUT/host_unity.log" -fo-net host -fo-listen 127.0.0.1 -fo-wait "$N" \
       -fo-secs "$SECS" -fo-restart 3 -fo-log "$OUT/host.txt" &
sleep 4
for i in $(seq 1 "$N"); do
  pat=right; [ "$i" = 2 ] && pat=back; [ "$i" = 3 ] && pat=idle
  "$EXE" -batchmode -nographics -logFile "$OUT/client${i}_unity.log" -fo-net join -fo-addr B9S-AAAB -fo-name "Client$i" \
         -fo-pattern "$pat" -fo-secs $((SECS - 6)) -fo-log "$OUT/client${i}.txt" &
  sleep 1
done
wait
echo "done -> $OUT"
