"""The Main session's side of the developer prompt outbox (DevReports/, written by DevReportStore in the game and
Asset Studio). The game only reads what this writes:

    python Tools/devreport.py new                      reports with no receipt yet (one line each: id, context, message)
    python Tools/devreport.py show <id>                the report, its game state and the screenshot's path
    python Tools/devreport.py receive <id> [session]   receipt.json: Main has it (the game shows "Main has it")
    python Tools/devreport.py triage <id> <status> <owner> [note]
                                                       triage.json: status working|done|failed, who has it, a short note
    python Tools/devreport.py list                     every report with its status
    python Tools/devreport.py wait                     blocks until there are new reports and Play has stopped since the
                                                       newest (DevReports/.play-stopped, written by DevReportPlayStop in
                                                       the editor), then lists them and ends: Main runs it in the
                                                       background so stopping Play wakes it

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
    else:
        sys.exit(__doc__)


def stopped_at():
    p = os.path.join(ROOT, ".play-stopped")
    return os.path.getmtime(p) if os.path.isfile(p) else 0.0


def wait(poll=3.0):
    # Ends once Play stops (the marker is written again) while some request is still untriaged, or at once when an Asset
    # Studio request comes in (made outside Play, so no stop will follow it).
    import time
    start = stopped_at()
    seen = {r["report_id"] for r in reports()}
    while True:
        time.sleep(poll)
        rs = reports()
        open_ = [r for r in rs if status(r) in ("new", "received")]
        fresh_studio = [r for r in rs if r["report_id"] not in seen and r.get("context") != "game"]
        if (stopped_at() > start and open_) or fresh_studio:
            print("play stopped" if stopped_at() > start else "studio request")
            for r in open_:
                print(f"{status(r):9} {one_line(r)}")
            return


if __name__ == "__main__":
    main(sys.argv)
