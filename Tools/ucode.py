"""Run a C# snippet file in the Unity Editor through MCP for Unity's execute_code.
usage: python Tools/ucode.py path/to/snippet.cs
The snippet is a method body (CodeDom / C# 6): use plain string concatenation and end with `return <value>;`.
"""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import umcp

code = open(sys.argv[1], encoding="utf-8").read()
sid = umcp.session()
_, msgs = umcp.post({"jsonrpc": "2.0", "id": 3, "method": "tools/call",
                     "params": {"name": "execute_code", "arguments": {"action": "execute", "code": code}}}, sid)
r = umcp.result(msgs)
for c in r.get("result", {}).get("content", []):
    print(c.get("text", c))
if "error" in r:
    print(json.dumps(r["error"]))
