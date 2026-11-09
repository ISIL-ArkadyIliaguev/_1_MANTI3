using System.Collections.Concurrent;
using Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;
using Manti3.Backend.Application.Features.AccessRequests.RunScript;

namespace Manti3.Backend.Infrastructure.Agent;

// קורא את script.sql מתיקיית הריצה ב-.agent-output ומסמן "הורץ ונשמר" בקובץ committed.txt באותה תיקייה.
// requestId כבר עבר ולידציה (yyyyMMdd-HHmm), ובנוסף הנתיב חייב להישאר בתוך .agent-output
internal sealed class AgentOutputStore(AgentOptions options, IAgentServers servers) : IAgentOutput
{
    // server.txt נכתב על ידי הסוכן-המפעיל. בלי הקובץ (בקשות ישנות): ברירת המחדל. שם שכבר לא ברשימה: חסום
    public bool IsRunBlockedByServer(string requestId)
    {
        var path = FilePath(requestId, "server.txt");
        if (path is null || !File.Exists(path))
            return false;

        var server = servers.Find(File.ReadAllText(path, System.Text.Encoding.UTF8).Trim());
        return server is null || !server.CanRunScripts;
    }

    private string Root => Path.GetFullPath(Path.Combine(options.WorkingDirectory, ".agent-output"));

    public string? ReadScript(string requestId)
    {
        var path = FilePath(requestId, "script.sql");
        return path is not null && File.Exists(path) ? File.ReadAllText(path, System.Text.Encoding.UTF8) : null;
    }

    public string? ReadSpec(string requestId)
    {
        var path = FilePath(requestId, "spec.json");
        return path is not null && File.Exists(path) ? File.ReadAllText(path, System.Text.Encoding.UTF8) : null;
    }

    public string? ReadServerName(string requestId)
    {
        var path = FilePath(requestId, "server.txt");
        return path is not null && File.Exists(path) ? File.ReadAllText(path, System.Text.Encoding.UTF8).Trim() : null;
    }

    public void WriteSpecAndScript(string requestId, string specJson, string script)
    {
        var specPath = FilePath(requestId, "spec.json")
                       ?? throw new InvalidOperationException("Request folder is outside .agent-output.");
        var scriptPath = FilePath(requestId, "script.sql")
                         ?? throw new InvalidOperationException("Request folder is outside .agent-output.");

        // הגרסה של הסוכן נשמרת פעם אחת לצד הערוכה, כראיה למה שהשתנה
        var original = Path.ChangeExtension(specPath, ".agent.json");
        if (!File.Exists(original) && File.Exists(specPath))
            File.Copy(specPath, original);

        File.WriteAllText(specPath, specJson, new System.Text.UTF8Encoding(false));
        File.WriteAllText(scriptPath, script, new System.Text.UTF8Encoding(true));
    }

    public bool IsCommitted(string requestId)
    {
        var path = FilePath(requestId, "committed.txt");
        return path is not null && File.Exists(path);
    }

    public void MarkCommitted(string requestId)
    {
        var path = FilePath(requestId, "committed.txt")
                   ?? throw new InvalidOperationException("Request folder is outside .agent-output.");
        File.WriteAllText(path, $"Committed {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
    }

    private string? FilePath(string requestId, string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(Root, requestId, fileName));
        return path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? path : null;
    }
}

// זיכרון בלבד: אחרי הפעלה מחדש של ה-API צריך שוב הרצה לניסיון לפני שמירה (בכוונה)
internal sealed class ScriptRunLedger : IScriptRunLedger
{
    private readonly ConcurrentDictionary<string, string> _dryRuns = new();

    public bool HasDryRun(string requestId, string scriptHash)
        => _dryRuns.TryGetValue(requestId, out var hash) && hash == scriptHash;

    public void RecordDryRun(string requestId, string scriptHash) => _dryRuns[requestId] = scriptHash;
}
