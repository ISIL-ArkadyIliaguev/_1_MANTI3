using System.Globalization;
using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Application.Features.AccessRequests.RunScript;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Manti3.Backend.Infrastructure.Agent;

// מריץ את גוף הסקריפט בעסקה אחת ומבטל (הרצה לניסיון) או שומר. זה המקום היחיד ב-MANTI3 שכותב ל-DB.
// הגנות: רק שרת מורשה לכתיבה (SQL25DEV עם Windows; SQL2025CLN2 עם AllowWrite, אושר 2026-11-08), והסקריפט כבר עבר את AccessScriptGuard.
// שום תוכן (שורות, הודעות שגיאה מלאות) לא נרשם ללוג: הם עלולים להכיל נתוני עובדים
internal sealed class SqlScriptExecutor(AgentServers servers, ILogger<SqlScriptExecutor> logger)
    : IScriptExecutor
{
    private const int MaxRowsPerResultSet = 200;
    private const int TimeoutSeconds = 120;

    public async Task<Result<ScriptRunResult>> Execute(string body, bool commit, string? serverName, CancellationToken ct)
    {
        // רק שרת מהרשימה שמוגדר לו כתיבה (CanRunScripts): SQL25DEV עם Windows, או שרת עם AllowWrite והתחברות SQL
        var server = servers.Find(serverName);
        if (server is null || !server.CanRunScripts)
            return Failure("AccessRequest.ServerNotAllowed", "Scripts may not run on this server.");

        var connectionString = servers.ScriptConnectionStringFor(server, "Manti3-ScriptRunner", 30);
        if (connectionString is null)
            return Failure("AccessRequest.NoConnection", "The database connection is not configured.");

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

            await using var command = new SqlCommand("SET XACT_ABORT ON;\n" + body, connection, transaction)
            {
                CommandTimeout = TimeoutSeconds,
            };

            var resultSets = new List<ScriptResultSet>();
            int affected;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                do
                {
                    if (reader.FieldCount > 0)
                        resultSets.Add(await ReadResultSet(reader, ct));
                } while (await reader.NextResultAsync(ct));

                affected = Math.Max(reader.RecordsAffected, 0);
            }

            if (commit)
                await transaction.CommitAsync(ct);
            else
                await transaction.RollbackAsync(ct);

            return Result<ScriptRunResult>.Success(new ScriptRunResult(commit, affected, resultSets));
        }
        catch (SqlException ex)
        {
            // מספר השגיאה בלבד: ההודעה עלולה להכיל ערכים של עובדים. העסקה לא נשמרה (Dispose מבטל אותה)
            logger.LogWarning("Script run failed with SQL error {Number}", ex.Number);
            return Failure("AccessRequest.ScriptFailed", $"SQL Server error {ex.Number}: {ex.Message}");
        }
    }

    private static async Task<ScriptResultSet> ReadResultSet(SqlDataReader reader, CancellationToken ct)
    {
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<IReadOnlyList<string?>>();
        while (rows.Count < MaxRowsPerResultSet && await reader.ReadAsync(ct))
        {
            var row = new string?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
                row[i] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
            rows.Add(row);
        }

        return new ScriptResultSet(columns, rows);
    }

    private static Result<ScriptRunResult> Failure(string code, string description)
        => Result<ScriptRunResult>.Failure(Error.Failure(code, description));
}

public sealed record ScriptRunnerOptions(string ConnectionString, string AllowedServer);
