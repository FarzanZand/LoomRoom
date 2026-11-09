#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

// The developer prompt's outbox (DevPrompt in the game, Asset Studio's "Ask Main" in the Editor): every request is an
// immutable folder under DevReports/<32-hex id>/ with screenshot.png and game-state.json written first and report.json
// published last (written to a temporary file, then moved), so a half-written report is never picked up. The Main
// Claude session reads new reports and writes receipt.json (picked up) and later triage.json (status, task, owner);
// the game only reads those. In the Editor the outbox sits beside Assets (git-ignored); in a development build it is
// under the player's persistent data. Nothing here ever sends anything anywhere.
public static class DevReportStore
{
    public const int MaxMessage = 6000;
    public const int MaxStateBytes = 128 * 1024;

    public static string Root
    {
        get
        {
#if UNITY_EDITOR
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "DevReports"));
#else
            return Path.Combine(Application.persistentDataPath, "DevReports");
#endif
        }
    }

    [Serializable]
    public class AssetRef
    {
        public string id;        // ast_ + the asset's Unity GUID (permanent: renames and moves keep it)
        public string name;
        public string path;
        public string category;
    }

    [Serializable]
    public class Report
    {
        public int schema_version = 1;
        public string report_id;
        public string created_at;
        public string message;
        public string context;            // "game" (Shift+Space in Play) or "studio" (Asset Studio)
        public string screenshot = "screenshot.png";
        public string state = "game-state.json";
        public string target = "main";   // "main" or "asset"
        public AssetRef asset;
    }

    // What the Main session writes back. receipt.json: it has the report. triage.json: what became of it.
    [Serializable] public class Receipt { public string report_id; public string received_at; public string session; }
    [Serializable] public class Triage { public string report_id; public string status; public string task; public string owner; public string updated_at; public string note; }

    public enum Status { Saved, Received, Working, Done, Failed }

    public class Entry
    {
        public string id, folder, message, target, assetName;
        public DateTime created;
        public Status status;
        public string note;
        public bool dismissed;
    }

    static readonly Regex IdPattern = new("^[0-9a-f]{32}$");
    public static bool ValidId(string id) => id != null && IdPattern.IsMatch(id);

    // Checks a request before it is saved; null when it is fine.
    public static string Validate(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Write what Main should look into first.";
        if (message.Length > MaxMessage) return $"That is {message.Length} characters; keep it under {MaxMessage}.";
        return null;
    }

    // Saves a request; returns its id, or throws (the caller keeps the draft). png may be null (no screenshot).
    public static string Save(string message, byte[] png, string stateJson, string context, AssetRef asset = null)
    {
        var problem = Validate(message);
        if (problem != null) throw new ArgumentException(problem);
        if (png != null && !IsPng(png)) throw new ArgumentException("The screenshot is not a PNG.");
        if (stateJson != null && System.Text.Encoding.UTF8.GetByteCount(stateJson) > MaxStateBytes) throw new ArgumentException("The game state is over 128 KiB.");

        string id = Guid.NewGuid().ToString("N");
        string folder = Path.Combine(Root, id);
        Directory.CreateDirectory(folder);
        var report = new Report
        {
            report_id = id,
            created_at = DateTime.UtcNow.ToString("o"),
            message = message.Trim(),
            context = context,
            target = asset != null ? "asset" : "main",
            asset = asset,
            screenshot = png != null ? "screenshot.png" : null,
            state = stateJson != null ? "game-state.json" : null,
        };
        if (png != null) WriteAtomic(Path.Combine(folder, "screenshot.png"), png);
        if (stateJson != null) WriteAtomic(Path.Combine(folder, "game-state.json"), System.Text.Encoding.UTF8.GetBytes(stateJson));
        // Last: its presence is what makes the report real.
        WriteAtomic(Path.Combine(folder, "report.json"), System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(report, true)));
        return id;
    }

    static bool IsPng(byte[] b) => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

    static void WriteAtomic(string path, byte[] bytes)
    {
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        if (new FileInfo(temp).Length != bytes.Length) throw new IOException("Short write: " + temp);
        if (File.Exists(path)) File.Delete(path);
        File.Move(temp, path);
    }

    // Every saved request, newest first, with its status from receipt.json and triage.json.
    public static List<Entry> List(int max = 50)
    {
        var list = new List<Entry>();
        if (!Directory.Exists(Root)) return list;
        var dismissed = Dismissed();
        foreach (var dir in Directory.GetDirectories(Root))
        {
            string id = Path.GetFileName(dir);
            if (!ValidId(id)) continue;
            string reportPath = Path.Combine(dir, "report.json");
            if (!File.Exists(reportPath)) continue;   // still being written, or broken
            Report r;
            try { r = JsonUtility.FromJson<Report>(File.ReadAllText(reportPath)); } catch { continue; }
            if (r == null || r.report_id != id) continue;
            var e = new Entry { id = id, folder = dir, message = r.message, target = r.target, assetName = r.asset?.name, status = Status.Saved, dismissed = dismissed.Contains(id) };
            DateTime.TryParse(r.created_at, null, System.Globalization.DateTimeStyles.RoundtripKind, out e.created);
            if (File.Exists(Path.Combine(dir, "receipt.json"))) e.status = Status.Received;
            string triagePath = Path.Combine(dir, "triage.json");
            if (File.Exists(triagePath))
            {
                try
                {
                    var t = JsonUtility.FromJson<Triage>(File.ReadAllText(triagePath));
                    e.note = t?.note;
                    e.status = t?.status switch { "done" => Status.Done, "failed" => Status.Failed, "working" => Status.Working, _ => e.status };
                }
                catch { }
            }
            list.Add(e);
        }
        list.Sort((a, b) => b.created.CompareTo(a.created));
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
        return list;
    }

    // Dismissing a finished request only hides it here (DevReports/dismissed.txt); a request that becomes active again shows again.
    static string DismissedPath => Path.Combine(Root, "dismissed.txt");
    static HashSet<string> Dismissed()
    {
        var set = new HashSet<string>();
        try { if (File.Exists(DismissedPath)) foreach (var l in File.ReadAllLines(DismissedPath)) if (ValidId(l.Trim())) set.Add(l.Trim()); } catch { }
        return set;
    }
    public static void Dismiss(string id)
    {
        if (!ValidId(id)) return;
        Directory.CreateDirectory(Root);
        File.AppendAllText(DismissedPath, id + "\n");
    }

    public static string ShortStatus(Entry e) => e.status switch
    {
        Status.Saved => "waiting for Main",
        Status.Received => "Main has it",
        Status.Working => "being worked on",
        Status.Done => "done",
        Status.Failed => "failed",
        _ => "",
    };
}
#endif
