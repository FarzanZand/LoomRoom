"""The Main session's side of the developer prompt outbox (DevReports/, written by DevReportStore in the game and
Asset Studio). The game only reads what this writes:

    python Tools/devreport.py new                      reports with no receipt yet (one line each: id, context, message)
    python Tools/devreport.py show <id>                the report, its game state and the screenshot's path
    python Tools/devreport.py receive <id> [session]   receipt.json: Main has it (the game shows "Main has it")
    python Tools/devreport.py triage <id> <status> <owner> [note]
                                                       triage.json: status working|done|failed, who has it, a short note
    python Tools/devreport.py list                     every report with its status
    python Tools/devreport.py watch                    blocks until a report with no receipt comes in (during Play too),
                                                       then lists the new ones and ends: Main keeps it running in the
                                                       background so each request reaches it as it is sent
    python Tools/devreport.py wait                     blocks until Play has stopped after the newest open game request
                                                       (DevReports/.play-stopped, written by DevReportPlayStop in the
                                                       editor; Asset Studio requests need no stop), then lists the open
                                                       ones and ends: Main acts on requests only after this ends
    python Tools/devreport.py hook                     the SessionStart hook (.claude/settings.local.json): for Main's
                                                       session only, tells it to start the watcher and lists what is open

Main's loop: watch (background) -> receive what came, say it is noted -> wait (background) and watch again -> act once
Play has stopped. The hook brings the loop back after every restart, so the user never has to ask.

Writes are atomic (temporary file, then replace). Report folders are never changed otherwise."""
import json, os, sys, datetime, re

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "DevReports")
ID = re.compile(r"^[0-9a-f]{32}$")


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def folder(rid):
    if not ID.match(rid or ""):
        sys.exit(f"not a report id: {rid}")
    path = os.path.join(ROOT, rid)
    if not os.path.isfile(os.path.join(path, "report.json")):
        sys.exit(f"no report {rid}")
    return path


def write(path, data):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=4)
    os.replace(tmp, path)


def reports():
    if not os.path.isdir(ROOT):
        return []
    out = []
    for rid in os.listdir(ROOT):
        p = os.path.join(ROOT, rid, "report.json")
        if ID.match(rid) and os.path.isfile(p):
            with open(p, encoding="utf-8") as f:
                r = json.load(f)
            r["_dir"] = os.path.join(ROOT, rid)
            out.append(r)
    return sorted(out, key=lambda r: r.get("created_at", ""))


def status(r):
    t = os.path.join(r["_dir"], "triage.json")
    if os.path.isfile(t):
        with open(t, encoding="utf-8") as f:
            return json.load(f).get("status", "?")
    return "received" if os.path.isfile(os.path.join(r["_dir"], "receipt.json")) else "new"


def one_line(r):
    msg = r.get("message", "").replace("\n", " ")
    asset = r.get("asset") or {}
    about = f" [{asset.get('name')}]" if asset.get("name") else ""
    return f"{r['report_id']}  {r.get('context')}{about}  {msg[:120]}"


def main(argv):
    cmd = argv[1] if len(argv) > 1 else "list"
    if cmd == "new":
        for r in reports():
            if status(r) == "new":
                print(one_line(r))
    elif cmd == "list":
        for r in reports():
            print(f"{status(r):9} {one_line(r)}")
    elif cmd == "show":
        d = folder(argv[2])
        with open(os.path.join(d, "report.json"), encoding="utf-8") as f:
            print(f.read())
        s = os.path.join(d, "game-state.json")
        if os.path.isfile(s):
            with open(s, encoding="utf-8") as f:
                print(f.read())
        png = os.path.join(d, "screenshot.png")
        if os.path.isfile(png):
            print("screenshot:", os.path.abspath(png))
    elif cmd == "receive":
        d = folder(argv[2])
        write(os.path.join(d, "receipt.json"), {"report_id": argv[2], "received_at": now(), "session": argv[3] if len(argv) > 3 else "Main"})
    elif cmd == "triage":
        d = folder(argv[2])
        if argv[3] not in ("working", "done", "failed"):
            sys.exit("status is working, done or failed")
        if not os.path.isfile(os.path.join(d, "receipt.json")):
            write(os.path.join(d, "receipt.json"), {"report_id": argv[2], "received_at": now(), "session": "Main"})
        write(os.path.join(d, "triage.json"), {"report_id": argv[2], "status": argv[3], "owner": argv[4] if len(argv) > 4 else "Main",
                                                "note": argv[5] if len(argv) > 5 else "", "task": "", "updated_at": now()})
    elif cmd == "wait":
        wait()
    elif cmd == "watch":
        watch()
    elif cmd == "hook":
        hook()
    else:
        sys.exit(__doc__)


def stopped_at():
    # The UTC time Play last stopped, as the editor wrote it ("" when it never has); compared to the seconds.
    p = os.path.join(ROOT, ".play-stopped")
    if not os.path.isfile(p):
        return ""
    with open(p, encoding="utf-8") as f:
        return f.read().strip()[:19]


def open_reports():
    return [r for r in reports() if status(r) in ("new", "received")]


def ready(rs):
    # Play has stopped after the newest open game request (an Asset Studio request is made outside Play).
    games = [r.get("created_at", "")[:19] for r in rs if r.get("context") == "game"]
    return bool(rs) and (not games or stopped_at() >= max(games))


def watch(poll=2.0):
    import time
    while True:
        fresh = [r for r in reports() if status(r) == "new"]
        if fresh:
            print("new requests")
            for r in fresh:
                print(one_line(r))
            return
        time.sleep(poll)


def wait(poll=3.0):
    import time
    while True:
        rs = open_reports()
        if ready(rs):
            print("play stopped")
            for r in rs:
                print(f"{status(r):9} {one_line(r)}")
            return
        time.sleep(poll)


# Main's session id (the Guen - R&D chat). Other sessions in this folder get nothing from the hook.
MAIN_SESSIONS = {"bb964bc9-abc6-43bf-80a8-772c0dea1768"}


def hook():
    try:
        sid = json.load(sys.stdin).get("session_id", "")
    except Exception:
        sid = ""
    if sid not in MAIN_SESSIONS:
        return
    rs = open_reports()
    lines = [
        "You are Main for the developer prompt outbox (DevReports/). Right away, start the watcher in the background "
        "(Bash, run_in_background): python Tools/devreport.py watch. Each time it ends, mark every new request received "
        "(python Tools/devreport.py receive <id> Guen), tell the user in one line per request that it is noted, start "
        "python Tools/devreport.py wait in the background, and start watch again. Do no work on the requests (no edits, "
        "no editor use, no handing out) until wait has ended, which means Play has stopped; then do or route them. "
        "Route by waking the teammate yourself (mcp__ccd_session_mgmt__send_message to her local_ session id from "
        "list_sessions; offline sessions wake on it). Never wait for the user to remind you of any of this.",
    ]
    if rs:
        lines.append("Open requests now (" + ("Play has stopped since: act on them" if ready(rs) else "Play still running: hold") + "):")
        lines += [f"{status(r):9} {one_line(r)}" for r in rs]
    print("\n".join(lines))


if __name__ == "__main__":
    main(sys.argv)
