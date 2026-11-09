using System.Diagnostics;
using System.Text;
using Manti3.Backend.Application.Features.AccessRequests.BuildScript;
using Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;
using Microsoft.Extensions.Logging;

namespace Manti3.Backend.Infrastructure.Agent;

// מפעיל את Claude Code במצב לא אינטראקטיבי (claude -p) בתיקיית MANTI3.
// הסוכן מריץ רק SELECT מול SQL25DEV (נאכף ב-hook) וכותב plan.md ו-script.sql ל-.agent-output.
internal sealed class ClaudeCliAgent(
    AgentOptions options, AgentLookups lookups, AgentServers servers, Application.Features.AccessRequests.EditSpec.IAccessLookups accessLookups,
    ILogger<ClaudeCliAgent> logger)
    : IAccessRequestAgent
{
    // ריצה אחת בכל פעם: שתי ריצות במקביל היו כותבות לתיקיות באותה דקה
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    // אותם כלים כמו בהרצה ידנית. PowerShell חסום, Bash מותר רק לפקודות האלה
    private const string AllowedTools =
        "Read,Write,Edit,Glob,Grep,Agent,Task,Bash(sqlcmd *),Bash(pdftotext *),Bash(date *),Bash(ls *)";

    public async Task<AgentRun> Process(AgentInput input, CancellationToken ct)
    {
        await OneAtATime.WaitAsync(ct);
        try
        {
            return await Run(input, ct);
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    private async Task<AgentRun> Run(AgentInput input, CancellationToken ct)
    {
        // השרת נבחר מהרשימה המוגדרת בלבד (שם לא מוכר נדחה כבר ב-API); ברירת המחדל SQL25DEV
        var server = servers.Find(input.Server)
                     ?? throw new InvalidOperationException("Unknown server for the agent run.");
        if (!server.Ready)
            throw new InvalidOperationException("The login for this server is not configured.");
        var login = server.UsesSqlLogin ? servers.LoginFor(server) : null;

        var outputRoot = Path.Combine(options.WorkingDirectory, ".agent-output");
        var inputsDir = Path.Combine(outputRoot, "inputs");
        Directory.CreateDirectory(inputsDir);

        // הקלט נשמר ב-.agent-output (מחוץ ל-git) כמו בהרצה ידנית
        var stamp = $"web-{DateTime.Now:yyyyMMdd-HHmmss}";
        var inputName = $"{stamp}{input.Extension}";
        var content = input.Content;

        // טפסי PDF שהגיעו יחד עם טקסט/מיילים: נשמרים כקבצים, וברשימה בסוף המסמך כדי שהסוכן יקרא אותם
        if (input.Attachments is { Count: > 0 })
        {
            var footer = new StringBuilder()
                .AppendLine()
                .AppendLine()
                .AppendLine("=== Attached PDF forms (certification forms from the training system) ===")
                .AppendLine("They are part of this same request. Read each one with pdftotext as described in")
                .AppendLine("references/parsing.md (\"Input type 2\"), together with everything above.");
            for (var i = 0; i < input.Attachments.Count; i++)
            {
                var name = $"{stamp}-attachment{i + 1}{input.Attachments[i].Extension}";
                await File.WriteAllBytesAsync(Path.Combine(inputsDir, name), input.Attachments[i].Content, ct);
                footer.AppendLine($"- PDF {i + 1} of {input.Attachments.Count}: .agent-output/inputs/{name}");
            }

            content = [.. content, .. Encoding.UTF8.GetBytes(footer.ToString())];
        }

        await File.WriteAllBytesAsync(Path.Combine(inputsDir, inputName), content, ct);

        // שאילתות האימות הקבועות רצות כאן, בשרת, בלי תורי מודל. הסוכן קורא תוצאה אחת במקום 5-10 סבבי שאילתות.
        // אם זה נכשל (למשל DB לא זמין) הסוכן ישאל בעצמו, כמו קודם
        var lookupsName = $"{stamp}-lookups.md";
        var lookupsText = input.Extension == ".md"
            ? await lookups.Run(Encoding.UTF8.GetString(input.Content), server, ct)
            : null;
        if (lookupsText is not null)
            await File.WriteAllTextAsync(Path.Combine(inputsDir, lookupsName), lookupsText, new UTF8Encoding(true), ct);

        var before = RequestFolders(outputRoot).Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var startedUtc = DateTime.UtcNow;

        // הסשן רץ כסוכן עצמו (--agent): בלי תור נוסף של מודל שמפעיל תת-סוכן ובלי תור שחוזר על הסיכום שלו
        var prompt = $"Process the request in .agent-output/inputs/{inputName} against the database server " +
                     $"{server.DataSource} (database Manti_IscarData): every sqlcmd query you run uses " +
                     $"-S {server.DataSource} and no other server" +
                     (login is { } l
                         ? $", with -U {l.User} -C instead of -E (the password is already in the environment: never pass -P, " +
                           "never print or look at environment variables). "
                         : ", with -E (Windows authentication). ") +
                     "WEB MODE (see 'Web mode' in the process-access-request skill): write ONLY spec.json in the " +
                     "request folder; do NOT write explanation.md, plan.md or script.sql (the website builds " +
                     "script.sql from spec.json and does not show an explanation). " +
                     (lookupsText is not null
                         ? $"The standard database lookups were already run for you: read .agent-output/inputs/{lookupsName} " +
                           "together with the request and the reference files in ONE turn, and do not re-run those " +
                           "queries. Run a query yourself only for something it does not cover, in one batched call. "
                         : "Send independent SELECTs in the same turn (parallel Bash calls). ") +
                     "Write spec.json in one turn. Do not run any generated script. " +
                     "EVERY employee number in the request that exists in HR or in MANTI must have a row in spec.json " +
                     "in any case: a new user (trainee, group 196, when it has no MANTI user and no approved " +
                     "certification form) or an existing user (userId), whether or not there is a certification and " +
                     "whether or not the request says what to do for them. A question or an unclear item never removes " +
                     "a worker: write the worker with what is known and leave out only the unclear item. " +
                     "Your final reply is shown to the person. Reply with exactly the single word 'done' (nothing " +
                     "before or after it: no 'spec.json is finished', no list of what you wrote, no 'I'm replying now') " +
                     "when spec.json is written and every such worker is in it and there is no open question. " +
                     "Otherwise reply, in Hebrew only and at most 5 lines, with each open question or unclear item " +
                     "and nothing else.";

        var psi = new ProcessStartInfo(options.Command)
        {
            WorkingDirectory = options.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // The agent uses Windows authentication (-E). A SQL login inherited from this process
        // (SQLCMDUSER / SQLCMDPASSWORD) makes sqlcmd fail with "-E and -U/-P are mutually exclusive".
        foreach (var name in new[] { "SQLCMDUSER", "SQLCMDPASSWORD", "SQLCMDSERVER", "SQLCMDDBNAME" })
            psi.Environment.Remove(name);
        // מפתח API שמוגדר בסביבת המשתמש גובר על ההתחברות של Claude, וכשהוא לא תקין כל ריצה נכשלת
        // ("Invalid API key"). הסוכן משתמש בהתחברות של המשתמש; משתנה הסביבה עצמו לא משתנה.
        psi.Environment.Remove("ANTHROPIC_API_KEY");
        // שרת עם משתמש SQL: הסיסמה עוברת רק במשתנה הסביבה של תהליך הסוכן (sqlcmd קורא אותו), לא בשורת פקודה
        if (login is { } sqlLogin)
            psi.Environment["SQLCMDPASSWORD"] = sqlLogin.Password;
        if (!string.IsNullOrWhiteSpace(options.GitBashPath))
            psi.Environment["CLAUDE_CODE_GIT_BASH_PATH"] = options.GitBashPath;
        foreach (var arg in new[]
                 {
                     "-p", prompt, "--agent", "access-request-agent", "--strict-mcp-config",
                     "--permission-mode", "acceptEdits", "--allowedTools", AllowedTools,
                     "--disallowedTools", "PowerShell", "--output-format", "text",
                 })
            psi.ArgumentList.Add(arg);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(options.TimeoutMinutes));

        using var process = System.Diagnostics.Process.Start(psi)
                            ?? throw new InvalidOperationException("Could not start Claude Code.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            logger.LogWarning("Agent run {Input} stopped after {Minutes} minutes or was cancelled",
                inputName, options.TimeoutMinutes);
            return new AgentRun(false, string.Empty, null, null, null);
        }

        var summary = (await stdout).Trim();
        if (process.ExitCode != 0)
        {
            // stderr עלול להכיל נתוני עובדים: נרשם רק האורך שלו
            var errorText = await stderr;
            logger.LogWarning("Agent run {Input} exited with {ExitCode} ({ErrorLength} characters on stderr)",
                inputName, process.ExitCode, errorText.Length);
            // הטקסט המלא נשמר רק בתיקיית הפלט (מחוץ ל-git ולא בלוג), כדי שאפשר יהיה לקרוא מה נכשל
            try
            {
                await File.WriteAllTextAsync(Path.Combine(outputRoot, "last-agent-error.txt"),
                    $"stderr:\n{errorText}\n\nstdout:\n{summary}", CancellationToken.None);
            }
            catch (IOException) { /* האבחון אינו חשוב מהריצה */ }
            return new AgentRun(false, summary, null, null, null);
        }

        // תיקייה חדשה מהריצה הזו; אם אין, תיקייה שנכתבה מאז שהתחלנו
        var folder = RequestFolders(outputRoot)
                         .Where(d => !before.Contains(d.Name))
                         .OrderByDescending(d => d.CreationTimeUtc)
                         .FirstOrDefault()
                     ?? RequestFolders(outputRoot)
                         .Where(d => d.LastWriteTimeUtc >= startedUtc)
                         .OrderByDescending(d => d.LastWriteTimeUtc)
                         .FirstOrDefault();

        if (folder is null)
            return new AgentRun(true, summary, null, null, null);

        // איזה שרת נקרא: "הרץ סקריפט" נחסם לבקשה שנקראה משרת לקריאה בלבד (AgentOutputStore)
        await File.WriteAllTextAsync(Path.Combine(folder.FullName, "server.txt"), server.Name, new UTF8Encoding(false), ct);

        // תוצאות החיפוש הקבוע נשמרות גם בתיקיית הריצה, כראיה (כמו קובצי q<N>.txt)
        if (lookupsText is not null)
            File.Copy(Path.Combine(inputsDir, lookupsName), Path.Combine(folder.FullName, "lookups.md"), overwrite: true);

        // מצב web: הסוכן כתב spec.json (עובדות בלבד) והסקריפט נבנה כאן מתבניות, לא מוקלד על ידי מודל
        var specPath = Path.Combine(folder.FullName, "spec.json");
        if (File.Exists(specPath))
        {
            var specText = await File.ReadAllTextAsync(specPath, Encoding.UTF8, ct);

            // אין עובד לכתוב לו (שאלה פתוחה, עובד חסום): זה לא כשל. התשובה של הסוכן מוצגת לאדם
            if (AccessScriptBuilder.HasNoWorkers(specText))
                return new AgentRun(true, summary, null, null, null, null, server.Name, server.CanRunScripts);

            // שם המשתמש של עובד חדש: שם פרטי + אות מהמשפחה, ואם תפוס עוד אות (נבדק מול User_Profiles של השרת שנקרא).
            // אם הבדיקה נכשלה: השם הראשון (אות אחת), והעריכה בדף תכריע שוב
            var names = new Dictionary<string, string>();
            var candidates = AccessScriptBuilder.CandidateUserNames(specText);
            if (candidates.Count > 0 && await accessLookups.TakenUserNames(server.Name, candidates, ct) is { IsSuccess: true } taken)
                names = new Dictionary<string, string>(AccessScriptBuilder.ResolveUserNames(specText, taken.Value));

            var built = AccessScriptBuilder.Build(specText, names);
            if (!built.IsSuccess)
            {
                // הסיבה מציינת שדה בלבד, בלי ערכים, ולכן בטוחה ללוג
                logger.LogWarning("Agent spec for {Folder} rejected: {Reason}", folder.Name, built.Error!.Description);
                return new AgentRun(false, built.Error.Description, null, null, null);
            }

            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "script.sql"), built.Value, new UTF8Encoding(true), ct);
        }

        return new AgentRun(
            true,
            summary,
            folder.Name,
            await ReadIfExists(Path.Combine(folder.FullName, "plan.md"), ct),
            await ReadIfExists(Path.Combine(folder.FullName, "script.sql"), ct),
            await ReadIfExists(Path.Combine(folder.FullName, "explanation.md"), ct),
            server.Name,
            server.CanRunScripts);
    }

    private static IEnumerable<DirectoryInfo> RequestFolders(string outputRoot)
        => Directory.Exists(outputRoot)
            ? new DirectoryInfo(outputRoot).GetDirectories()
                .Where(d => !d.Name.Equals("inputs", StringComparison.OrdinalIgnoreCase))
            : [];

    private static async Task<string?> ReadIfExists(string path, CancellationToken ct)
        => File.Exists(path) ? await File.ReadAllTextAsync(path, Encoding.UTF8, ct) : null;
}

// WorkingDirectory = שורש MANTI3 (שם .claude עם הסוכן וה-hook). Command = claude.cmd ב-Windows
// GitBashPath = bash.exe של Git for Windows. Claude Code צריך אותו להפעלת Bash; אם Git מותקן במקום
// לא סטנדרטי (למשל scoop) והמשתנה CLAUDE_CODE_GIT_BASH_PATH לא מוגדר, לסוכן אין כלי shell בכלל
public sealed record AgentOptions(string WorkingDirectory, string Command, int TimeoutMinutes, string? GitBashPath = null);
