using Manti3.Backend.Application.Features.AccessRequests.EditSpec;
using Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;
using Manti3.Backend.Application.Features.AccessRequests.RunScript;
using Manti3.Backend.Application.Features.Items.GetItems;
using Manti3.Backend.Infrastructure.Agent;
using Manti3.Backend.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Manti3.Backend.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(
            BuildConnectionString(config),
            sql => sql.EnableRetryOnFailure()));

        services.AddScoped<IItemsReader, ItemsReader>();

        services.AddSingleton(new AgentOptions(
            config["Agent:WorkingDirectory"] ?? Directory.GetCurrentDirectory(),
            config["Agent:Command"] ?? "claude.cmd",
            int.TryParse(config["Agent:TimeoutMinutes"], out var minutes) ? minutes : 10,
            config["Agent:GitBashPath"]));
        services.AddSingleton<IAccessRequestAgent, ClaudeCliAgent>();
        services.AddSingleton<IMailFileReader, MailFileReader>();

        // הכתיבה היחידה ל-DB מ-MANTI3: כפתור "הרץ סקריפט". רק SQL25DEV (נאכף גם ב-SqlScriptExecutor)
        services.AddSingleton(new ScriptRunnerOptions(
            BuildConnectionString(config),
            config["ScriptRunner:AllowedServer"] ?? "SQL25DEV"));
        services.AddSingleton<AgentServers>();
        services.AddSingleton<IAgentServers>(sp => sp.GetRequiredService<AgentServers>());
        services.AddSingleton<AgentLookups>();
        services.AddSingleton<IAccessLookups, AccessLookups>();
        services.AddSingleton<IScriptExecutor, SqlScriptExecutor>();
        services.AddSingleton<IAgentOutput, AgentOutputStore>();
        services.AddSingleton<IScriptRunLedger, ScriptRunLedger>();

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("sqlserver", tags: ["ready", "startup"]);

        return services;
    }

    // שרת ו-DB מתוך ConnectionStrings:Default. משתמש וסיסמה (Db:User, Db:Password) מגיעים
    // רק מ-user-secrets / משתני סביבה ולא נשמרים בקוד.
    private static string BuildConnectionString(IConfiguration config)
    {
        var builder = new SqlConnectionStringBuilder(config.GetConnectionString("Default"));

        if (!string.IsNullOrEmpty(config["Db:User"]))
        {
            builder.UserID = config["Db:User"]!;
            builder.Password = config["Db:Password"] ?? string.Empty;
        }

        return builder.ConnectionString;
    }
}
