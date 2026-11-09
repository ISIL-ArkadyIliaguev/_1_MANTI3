using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Manti3.Backend.Application.Abstractions;

namespace Manti3.Backend.Application.Features.AccessRequests.BuildScript;

// בונה את script.sql מ-spec.json שהסוכן כותב. הסוכן מבצע את השיפוט (מי העובדים, מה קיים, מה חסר) וכותב עובדות
// בלבד; את ה-SQL כותב הקוד, מאותן תבניות של script-templates.md. כך הסקריפט מוכן מיד (במקום דקה של הקלדת
// ה-SQL על ידי מודל), וה-SQL שיכול לרוץ לא נכתב על ידי מודל שקרא טקסט של מייל.
// כל ערך עובר ולידציה ומחרוזות עוברות escape. הודעות השגיאה מציינות שדה בלבד, לעולם לא את הערך (נתוני עובדים)
public static partial class AccessScriptBuilder
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // סרגל הכלים שכל משתמש חדש מקבל (2026-11-05): "Lobby" עם היישום 742. לשינוי: כאן וב-new-user-defaults.md
    private const int LobbyToolbarCode = 1;
    private const string LobbyText = "Lobby";
    private const int LobbyAppId = 742;

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // לעריכה בדף: קריאת ה-spec השמור וכתיבתו מחדש (אחרי שהאדם שינה שדות). null אם אינו JSON תקין
    internal static ScriptSpec? Parse(string specJson)
    {
        try
        {
            return JsonSerializer.Deserialize<ScriptSpec>(specJson, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string ToJson(ScriptSpec spec) => JsonSerializer.Serialize(spec, JsonOut);

    // spec.json ללא עובדים: הסוכן החליט שאין מה לכתוב (שאלה פתוחה, עובד חסום). זה לא כשל, והתשובה שלו היא ההסבר
    public static bool HasNoWorkers(string specJson) => Parse(specJson) is not { Workers.Count: > 0 };

    // עובדים חדשים שנשמרים וששם המשתמש שלהם מחושב (לא הוקלד): הם הצריכים הכרעה מול ה-DB
    private static IEnumerable<WorkerSpec> ComputedNameWorkers(ScriptSpec spec)
        => spec.Workers?.Where(w => w.NewUser is { UserName: null } && !w.Skip && !string.IsNullOrEmpty(w.UserCode)) ?? [];

    // כל השמות שצריך לבדוק מול User_Profiles כדי להכריע שם משתמש לכל עובד חדש (ראו EnglishNames.Resolve)
    public static IReadOnlyList<string> CandidateUserNames(string specJson)
        => Parse(specJson) is { } spec
            ? ComputedNameWorkers(spec)
                .SelectMany(w => EnglishNames.UserNameCandidates(w.NewUser!.EngFirstName, w.NewUser.EngLastName))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    // לכל עובד חדש עם שם מחושב: השם הראשון הפנוי (אות אחת מהמשפחה, ואם תפוס עוד אות, וכן הלאה). `taken` = השמות התפוסים ב-DB.
    // שם של עובד קודם בבקשה (וגם שם שהוקלד) נחשב תפוס, כדי ששני עובדים באותה בקשה לא יקבלו את אותו שם. אין שם פנוי: העובד חסר במפה
    public static IReadOnlyDictionary<string, string> ResolveUserNames(string specJson, IReadOnlySet<string> taken)
    {
        var result = new Dictionary<string, string>();
        if (Parse(specJson) is not { } spec)
            return result;

        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        foreach (var typed in spec.Workers!.Where(w => w.NewUser is { UserName: not null } && !w.Skip))
            used.Add(typed.NewUser!.UserName!);

        foreach (var w in ComputedNameWorkers(spec))
            if (EnglishNames.Resolve(w.NewUser!.EngFirstName, w.NewUser.EngLastName, used) is { } name)
            {
                result[w.UserCode!] = name;
                used.Add(name);
            }

        return result;
    }

    // resolvedNames: ראו ResolveUserNames. בלעדיהם שם מחושב הוא שם פרטי + אות אחת (ללא בדיקה מול ה-DB)
    public static Result<string> Build(string specJson, IReadOnlyDictionary<string, string>? resolvedNames = null)
    {
        ScriptSpec? spec;
        try
        {
            spec = JsonSerializer.Deserialize<ScriptSpec>(specJson, Json);
        }
        catch (JsonException)
        {
            return Fail("spec is not valid JSON.");
        }

        if (spec?.Workers is not { Count: > 0 })
            return Fail("workers is missing or empty.");

        var invalid = Validate(spec);
        if (invalid is not null)
            return Fail(invalid);

        return Result<string>.Success(Render(spec, resolvedNames));
    }

    // ---------- validation ----------

    private static string? Validate(ScriptSpec spec)
    {
        if (spec.Example is { } ex)
        {
            if (!IsCode(ex.UserCode)) return "example.userCode is not valid.";
            if (ex.UserId <= 0) return "example.userId is not valid.";
            if (ex.Screens is null || ex.Screens.Any(s => s.ObjId <= 0)) return "example.screens is not valid.";
        }

        for (var i = 0; i < spec.Workers!.Count; i++)
        {
            var w = spec.Workers[i];
            var at = $"workers[{i}]";

            if (!IsCode(w.UserCode)) return $"{at}.userCode is not valid.";
            if ((w.UserId is null) == (w.NewUser is null)) return $"{at} needs exactly one of userId and newUser.";
            if (w.UserId is <= 0) return $"{at}.userId is not valid.";

            // עובד שהאדם ביטל את הסימון שלו: לא נכתב, ולכן אין צורך בשאר השדות שלו
            if (w.Skip)
                continue;

            // עובד קיים (2026-11-08): רק הוספה. מרכזי עבודה ויישומים (מסכים) שלא היו לו; אין הסרה ואין שינוי פרטים
            if (w.UserId is not null)
            {
                if (w.WorkCenters?.Any(wc => wc.Id <= 0) == true) return $"{at}.workCenters is not valid.";
                if (w.AddScreens?.Any(s => s.ObjId <= 0 || !ControlBits().IsMatch(s.ControlId ?? string.Empty)) == true)
                    return $"{at}.addScreens is not valid.";
                if (w.CopyScreens && spec.Example is not { Screens.Count: > 0 })
                    return $"{at}.copyScreens needs example with at least one screen.";
                if ((w.CopyScreens || w.AddScreens is { Count: > 0 }) && w.BranchId <= 0)
                    return $"{at}.branchId is not valid.";
                continue;
            }

            if (w.BranchId <= 0) return $"{at}.branchId is not valid.";

            if (w.NewUser is { } n)
            {
                if (Clean(n.FirstName) is not { Length: > 0 and <= 15 }) return $"{at}.newUser.firstName must be 1-15 characters.";
                if (Clean(n.LastName) is not { Length: > 0 and <= 15 }) return $"{at}.newUser.lastName must be 1-15 characters.";
                if (EnglishNames.ForColumn(n.EngFirstName) is null) return $"{at}.newUser.engFirstName must be an English name of 1-15 letters.";
                if (EnglishNames.ForColumn(n.EngLastName) is null) return $"{at}.newUser.engLastName must be an English name of 1-15 letters.";
                if (n.UserName is not null && !UserNameFormat().IsMatch(n.UserName))
                    return $"{at}.newUser.userName must be 1-50 letters, digits, dot, dash or underscore.";
                if (n.Password is not null && !PasswordFormat().IsMatch(n.Password))
                    return $"{at}.newUser.password must be 1-10 characters with no spaces.";
                if ((n.Phone ?? string.Empty).Length > 8) return $"{at}.newUser.phone is longer than 8.";
                if ((n.Email ?? string.Empty).Length > 50) return $"{at}.newUser.email is longer than 50.";
                if (n.DepartmentId <= 0) return $"{at}.newUser.departmentId is not valid.";
                if (n.GroupId <= 0) return $"{at}.newUser.groupId is not valid.";
                if (n.CertificationDate is not null && !Date().IsMatch(n.CertificationDate))
                    return $"{at}.newUser.certificationDate must be yyyymmdd.";
            }

            if (w.CertifyExisting is { } c)
            {
                if (w.UserId is null) return $"{at}.certifyExisting is only for an existing user.";
                if (c.GroupId <= 0) return $"{at}.certifyExisting.groupId is not valid.";
                if (!Date().IsMatch(c.Date ?? string.Empty)) return $"{at}.certifyExisting.date must be yyyymmdd.";
            }

            if (w.WorkCenters?.Any(wc => wc.Id <= 0) == true) return $"{at}.workCenters is not valid.";

            if (w.CopyScreens && spec.Example is not { Screens.Count: > 0 })
                return $"{at}.copyScreens needs example with at least one screen.";

            foreach (var d in w.DefaultScreens ?? [])
                if (d.ObjId <= 0 || !ControlBits().IsMatch(d.ControlId ?? string.Empty))
                    return $"{at}.defaultScreens is not valid.";

            foreach (var d in w.AddScreens ?? [])
                if (d.ObjId <= 0 || !ControlBits().IsMatch(d.ControlId ?? string.Empty))
                    return $"{at}.addScreens is not valid.";
        }

        return null;
    }

    // עובד קיים שיש מה להוסיף לו (מרכז עבודה, העתקת מסכים מהדוגמה או יישום). בלי זה: לא נוגעים בו
    internal static bool HasExistingWork(WorkerSpec w)
        => w.UserId is not null && w.NewUser is null && !w.Skip
           && (w.WorkCenters is { Count: > 0 } || w.CopyScreens || w.AddScreens is { Count: > 0 });

    private static bool IsWritten(WorkerSpec w) => !w.Skip && (w.NewUser is not null || HasExistingWork(w));

    // ---------- rendering ----------

    // עובד חדש: נוצר לו משתמש, ואם המשתמש נוצר בינתיים (בין הבנייה לשמירה), כל מה שנכתב לעובד הזה מדולג בתוך הסקריפט
    // עצמו (IF @Skip... = 0). עובד קיים (2026-11-08): רק הוספה, בלי למחוק ובלי לשנות פרטים: מרכזי עבודה ויישומים שחסרים לו
    // (INSERT ... WHERE NOT EXISTS). עובד קיים בלי מה להוסיף לו: לא נוגעים בו בכלל
    private static string Render(ScriptSpec spec, IReadOnlyDictionary<string, string>? resolvedNames)
    {
        var sb = new StringBuilder();
        // Skip = הסימון של העובד בטבלה בדף הוסר: לא נשמר
        var workers = spec.Workers!.Where(IsWritten).ToList();

        sb.AppendLine($"-- Request {Comment(spec.RequestId ?? "web")}. Review before running. Built by the website from the agent's spec; not executed.");
        foreach (var existing in spec.Workers!.Where(w => w.NewUser is null && !w.Skip && !HasExistingWork(w)))
            sb.AppendLine($"-- Worker {existing.UserCode} already has a MANTI user and nothing is added for this worker.");
        foreach (var unchecked_ in spec.Workers!.Where(w => w.Skip))
            sb.AppendLine($"-- Worker {unchecked_.UserCode} was unchecked on the page: not saved.");
        sb.AppendLine("BEGIN TRAN;");

        foreach (var w in workers)
        {
            sb.AppendLine();
            sb.AppendLine($"-- ===== Worker {w.UserCode} =====");

            string uid;
            if (w.NewUser is { } n)
            {
                var first = Clean(n.FirstName)!;
                var last = Clean(n.LastName)!;
                var certified = n.CertificationDate is not null;
                // שם משתמש = מה שהאדם הקליד, ואם לא: שם פרטי באנגלית + 2 אותיות ראשונות של שם המשפחה. הסיסמה: מה שהאדם הקליד,
                // ואם לא: מספר העובד. שם תפוס (אינדקס ייחודי על UserName) יכשיל את ההרצה לניסיון במקום להידלג בשקט
                var userName = n.UserName
                               ?? (resolvedNames is not null && resolvedNames.TryGetValue(w.UserCode!, out var resolved) ? resolved : null)
                               ?? EnglishNames.UserName(n.EngFirstName, n.EngLastName)!;
                var password = n.Password ?? w.UserCode!;
                var engFirst = EnglishNames.ForColumn(n.EngFirstName)!;
                var engLast = EnglishNames.ForColumn(n.EngLastName)!;
                sb.AppendLine($"-- New user {w.UserCode}: user name {userName}, department {n.DepartmentId}, group {n.GroupId}{(certified ? ", certified" : "")}");
                sb.AppendLine($"-- If {w.UserCode} already has a MANTI user when this runs, everything between BEGIN and END below is skipped.");
                sb.AppendLine($"DECLARE @Skip{w.UserCode} bit = CASE WHEN EXISTS (SELECT 1 FROM dbo.User_Profiles WHERE UserCode = '{w.UserCode}') THEN 1 ELSE 0 END;");
                sb.AppendLine($"IF @Skip{w.UserCode} = 0");
                sb.AppendLine("BEGIN");
                sb.AppendLine("    INSERT INTO dbo.User_Profiles");
                sb.AppendLine("        (UserName, UserCode, UserPassword, UserGroupId, DepartmentId,");
                // DateLastModified: הטריגר User_Profiles_I_U מעדכן אותו רק כשהחיבור הוא MANTI 2 (APP_NAME 'Manti' / 'Visual Basic'),
                // ולכן הסקריפט קובע אותו בעצמו
                sb.AppendLine($"         UserFirstName, UserLastName, EngFirstName, EngLastName, PhonNumber, E_MailAdress, UsersStatus, UserBarCode, DateLastModified{(certified ? ", IsCertified, CertificationDate" : "")})");
                sb.AppendLine("    VALUES");
                sb.AppendLine($"        ('{Lit(userName)}', '{w.UserCode}', '{Lit(password)}', {n.GroupId}, {n.DepartmentId},");
                sb.AppendLine($"         N'{Lit(first)}', N'{Lit(last)}', '{engFirst}', '{engLast}', '{Lit(n.Phone ?? string.Empty)}', '{Lit(n.Email ?? string.Empty)}', '', '', GETDATE(){(certified ? $", 1, '{n.CertificationDate}'" : "")});");
                sb.AppendLine($"DECLARE @U{w.UserCode} int = (SELECT UserId FROM dbo.User_Profiles WHERE UserCode = '{w.UserCode}');");
                uid = $"@U{w.UserCode}";
            }
            else
            {
                // עובד קיים: מזהים אותו לפי קוד העובד, ואם אין לו משתמש (נמחק בינתיים) מדלגים עליו בתוך הסקריפט
                sb.AppendLine($"-- Existing user {w.UserCode}: only missing work centers and applications are added; nothing is changed or removed.");
                sb.AppendLine($"DECLARE @U{w.UserCode} int = (SELECT UserId FROM dbo.User_Profiles WHERE UserCode = '{w.UserCode}');");
                sb.AppendLine($"IF @U{w.UserCode} IS NOT NULL");
                sb.AppendLine("BEGIN");
                uid = $"@U{w.UserCode}";
            }

            foreach (var wc in w.WorkCenters ?? [])
            {
                sb.AppendLine($"-- {w.UserCode} -> work center {Comment(wc.Code ?? wc.Id.ToString())}");
                sb.AppendLine("INSERT INTO dbo.Authorisations_WorkCtrs (UserId, WorkCtrId)");
                sb.AppendLine($"SELECT {uid}, {wc.Id}");
                sb.AppendLine($"WHERE NOT EXISTS (SELECT 1 FROM dbo.Authorisations_WorkCtrs WHERE UserId = {uid} AND WorkCtrId = {wc.Id});");
            }

            // מסך אחד בכל משפט: הטריגר Authorizations_Ins_Upd מחתים שורה אחת בלבד בכל משפט
            if (w.CopyScreens)
                foreach (var s in spec.Example!.Screens!)
                {
                    sb.AppendLine($"-- {w.UserCode}: screen {s.ObjId} \"{Comment(s.Name ?? string.Empty)}\" from example {spec.Example.UserCode}");
                    sb.AppendLine("INSERT INTO dbo.Authorizations (UserId, BranchId, ObjId, ControlId)");
                    sb.AppendLine($"SELECT {uid}, {w.BranchId}, ex.ObjId,");
                    sb.AppendLine("       STUFF(REPLACE(CONVERT(char(30), ex.ControlId), ' ', '0'), 30, 1, '1')");
                    sb.AppendLine("FROM dbo.Authorizations ex");
                    sb.AppendLine($"WHERE ex.UserId = {spec.Example.UserId} AND ex.ObjId = {s.ObjId}");
                    sb.AppendLine("  AND NOT EXISTS (SELECT 1 FROM dbo.Authorizations t");
                    sb.AppendLine($"                  WHERE t.UserId = {uid} AND t.ObjId = {s.ObjId});");
                }

            // אחרי ההעתקה, כדי שהגרסה של משתמש הדוגמה תנצח (NOT EXISTS)
            foreach (var d in w.DefaultScreens ?? [])
            {
                sb.AppendLine($"-- {w.UserCode}: default screen {d.ObjId} \"{Comment(d.Name ?? string.Empty)}\"");
                sb.AppendLine("INSERT INTO dbo.Authorizations (UserId, BranchId, ObjId, ControlId)");
                sb.AppendLine($"SELECT {uid}, {w.BranchId}, {d.ObjId},");
                sb.AppendLine($"       STUFF(REPLACE(CONVERT(char(30), '{d.ControlId}'), ' ', '0'), 30, 1, '1')");
                sb.AppendLine("WHERE NOT EXISTS (SELECT 1 FROM dbo.Authorizations t");
                sb.AppendLine($"                  WHERE t.UserId = {uid} AND t.ObjId = {d.ObjId});");
            }

            // יישומים שנוספו בטבלה (2026-11-08): הרשאת גישה בלבד כברירת מחדל (ControlId שהדף שלח), רק אם אין כבר שורה למשתמש ולמסך
            foreach (var a in w.AddScreens ?? [])
            {
                sb.AppendLine($"-- {w.UserCode}: add application {a.ObjId} \"{Comment(a.Name ?? string.Empty)}\"");
                sb.AppendLine("INSERT INTO dbo.Authorizations (UserId, BranchId, ObjId, ControlId)");
                sb.AppendLine($"SELECT {uid}, {w.BranchId}, {a.ObjId},");
                sb.AppendLine($"       STUFF(REPLACE(CONVERT(char(30), '{a.ControlId}'), ' ', '0'), 30, 1, '1')");
                sb.AppendLine("WHERE NOT EXISTS (SELECT 1 FROM dbo.Authorizations t");
                sb.AppendLine($"                  WHERE t.UserId = {uid} AND t.ObjId = {a.ObjId});");
            }

            // סרגל כלים "Lobby" לכל משתמש חדש (User_Toolbars): סרגל מספר 1, הכפתור הראשון הוא היישום 742. שאר הכפתורים
            // נשארים בברירת המחדל של העמודות (USER_B2..6 = '1', USER_OPERCODE2..6 = 0). ראו new-user-defaults.md. לעובד קיים: לא נוגעים
            if (w.NewUser is not null)
            {
                sb.AppendLine($"-- {w.UserCode}: toolbar \"{LobbyText}\" with application {LobbyAppId}");
                sb.AppendLine("INSERT INTO dbo.User_Toolbars (UserId, BranchId, USER_TBCODE, USER_TBTEXT, USER_B1, USER_OPERCODE1)");
                sb.AppendLine($"SELECT {uid}, {w.BranchId}, {LobbyToolbarCode}, '{LobbyText}', '1', {LobbyAppId}");
                sb.AppendLine("WHERE NOT EXISTS (SELECT 1 FROM dbo.User_Toolbars t");
                sb.AppendLine($"                  WHERE t.UserId = {uid} AND t.USER_TBCODE = {LobbyToolbarCode});");
            }

            sb.AppendLine("END");
        }

        AppendCheck(sb, spec);

        sb.AppendLine();
        sb.AppendLine("ROLLBACK TRAN;");
        sb.AppendLine("-- To apply: press the approve button on the page (it dry-runs first), or change ROLLBACK TRAN to COMMIT TRAN here after reviewing the check result.");
        return sb.ToString();
    }

    private static void AppendCheck(StringBuilder sb, ScriptSpec spec)
    {
        var workers = spec.Workers!;

        // רק עובדים חדשים נכתבים; עובד קיים מופיע בבדיקה לפי הקוד שלו בלבד
        var written = workers.Where(IsWritten).ToList();
        var centers = written.SelectMany(w => w.WorkCenters ?? []).Select(wc => wc.Id).Distinct().ToList();
        var screens = written
            .SelectMany(w => (w.CopyScreens ? spec.Example!.Screens!.Select(s => s.ObjId) : [])
                .Concat((w.DefaultScreens ?? []).Select(d => d.ObjId))
                .Concat((w.AddScreens ?? []).Select(d => d.ObjId)))
            .Distinct().ToList();

        sb.AppendLine();
        sb.AppendLine("-- ===== Check =====");
        var columns = new List<string> { "u.UserCode", "u.UserGroupId", "u.DepartmentId" };
        if (centers.Count > 0)
            columns.Add($"(SELECT COUNT(*) FROM dbo.Authorisations_WorkCtrs w WHERE w.UserId = u.UserId AND w.WorkCtrId IN ({string.Join(", ", centers)})) AS WorkCenters_present");
        if (screens.Count > 0)
            columns.Add($"(SELECT COUNT(*) FROM dbo.Authorizations a WHERE a.UserId = u.UserId AND a.ObjId IN ({string.Join(", ", screens)})) AS Screens_present");
        if (written.Any(w => w.NewUser is not null))
            columns.Add($"(SELECT COUNT(*) FROM dbo.User_Toolbars tb WHERE tb.UserId = u.UserId AND tb.USER_OPERCODE1 = {LobbyAppId}) AS Lobby_present");

        sb.AppendLine($"SELECT {string.Join(",\n       ", columns)}");
        sb.AppendLine("FROM dbo.User_Profiles u");
        sb.AppendLine($"WHERE u.UserCode IN ({string.Join(", ", workers.Select(w => $"'{w.UserCode}'"))});");
    }

    // ---------- helpers ----------

    private static bool IsCode(string? code) => !string.IsNullOrEmpty(code) && UserCode().IsMatch(code);

    // כמו MANTI 2: רווחים כפולים מכווצים, קצוות נחתכים
    private static string? Clean(string? name)
        => name is null ? null : Spaces().Replace(name.Trim(), " ");

    // תוכן מחרוזת ב-SQL: גרש מוכפל
    private static string Lit(string value) => value.Replace("'", "''");

    // טקסט בהערה: בלי שורות חדשות ובלי סימני הערה שיחתכו אותה או יסגרו אותה
    private static string Comment(string value)
        => Regex.Replace(value, @"[\r\n\t]|--|/\*|\*/", " ").Trim();

    private static Result<string> Fail(string reason)
        => Result<string>.Failure(Error.Validation("AccessRequest.InvalidScriptSpec", reason));

    [GeneratedRegex("^[0-9A-Za-z]{1,20}$")]
    private static partial Regex UserCode();

    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex Date();

    [GeneratedRegex("^[01 ]{1,30}$")]
    private static partial Regex ControlBits();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,50}$")]
    private static partial Regex UserNameFormat();

    [GeneratedRegex(@"^[\x21-\x7E]{1,10}$")]
    private static partial Regex PasswordFormat();
}

// צורת ה-JSON שהסוכן כותב (camelCase). ראו SKILL.md, "Web mode"
internal sealed record ScriptSpec(string? RequestId, ExampleSpec? Example, List<WorkerSpec>? Workers);

internal sealed record ExampleSpec(string? UserCode, long UserId, List<ScreenSpec>? Screens);

internal sealed record ScreenSpec(int ObjId, string? Name);

internal sealed record WorkerSpec(
    string? UserCode, long? UserId, NewUserSpec? NewUser, int BranchId, List<WorkCenterSpec>? WorkCenters,
    bool CopyScreens, List<DefaultScreenSpec>? DefaultScreens, CertifySpec? CertifyExisting, bool Skip = false,
    string? Name = null, List<DefaultScreenSpec>? AddScreens = null);

// FirstName/LastName = העברית מ-HR (UserFirstName/UserLastName). EngFirstName/EngLastName = האנגלית מ-HR
// (FirstNameE/LastNameE) או תרגום של השם העברי כשחסר: מהם נבנה שם המשתמש (EnglishNames.UserName).
// UserName / Password: ערכים שהאדם הקליד בטבלת העריכה. בלעדיהם: שם המשתמש מחושב מהשמות באנגלית והסיסמה היא מספר העובד
internal sealed record NewUserSpec(
    string? FirstName, string? LastName, string? Phone, string? Email, int DepartmentId, int GroupId,
    string? CertificationDate, string? EngFirstName = null, string? EngLastName = null,
    string? UserName = null, string? Password = null);

internal sealed record WorkCenterSpec(int Id, string? Code);

internal sealed record DefaultScreenSpec(int ObjId, string? Name, string? ControlId);

internal sealed record CertifySpec(int GroupId, string? Date);
