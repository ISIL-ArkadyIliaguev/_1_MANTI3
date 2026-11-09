using System.Globalization;
using System.Text;
using Manti3.Backend.Application.Features.AccessRequests.BuildScript;
using Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Manti3.Backend.Infrastructure.Agent;

// מריץ בשרת, לפני שהסוכן מתחיל, את שאילתות האימות הקבועות (קריאה בלבד, שרת מהרשימה המוגדרת בלבד) ומחזיר אותן כטקסט.
// הסוכן קורא קובץ אחד במקום להריץ 5-10 סבבי שאילתות (כל סבב הוא תור של מודל), והשיפוט נשאר אצלו.
// כל השאילתות קבועות; ערכים מהטקסט נכנסים רק כפרמטרים. כשל כלשהו מחזיר null והסוכן שואל בעצמו, כמו קודם.
// התוצאה כוללת נתוני עובדים ולכן נשמרת רק ב-.agent-output, ושום דבר ממנה לא נרשם ללוג
internal sealed class AgentLookups(AgentServers servers, ILogger<AgentLookups> logger)
{
    private const int MaxRows = 300;

    // מסכי ברירת מחדל למשתמש חדש (new-user-defaults.md): כשמוסיפים שם שורה, מוסיפים גם כאן
    private static readonly int[] DefaultScreenIds = [1712];

    public async Task<string?> Run(string requestText, AgentServer server, CancellationToken ct)
    {
        var numbers = LookupInput.EmployeeNumbers(requestText);
        if (numbers.Count == 0)
            return null;

        var codes = LookupInput.WorkCenterCodes(requestText);
        var exampleCandidates = LookupInput.ExampleNumbers(requestText);

        try
        {
            // רק שרת שברשימה המוגדרת (ההזדהות: Windows או משתמש SQL מ-user-secrets, ראו AgentServers)
            var connectionString = servers.ConnectionStringFor(server, "Manti3-AgentLookups", 10);
            if (connectionString is null)
                return null;

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            var text = new StringBuilder();
            text.AppendLine($"# Lookups run by the website on {server.Name} (read-only SELECTs, fixed queries)");
            text.AppendLine($"Employee numbers found in the text: {string.Join(", ", numbers)}");
            text.AppendLine($"Example-user candidates (numbers after \"דוגמא\"): {(exampleCandidates.Count == 0 ? "none found" : string.Join(", ", exampleCandidates))}");
            text.AppendLine($"Work-center codes looked up (each with its reversed form): {(codes.Count == 0 ? "none found" : string.Join(", ", codes))}");
            text.AppendLine("These are candidates taken from the text; you decide which are real. A number or code with no row below does not exist.");
            text.AppendLine("Not covered here (query it yourself only if needed): numbers that appear only inside a PDF, and the certified-worker group lookup.");

            var inUsers = Params("n", numbers);
            await Section(connection, text, "users (User_Profiles by UserCode or UserName; a UserName match with another UserCode means the name is taken)",
                $"""
                SELECT TOP {MaxRows} u.UserID, u.UserCode, u.UserName, u.UserFirstName, u.UserLastName, u.DepartmentId,
                       u.UserGroupId, u.UsersStatus, f.BranchId
                FROM dbo.User_Profiles u
                LEFT JOIN dbo.Departments d ON d.DepartmentID = u.DepartmentId
                LEFT JOIN dbo.Factories f ON f.FactoryID = d.FactoryID
                WHERE u.UserCode IN ({inUsers.Names}) OR u.UserName IN ({inUsers.Names})
                """, inUsers.Values, ct);

            var hr = await Section(connection, text, "hr (PRS_WORKERS_V6 via SQLCLN1; MantiDepartmentId is the mapped MANTI department, BranchId its branch)",
                $"""
                SELECT TOP {MaxRows} w.Employeeno, w.firstname, w.surname, w.InternalPhone, w.EmailAdd, w.DepId, w.FacId,
                       d.DepartmentId AS MantiDepartmentId, f.BranchId, w.FirstNameE, w.LastNameE
                FROM SQLCLN1.prs_IscarData.dbo.PRS_WORKERS_V6 w
                LEFT JOIN dbo.Factories f ON w.FacId LIKE f.FactoryCode
                LEFT JOIN dbo.Departments d ON w.DepId LIKE d.DepartmentCode AND d.FactoryId = f.FactoryId AND d.DepartmentDisplay = 1
                WHERE CAST(w.Employeeno AS varchar(20)) IN ({inUsers.Names})
                """, inUsers.Values, ct);

            await UserNameSection(connection, text, hr, ct);

            if (codes.Count > 0)
            {
                var inCodes = Params("c", codes);
                await Section(connection, text, "workCenters (Work_Centers; WorkCtrDisplay = 1 means the MANTI 2 screen lists it)",
                    $"""
                    SELECT TOP {MaxRows} WorkCtrId, WorkCtrCode, WorkCtrName, WorkCtrDisplay, DepartmentID
                    FROM dbo.Work_Centers WHERE WorkCtrCode IN ({inCodes.Names})
                    """, inCodes.Values, ct);
            }

            // המסכים של משתמש הדוגמה: מי שנמצא אחרי "דוגמא", ואם אין כזה כל מי שנמצא (RowsForObj > 1 = כפילות, שאלה)
            var screenCodes = exampleCandidates.Count > 0 ? exampleCandidates : numbers;
            var inScreens = Params("s", screenCodes);
            await Section(connection, text,
                $"exampleScreens (screens of {(exampleCandidates.Count > 0 ? "the example candidates" : "every user found, since no example was named")}; RowsForObj > 1 is a duplicate)",
                $"""
                SELECT TOP {MaxRows} u.UserCode, a.ObjId, ah.AppTbToolTip, a.ControlId,
                       COUNT(*) OVER (PARTITION BY a.UserId, a.ObjId) AS RowsForObj
                FROM dbo.Authorizations a
                JOIN dbo.User_Profiles u ON u.UserId = a.UserId
                LEFT JOIN dbo.Appl_Header ah ON ah.ObjID = a.ObjId
                WHERE u.UserCode IN ({inScreens.Names})
                ORDER BY u.UserCode, a.ObjId
                """, inScreens.Values, ct);

            // ברירות מחדל לעובד חדש: לכל מחלקת MANTI של שורת HR שאין לעובדה משתמש
            var departments = hr.Where(r => r.Count > 7 && r[7] is not null).Select(r => r[7]!).Distinct().Take(10).ToList();
            foreach (var department in departments)
                foreach (var screen in DefaultScreenIds)
                {
                    var args = new[]
                    {
                        new SqlParameter("@dept", int.Parse(department, CultureInfo.InvariantCulture)),
                        new SqlParameter("@obj", screen),
                    };
                    await Section(connection, text, $"defaultScreen {screen} in department {department} (Peers = active users; WithScreen = those that have it)",
                        """
                        SELECT COUNT(DISTINCT u.UserId) AS Peers, COUNT(DISTINCT a.UserId) AS WithScreen
                        FROM dbo.User_Profiles u
                        LEFT JOIN dbo.Authorizations a ON a.UserId = u.UserId AND a.ObjId = @obj
                        WHERE u.DepartmentId = @dept AND u.UsersStatus <> '1'
                        """, args, ct);

                    var bitsArgs = new[]
                    {
                        new SqlParameter("@dept", int.Parse(department, CultureInfo.InvariantCulture)),
                        new SqlParameter("@obj", screen),
                    };
                    await Section(connection, text, $"defaultScreen {screen} trainee bits in department {department} (most common ControlId among active trainees, group 196; empty = use the fallback bits)",
                        """
                        SELECT TOP 1 a.ControlId, COUNT(*) AS Users
                        FROM dbo.Authorizations a JOIN dbo.User_Profiles u ON u.UserId = a.UserId
                        WHERE a.ObjId = @obj AND u.DepartmentId = @dept AND u.UsersStatus <> '1' AND u.UserGroupId = 196
                        GROUP BY a.ControlId ORDER BY COUNT(*) DESC
                        """, bitsArgs, ct);
                }

            return text.ToString();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException or FormatException)
        {
            // הסוכן ישאל בעצמו. נרשם סוג השגיאה בלבד: ההודעה עלולה להכיל נתוני עובדים
            logger.LogWarning("Prefetching lookups failed ({Type}); the agent will query itself", ex.GetType().Name);
            return null;
        }
    }

    // שם משתמש לעובד חדש: האתר מחשב אותו (EnglishNames, ראו new-user-naming.md): שם פרטי באנגלית + אות ראשונה של שם
    // המשפחה, ואם השם תפוס ב-User_Profiles עוד אות, עד ששם פנוי. הסוכן לא בוחר שם ולא שואל על שם תפוס. האנגלית באה
    // מ-HR (FirstNameE / LastNameE); חסרה או לא לטינית: הסוכן מתרגם את השם העברי
    private static Task UserNameSection(
        SqlConnection connection, StringBuilder text, List<List<string?>> hr, CancellationToken ct)
    {
        const int firstIndex = 9, lastIndex = 10;

        text.AppendLine().AppendLine("## englishNames (the website builds the user name from these: English first name + first letter of the English last name, one more letter while that name is taken; you do not choose or check user names; password stays the employee number; matters only for a worker with no MANTI user)");
        text.AppendLine("employee | engFirstName | engLastName");
        foreach (var row in hr.Where(r => r.Count > lastIndex && r[0] is not null))
        {
            if (EnglishNames.UserName(row[firstIndex], row[lastIndex]) is null)
            {
                text.AppendLine($"{row[0]} | HR has no usable English name | | translate the Hebrew first and last name to English yourself (engFirstName / engLastName in spec.json)");
                continue;
            }

            text.AppendLine($"{row[0]} | {EnglishNames.ForColumn(row[firstIndex])} | {EnglishNames.ForColumn(row[lastIndex])}");
        }

        if (hr.Count == 0)
            text.AppendLine("(no HR rows)");

        return Task.CompletedTask;
    }
    // מריץ שאילתה אחת, כותב אותה כטבלה בטקסט ומחזיר את השורות (לשימוש בשאילתה הבאה)
    private static async Task<List<List<string?>>> Section(
        SqlConnection connection, StringBuilder text, string title, string sql, IEnumerable<SqlParameter> parameters,
        CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        foreach (var p in parameters)
            command.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));

        var rows = new List<List<string?>>();
        await using var reader = await command.ExecuteReaderAsync(ct);

        text.AppendLine().AppendLine($"## {title}");
        text.AppendLine(string.Join(" | ", Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)));
        while (await reader.ReadAsync(ct))
        {
            var row = new List<string?>();
            for (var i = 0; i < reader.FieldCount; i++)
                row.Add(reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)?.Trim());
            rows.Add(row);
            text.AppendLine(string.Join(" | ", row.Select(c => c ?? "NULL")));
        }

        if (rows.Count == 0)
            text.AppendLine("(no rows)");
        return rows;
    }

    private static ParamList Params(string prefix, IReadOnlyList<string> values)
    {
        var parameters = values.Select((v, i) => new SqlParameter($"@{prefix}{i}", v)).ToList();
        return new ParamList(string.Join(", ", parameters.Select(p => p.ParameterName)), parameters);
    }

    private sealed record ParamList(string Names, List<SqlParameter> Values);
}
