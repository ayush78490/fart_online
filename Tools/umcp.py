"""Minimal MCP-over-HTTP client for the MCP for Unity server.
usage: python umcp.py tools                      -> list tool names
       python umcp.py call <tool> '<json args>'
       python umcp.py read <uri>
       python umcp.py schema <tool>
"""
import json, sys, urllib.request

URL = "http://127.0.0.1:58421/mcp"
HDR = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}

def post(payload, sid=None, timeout=600):
    h = dict(HDR)
    if sid:
        h["mcp-session-id"] = sid
    req = urllib.request.Request(URL, json.dumps(payload).encode(), h)
    with urllib.request.urlopen(req, timeout=timeout) as r:
        sid = r.headers.get("mcp-session-id") or sid
        body = r.read().decode("utf-8", "replace")
    msgs = []
    for line in body.splitlines():
        if line.startswith("data:"):
            try:
                msgs.append(json.loads(line[5:].strip()))
            except Exception:
                pass
    if not msgs and body.strip().startswith("{"):
        msgs.append(json.loads(body))
    return sid, msgs

def session():
    sid, _ = post({"jsonrpc": "2.0", "id": 1, "method": "initialize",
                   "params": {"protocolVersion": "2025-03-26", "capabilities": {},
                              "clientInfo": {"name": "claude-code", "version": "1"}}})
    post({"jsonrpc": "2.0", "method": "notifications/initialized"}, sid)
    return sid

def result(msgs):
    for m in msgs:
        if "result" in m or "error" in m:
            return m
    return msgs

if __name__ == "__main__":
    sid = session()
    cmd = sys.argv[1]
    if cmd == "tools":
        _, m = post({"jsonrpc": "2.0", "id": 2, "method": "tools/list"}, sid)
        for t in result(m)["result"]["tools"]:
            print(t["name"], "-", t.get("description", "").split("\n")[0][:110])
    elif cmd == "schema":
        _, m = post({"jsonrpc": "2.0", "id": 2, "method": "tools/list"}, sid)
        for t in result(m)["result"]["tools"]:
            if t["name"] == sys.argv[2]:
                print(json.dumps(t["inputSchema"], indent=1)[:6000])
    elif cmd == "call":
        args = json.loads(sys.argv[3]) if len(sys.argv) > 3 else {}
        _, m = post({"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": sys.argv[2], "arguments": args}}, sid)
        r = result(m)
        if "result" in r:
            for c in r["result"].get("content", []):
                print(c.get("text", c)[:20000] if isinstance(c, dict) else c)
        else:
            print(json.dumps(r)[:4000])
    elif cmd == "read":
        _, m = post({"jsonrpc": "2.0", "id": 4, "method": "resources/read", "params": {"uri": sys.argv[2]}}, sid)
        r = result(m)
        for c in r.get("result", {}).get("contents", []):
            print(c.get("text", "")[:20000])
        if "error" in r:
            print(json.dumps(r["error"]))
