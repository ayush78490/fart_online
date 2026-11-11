#!/usr/bin/env bash
# Local end-to-end test of AWS-style rooms on this PC (no AWS, no Supabase):
#   room service (DEV_AUTH, plain ws) -> dedicated room server (Windows build, -fo-dedicated)
#   bot "alice" creates a room and leads; bot "bobby" joins with the room code; both push a voice test tone.
#   Extra checks: a wrong room code is refused (404); a second login of "alice" is rejected by the room server.
# Usage: Tools/room_local_test.sh [seconds] [out-dir]
set -u
SECS="${1:-60}"
OUT="${2:-ArtSource/Game/NetTests/room1_local}"
EXE="/d/FOBuild/FartOnline.exe"
mkdir -p "$OUT"
rm -f "$OUT"/*.txt "$OUT"/*.log

export PORT=18080 BIND=127.0.0.1 SUPABASE_URL=http://unused SUPABASE_ANON_KEY=unused
export TICKET_SECRET="local-test-secret-0123456789abcdef0123456789"
export GAME_SERVER_BIN="$EXE" GAME_SERVER_ARGS="-batchmode -nographics"
if [ "${SERVER:-}" = "wsl" ]; then
  # room servers run the real Linux dedicated server build inside WSL (Ubuntu); the secret crosses over via WSLENV
  export GAME_SERVER_BIN="wsl.exe" GAME_SERVER_ARGS="-d Ubuntu -- /mnt/d/FOBuild/Server/FartOnlineServer.x86_64 -batchmode -nographics"
  export WSLENV="FO_TICKET_SECRET"
fi
export PUBLIC_HOST=127.0.0.1 PUBLIC_PORT_BASE=7800 MAX_ROOMS=2 TLS=0 DEV_AUTH=1
node Backend/room-service/server.js > "$OUT/room-service.log" 2>&1 &
SVC=$!
trap 'kill $SVC 2>/dev/null; taskkill //F //IM FartOnline.exe >/dev/null 2>&1; [ "${SERVER:-}" = "wsl" ] && MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu -- pkill -f FartOnlineServer >/dev/null 2>&1' EXIT
sleep 1.5
curl -s http://127.0.0.1:18080/health; echo

"$EXE" -batchmode -nographics -logFile "$OUT/alice_unity.log" -fo-net room -fo-rooms http://127.0.0.1:18080 -fo-name alice -fo-wait 2 \
       -fo-secs "$SECS" -fo-voice tone -fo-restart 3 -fo-pattern back -fo-log "$(cygpath -w "$OUT")\\alice.txt" &
for i in $(seq 1 120); do grep -q "ROOM CODE=" "$OUT/alice.txt" 2>/dev/null && break; sleep 1; done
CODE="$(grep -o 'ROOM CODE=[A-Z0-9]*' "$OUT/alice.txt" | head -1 | cut -d= -f2)"
echo "room code: $CODE"
[ -n "$CODE" ] || { echo "FAIL: no room created"; cat "$OUT/room-service.log"; exit 1; }

echo "wrong code -> $(curl -s -o /dev/null -w '%{http_code}' -X POST -H 'Authorization: Bearer dev:mallory' -d '{"code":"ZZZZZZ"}' http://127.0.0.1:18080/rooms/join)"
echo "no login   -> $(curl -s -o /dev/null -w '%{http_code}' -X POST -d '{"code":"'"$CODE"'"}' http://127.0.0.1:18080/rooms/join)"

"$EXE" -batchmode -nographics -logFile "$OUT/bobby_unity.log" -fo-net room -fo-rooms http://127.0.0.1:18080 -fo-name bobby -fo-code "$CODE" \
       -fo-secs "$((SECS - 8))" -fo-voice tone -fo-pattern right -fo-log "$(cygpath -w "$OUT")\\bobby.txt" &
sleep 12
"$EXE" -batchmode -nographics -logFile "$OUT/alice2_unity.log" -fo-net room -fo-rooms http://127.0.0.1:18080 -fo-name alice -fo-code "$CODE" \
       -fo-secs 12 -fo-log "$(cygpath -w "$OUT")\\alice2.txt" &
wait $(jobs -p | grep -v "^$SVC$") 2>/dev/null
sleep 2
echo "---- room service"; cat "$OUT/room-service.log"
for f in alice bobby alice2; do echo "---- $f"; grep -E "ROOM|connected|CONNECT|players=|leader|GAME OVER|restarts|END|FAILED|Disconnected|disconnected" "$OUT/$f.txt" | head -20; tail -1 "$OUT/$f.txt"; done
grep -h "Disconnect\|rejected\|already on this raft" "$OUT"/*_unity.log | head -5
