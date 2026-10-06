#!/usr/bin/env python3
"""Client for the DD2 Estate dev bridge (see src/DD2Estate/Dev/AgentBridge.cs).

    python tools/bridge.py ping
    python tools/bridge.py observe [filter] [--all]
    python tools/bridge.py click <id | path>
    python tools/bridge.py tree [path] [depth]
    python tools/bridge.py get <expr> [depth]
    python tools/bridge.py members <expr> [filter]
    python tools/bridge.py invoke <expr> <method> [json-arg ...]   ("=expr" passes an object reference)
    python tools/bridge.py set <expr> <member> <json-value>
    python tools/bridge.py log [since] [filter]
    python tools/bridge.py shot <file.png>
    python tools/bridge.py run <name> [key=value ...]
    python tools/bridge.py raw '{"cmd":"..."}'

expr roots: Namespace.Type (static members), $TypeName (first live component of that type),
            @Path/To/Object:Component (a component on a scene object)
"""
import json
import os
import socket
import sys

PORT = int(os.environ.get("DD2ESTATE_BRIDGE_PORT", "7878"))


def send(req, timeout=40):
    with socket.create_connection(("127.0.0.1", PORT), timeout=timeout) as s:
        s.sendall((json.dumps(req) + "\n").encode("utf-8"))
        buf = b""
        while not buf.endswith(b"\n"):
            chunk = s.recv(65536)
            if not chunk:
                break
            buf += chunk
    return json.loads(buf.decode("utf-8"))


def coerce(v):
    if v in ("true", "false"):
        return v == "true"
    try:
        return int(v)
    except ValueError:
        try:
            return float(v)
        except ValueError:
            return v


def jsonish(a):
    """JSON if it parses (numbers, true/false/null, quoted strings, arrays), else the bare string."""
    try:
        return json.loads(a)
    except ValueError:
        return a


def build(argv):
    cmd, args = argv[0], argv[1:]
    if cmd == "raw":
        return json.loads(args[0])
    req = {"cmd": cmd}
    if cmd == "observe":
        if "--all" in args:
            req["all"] = True
            args = [a for a in args if a != "--all"]
        if args:
            req["filter"] = args[0]
    elif cmd == "click":
        if args[0].isdigit():
            req["id"] = int(args[0])
        else:
            req["path"] = args[0]
    elif cmd == "tree":
        if args:
            req["path"] = args[0]
        if len(args) > 1:
            req["depth"] = int(args[1])
    elif cmd == "get":
        req["expr"] = args[0]
        if len(args) > 1:
            req["depth"] = int(args[1])
    elif cmd == "members":
        req["expr"] = args[0]
        if len(args) > 1:
            req["filter"] = args[1]
    elif cmd == "invoke":
        req["expr"], req["method"] = args[0], args[1]
        req["args"] = [jsonish(a) for a in args[2:]]
    elif cmd == "set":
        req["expr"], req["member"], req["value"] = args[0], args[1], jsonish(args[2])
    elif cmd == "log":
        if args:
            req["since"] = int(args[0])
        if len(args) > 1:
            req["filter"] = args[1]
    elif cmd == "shot":
        req["path"] = os.path.abspath(args[0])
    elif cmd == "timescale":
        if args:
            req["value"] = float(args[0])
    elif cmd == "run":
        req["name"] = args[0]
        for kv in args[1:]:
            k, _, v = kv.partition("=")
            req[k] = coerce(v)
    return req


def show(cmd, reply):
    if not reply.get("ok"):
        print("ERROR:", reply.get("error"))
        if reply.get("stack"):
            print(reply["stack"])
        return 1
    r = reply.get("result")
    if cmd == "observe" and isinstance(r, dict):
        print("scenes:", ", ".join(r["scenes"]), "| screen", r["screen"], "| timeScale", r["timeScale"])
        for o in r["options"]:
            flags = ("" if o["interactable"] else " [disabled]") + ("" if o["top"] else " [covered]")
            print(f'{o["id"]:3} {o["label"][:50]:50} ({o["x"]},{o["y"]}) {o["type"]}{flags}  {o["path"]}')
    elif cmd == "log" and isinstance(r, dict):
        for line in r["lines"]:
            print(line)
        print("-- next:", r["next"])
    elif isinstance(r, str):
        print(r)
    elif cmd == "members" and isinstance(r, list):
        print("\n".join(r))
    else:
        print(json.dumps(r, indent=1, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    sys.exit(show(sys.argv[1], send(build(sys.argv[1:]))))
