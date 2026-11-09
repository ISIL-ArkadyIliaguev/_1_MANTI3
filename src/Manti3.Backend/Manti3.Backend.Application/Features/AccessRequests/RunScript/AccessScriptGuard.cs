using System.Text.RegularExpressions;

namespace Manti3.Backend.Application.Features.AccessRequests.RunScript;

// הסקריפט נכתב על ידי מודל מתוך טקסט של מייל, ולכן אסור לסמוך עליו: לפני כל הרצה בודקים שהוא מכיל רק
// את מה שתבניות הסוכן מייצרות (script-templates.md): SELECT, DECLARE, IF NOT EXISTS, INSERT לארבע הטבלאות
// (User_Profiles, Authorisations_WorkCtrs, Authorizations, User_Toolbars),
// ו-UPDATE אחד על ארבע עמודות של User_Profiles (קבוצה, הסמכה, תאריך הסמכה, DateLastModified). כל דבר אחר נדחה
internal static partial class AccessScriptGuard
{
    private static readonly string[] InsertTables =
        ["dbo.user_profiles", "dbo.authorisations_workctrs", "dbo.authorizations", "dbo.user_toolbars"];

    private static readonly string[] UpdateColumns = ["usergroupid", "iscertified", "certificationdate", "datelastmodified"];

    // null = מותר. אחרת הסיבה לדחייה (באנגלית, מוצגת לאדם)
    public static string? Check(string script)
    {
        var code = Sanitize(script);
        if (string.IsNullOrWhiteSpace(code))
            return "The script is empty.";

        var forbidden = ForbiddenWords().Match(code);
        if (forbidden.Success)
            return $"The script contains '{forbidden.Value}', which is not allowed.";

        if (IntoWord().Count(code) != InsertInto().Count(code))
            return "INTO is allowed only as INSERT INTO (SELECT ... INTO creates tables).";

        foreach (Match m in InsertInto().Matches(code))
            if (!InsertTables.Contains(m.Groups[1].Value.Replace("[", "").Replace("]", "").ToLowerInvariant()))
                return $"INSERT into '{m.Groups[1].Value}' is not allowed.";

        foreach (Match m in UpdateStatement().Matches(code))
        {
            if (!m.Groups[1].Value.Replace("[", "").Replace("]", "").Equals("dbo.User_Profiles", StringComparison.OrdinalIgnoreCase))
                return $"UPDATE of '{m.Groups[1].Value}' is not allowed.";

            foreach (Match column in AssignedColumn().Matches(m.Groups[2].Value))
                if (!UpdateColumns.Contains(column.Groups[1].Value.ToLowerInvariant()))
                    return $"UPDATE of the column '{column.Groups[1].Value}' is not allowed.";
        }

        // העסקה מנוהלת על ידי המריץ; BEGIN/COMMIT/ROLLBACK שנשארו אחרי Prepare הם באמצע הסקריפט
        if (TransactionControl().IsMatch(Sanitize(Prepare(script))))
            return "Transaction statements are allowed only at the start and end of the script.";

        return null;
    }

    // הסקריפט הוא אצווה אחת: BEGIN TRAN בראשו ו-ROLLBACK TRAN (או COMMIT TRAN) בסופו.
    // המריץ מנהל את העסקה בעצמו (ביטול או שמירה), לכן שורות העסקה של הסקריפט מוסרות
    // רק ה-BEGIN TRAN הראשון וה-ROLLBACK/COMMIT האחרון מוסרים; כל שורת עסקה אחרת נשארת ו-Check דוחה אותה
    public static string Prepare(string script)
    {
        var withoutBegin = BeginLine().Replace(script, string.Empty, 1);

        var last = EndingLine().Matches(withoutBegin).LastOrDefault();
        return last is null ? withoutBegin : withoutBegin.Remove(last.Index, last.Length);
    }

    // האם הסקריפט משנה נתונים (לעומת סקריפט בדיקה בלבד). בלי זה אין טעם להציג כפתור הרצה
    public static bool HasWrites(string script)
    {
        var code = Sanitize(script);
        return InsertInto().IsMatch(code) || UpdateStatement().IsMatch(code);
    }

    // בלי הערות ובלי תוכן מחרוזות: מילה אסורה בתוך שם או הערה אינה בעיה. מעבר אחד משמאל לימין, כמו מנתח
    // ה-SQL: סימן הערה בתוך מחרוזת (למשל N'a -- b') אינו הערה, ומחרוזת בתוך הערה אינה מחרוזת
    private static string Sanitize(string script)
        => CommentOrString().Replace(script, m => m.Value[0] == '\'' ? "''" : " ");

    [GeneratedRegex(@"\b(delete|drop|alter|truncate|create|exec|execute|merge|grant|revoke|deny|backup|restore|shutdown|kill|reconfigure|waitfor|openrowset|openquery|opendatasource|openxml|bulk|dbcc|go|use|xp_\w+|sp_\w+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenWords();

    [GeneratedRegex(@"\binto\b", RegexOptions.IgnoreCase)]
    private static partial Regex IntoWord();

    [GeneratedRegex(@"\binsert\s+into\s+([\w\.\[\]]+)", RegexOptions.IgnoreCase)]
    private static partial Regex InsertInto();

    [GeneratedRegex(@"\bupdate\s+([\w\.\[\]]+)\s+set\b(.*?)(?:\bwhere\b|;|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UpdateStatement();

    [GeneratedRegex(@"(\w+)\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex AssignedColumn();

    [GeneratedRegex(@"\b(begin|commit|rollback|save)\s+tran", RegexOptions.IgnoreCase)]
    private static partial Regex TransactionControl();

    [GeneratedRegex(@"^[ \t]*BEGIN\s+TRAN(SACTION)?[ \t]*;?[ \t]*(--[^\r\n]*)?\r?$\n?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex BeginLine();

    [GeneratedRegex(@"^[ \t]*(ROLLBACK|COMMIT)\s+TRAN(SACTION)?[ \t]*;?[ \t]*(--[^\r\n]*)?\r?$\n?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex EndingLine();

    [GeneratedRegex(@"'(?:[^']|'')*'|--[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CommentOrString();
}
