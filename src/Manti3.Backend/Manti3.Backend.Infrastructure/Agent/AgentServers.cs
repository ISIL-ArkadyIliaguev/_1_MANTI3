using Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Manti3.Backend.Infrastructure.Agent;

// רשימת השרתים שהסוכן רשאי לקרוא מהם, מתוך Agent:Servers בהגדרות (Name + DataSource, ואופציונלי Auth = "Sql").
// הראשון הוא ברירת המחדל. בלי הגדרה: רק השרת של ScriptRunner:AllowedServer (SQL25DEV). רק לשרת הזה מותר גם "הרץ סקריפט".
// שרת עם Auth = "Sql" נכנס עם משתמש וסיסמה מ-Agent:ServerLogins:<Name>:User / :Password, שמגיעים רק מ-user-secrets
// או ממשתני סביבה (Agent__ServerLogins__<Name>__User). לעולם לא מקובץ ב-repo, מהדפדפן או מהצ'אט
internal sealed class AgentServers : IAgentServers
{
    private readonly Dictionary<string, SqlLogin> _logins = new(StringComparer.OrdinalIgnoreCase);

    private readonly ScriptRunnerOptions _runner;

    public AgentServers(IConfiguration config, ScriptRunnerOptions runner)
    {
        _runner = runner;
        var list = new List<AgentServer>();
        foreach (var section in config.GetSection("Agent:Servers").GetChildren())
        {
            var name = section["Name"]?.Trim();
            var dataSource = section["DataSource"]?.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dataSource))
                continue;

            var sqlLogin = string.Equals(section["Auth"], "Sql", StringComparison.OrdinalIgnoreCase);
            var ready = true;
            if (sqlLogin)
            {
                var user = config[$"Agent:ServerLogins:{name}:User"];
                var password = config[$"Agent:ServerLogins:{name}:Password"];
                ready = !string.IsNullOrWhiteSpace(user) && !string.IsNullOrEmpty(password);
                if (ready)
                    _logins[name] = new SqlLogin(user!, password!);
            }

            // כתיבה (כפתור "אישור ושמירה"): שרת Windows רק אם הוא AllowedServer; שרת עם משתמש SQL רק אם הוגדר
            // במפורש AllowWrite = true (אושר ב-2026-11-08 עבור SQL2025CLN2). שרת אחר: קריאה בלבד
            var canRunScripts = sqlLogin
                ? string.Equals(section["AllowWrite"], "true", StringComparison.OrdinalIgnoreCase)
                : HostOf(dataSource).Equals(runner.AllowedServer, StringComparison.OrdinalIgnoreCase);

            list.Add(new AgentServer(name, dataSource, canRunScripts, sqlLogin, ready));
        }

        if (list.Count == 0)
            list.Add(new AgentServer(runner.AllowedServer, runner.AllowedServer, true));

        All = list;
        Default = list[0];
    }

    public IReadOnlyList<AgentServer> All { get; }

    public AgentServer Default { get; }

    public AgentServer? Find(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? Default
            : All.FirstOrDefault(s => s.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    // רק לשימוש הפנימי של ה-Infrastructure (חיבור מההדרים בשרת, וסביבת ה-sqlcmd של הסוכן)
    public (string User, string Password)? LoginFor(AgentServer server)
        => _logins.TryGetValue(server.Name, out var login) ? (login.User, login.Password) : null;

    // מחרוזת חיבור לקריאה בלבד לשרת מהרשימה: Windows, או משתמש SQL מ-user-secrets/סביבה. null אם השרת אינו ברשימה,
    // ההתחברות שלו לא הוגדרה, או שאין הזדהות מתאימה. לשימוש הסוכן (AgentLookups) ורשימות הבחירה (AccessLookups)
    public string? ConnectionStringFor(AgentServer server, string applicationName, int timeoutSeconds)
        => Build(server, applicationName, timeoutSeconds, ApplicationIntent.ReadOnly);

    // מחרוזת חיבור לכתיבה, רק לשרת שמותר להריץ עליו סקריפט (CanRunScripts). לשימוש SqlScriptExecutor בלבד
    public string? ScriptConnectionStringFor(AgentServer server, string applicationName, int timeoutSeconds)
        => server.CanRunScripts ? Build(server, applicationName, timeoutSeconds, ApplicationIntent.ReadWrite) : null;

    private string? Build(AgentServer server, string applicationName, int timeoutSeconds, ApplicationIntent intent)
    {
        if (Find(server.Name) != server || !server.Ready)
            return null;

        var builder = new SqlConnectionStringBuilder(_runner.ConnectionString) { DataSource = server.DataSource };
        if (server.UsesSqlLogin)
        {
            if (LoginFor(server) is not { } login)
                return null;
            builder.IntegratedSecurity = false;
            builder.UserID = login.User;
            builder.Password = login.Password;
        }
        else if (!builder.IntegratedSecurity)
        {
            return null;
        }

        builder.ApplicationIntent = intent;
        builder.ApplicationName = applicationName;
        builder.ConnectTimeout = timeoutSeconds;
        return builder.ConnectionString;
    }

    // "tcp:HOST,2433" / "HOST\INSTANCE" -> "HOST"
    public static string HostOf(string dataSource)
    {
        var name = dataSource.Trim();
        var colon = name.IndexOf(':');
        if (colon >= 0 && name[..colon].Length <= 3)
            name = name[(colon + 1)..];
        return name.Split(',', '\\')[0].Trim();
    }

    private sealed record SqlLogin(string User, string Password)
    {
        // שלא יודפס בטעות (לוג, חריגה)
        public override string ToString() => "SqlLogin";
    }
}
