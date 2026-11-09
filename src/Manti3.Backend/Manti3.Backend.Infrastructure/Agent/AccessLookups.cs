using System.Collections.Concurrent;
using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Application.Features.AccessRequests.EditSpec;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Manti3.Backend.Infrastructure.Agent;

// הרשימות לתפריטי הבחירה בטבלת העריכה: קבוצות, מחלקות ומרכזי עבודה. SELECT קבועים בלבד, מהשרת שנבחר
// (כמו הסוכן), ונשמרים בזיכרון כמה דקות. אין כאן נתוני עובדים
internal sealed class AccessLookups(AgentServers servers, ILogger<AccessLookups> logger) : IAccessLookups
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, (DateTime At, AccessLookupData Data)> _cache = new();

    public async Task<Result<AccessLookupData>> Get(string? serverName, CancellationToken ct)
    {
        var server = servers.Find(serverName);
        if (server is null || !server.Ready)
            return Result<AccessLookupData>.Failure(Error.Validation(
                "AccessRequest.UnknownServer", "The server is not available: it is not in the list, or its login is not configured."));

        if (_cache.TryGetValue(server.Name, out var cached) && DateTime.UtcNow - cached.At < KeepFor)
            return Result<AccessLookupData>.Success(cached.Data);

        var connectionString = servers.ConnectionStringFor(server, "Manti3-AccessLookups", 10);
        if (connectionString is null)
            return Result<AccessLookupData>.Failure(Error.Failure(
                "AccessRequest.LookupsUnavailable", "Could not connect to the server for the lists."));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            var groups = await Read(connection, "SELECT UserGroupId, UserGroupName FROM dbo.User_Groups ORDER BY UserGroupName",
                r => new LookupItem(r.GetInt32(0), r.IsDBNull(1) ? string.Empty : r.GetString(1).Trim()), ct);

            var departments = await Read(connection,
                "SELECT DepartmentId, DepartmentName, DepartmentCode FROM dbo.Departments WHERE DepartmentDisplay = 1 ORDER BY DepartmentName",
                r => new DepartmentItem(r.GetInt32(0), r.IsDBNull(1) ? string.Empty : r.GetString(1).Trim(),
                    r.IsDBNull(2) ? null : r.GetString(2).Trim()), ct);

            var workCenters = await Read(connection,
                "SELECT WorkCtrId, WorkCtrCode, WorkCtrName, DepartmentId, WorkCtrDisplay FROM dbo.Work_Centers ORDER BY WorkCtrCode",
                r => new WorkCenterItem(r.GetInt32(0), r.IsDBNull(1) ? string.Empty : r.GetString(1).Trim(),
                    r.IsDBNull(2) ? string.Empty : r.GetString(2).Trim(), r.IsDBNull(3) ? null : r.GetInt32(3),
                    !r.IsDBNull(4) && r.GetBoolean(4)), ct);

            var applications = await Read(connection,
                "SELECT ObjId, ObjName, ObjCaption FROM dbo.AppsAuthorizations_V ORDER BY ObjCaption",
                r => new ApplicationItem(r.GetInt32(0), r.IsDBNull(1) ? string.Empty : r.GetString(1).Trim(),
                    r.IsDBNull(2) ? string.Empty : r.GetString(2).Trim()), ct);

            var data = new AccessLookupData(groups, departments, workCenters, applications);
            _cache[server.Name] = (DateTime.UtcNow, data);
            return Result<AccessLookupData>.Success(data);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            logger.LogWarning("Reading the lookup lists failed ({Type})", ex.GetType().Name);
            return Result<AccessLookupData>.Failure(Error.Failure(
                "AccessRequest.LookupsUnavailable", "Could not read the lists (groups, departments, work centers) from the server."));
        }
    }

    // שמות המשתמש שכבר קיימים (בדיקה טרייה, בלי מטמון). פרמטרים בלבד: השמות הוקלדו על ידי אדם
    public async Task<Result<IReadOnlySet<string>>> TakenUserNames(
        string? serverName, IReadOnlyCollection<string> names, CancellationToken ct)
    {
        var server = servers.Find(serverName);
        var connectionString = server is { Ready: true } ? servers.ConnectionStringFor(server, "Manti3-AccessLookups", 10) : null;
        if (connectionString is null)
            return Result<IReadOnlySet<string>>.Failure(Error.Validation(
                "AccessRequest.UnknownServer", "The server is not available: it is not in the list, or its login is not configured."));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            var list = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            await using var command = new SqlCommand { Connection = connection, CommandTimeout = 30 };
            var parameters = list.Select((n, i) => $"@u{i}").ToList();
            for (var i = 0; i < list.Count; i++)
                command.Parameters.Add(new SqlParameter($"@u{i}", System.Data.SqlDbType.VarChar, 50) { Value = list[i] });
            command.CommandText = $"SELECT UserName FROM dbo.User_Profiles WHERE UserName IN ({string.Join(", ", parameters)})";

            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                taken.Add(reader.GetString(0).Trim());
            return Result<IReadOnlySet<string>>.Success(taken);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            logger.LogWarning("Checking the user names failed ({Type})", ex.GetType().Name);
            return Result<IReadOnlySet<string>>.Failure(Error.Failure(
                "AccessRequest.LookupsUnavailable", "Could not check the user names on the server."));
        }
    }

    // היישומים שכבר יש למשתמשים (SELECT עם פרמטרים, בלי מטמון). לתצוגה בלבד בתפריט היישומים של עובד קיים
    public async Task<Result<IReadOnlyDictionary<long, IReadOnlyList<int>>>> ExistingApplications(
        string? serverName, IReadOnlyCollection<long> userIds, CancellationToken ct)
    {
        var empty = new Dictionary<long, IReadOnlyList<int>>();
        if (userIds.Count == 0)
            return Result<IReadOnlyDictionary<long, IReadOnlyList<int>>>.Success(empty);

        var server = servers.Find(serverName);
        var connectionString = server is { Ready: true } ? servers.ConnectionStringFor(server, "Manti3-AccessLookups", 10) : null;
        if (connectionString is null)
            return Result<IReadOnlyDictionary<long, IReadOnlyList<int>>>.Failure(Error.Validation(
                "AccessRequest.UnknownServer", "The server is not available: it is not in the list, or its login is not configured."));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            var ids = userIds.Distinct().ToList();
            await using var command = new SqlCommand { Connection = connection, CommandTimeout = 30 };
            for (var i = 0; i < ids.Count; i++)
                command.Parameters.Add(new SqlParameter($"@u{i}", System.Data.SqlDbType.Int) { Value = (int)ids[i] });
            command.CommandText = "SELECT UserId, ObjId FROM dbo.Authorizations WHERE UserId IN ("
                                  + string.Join(", ", ids.Select((_, i) => $"@u{i}")) + ")";

            var found = new Dictionary<long, List<int>>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var user = reader.GetInt32(0);
                if (!found.TryGetValue(user, out var list))
                    found[user] = list = [];
                list.Add(reader.GetInt32(1));
            }

            return Result<IReadOnlyDictionary<long, IReadOnlyList<int>>>.Success(
                found.ToDictionary(p => p.Key, p => (IReadOnlyList<int>)p.Value));
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            logger.LogWarning("Reading the existing applications failed ({Type})", ex.GetType().Name);
            return Result<IReadOnlyDictionary<long, IReadOnlyList<int>>>.Failure(Error.Failure(
                "AccessRequest.LookupsUnavailable", "Could not read the existing applications from the server."));
        }
    }

    private static async Task<List<T>> Read<T>(SqlConnection connection, string sql, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct))
            rows.Add(map(reader));
        return rows;
    }
}
