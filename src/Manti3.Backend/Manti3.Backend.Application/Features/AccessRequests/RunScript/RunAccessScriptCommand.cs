using Manti3.Backend.Application.Abstractions;

namespace Manti3.Backend.Application.Features.AccessRequests.RunScript;

// RequestId = שם תיקיית הריצה ב-.agent-output. הדפדפן לא שולח SQL: הסקריפט נקרא מהתיקייה של השרת.
// Commit = false: הרצה לניסיון (הכול רץ ואז מתבטל). Commit = true: אחרי הרצה לניסיון של אותו סקריפט, שומר
public sealed record RunAccessScriptCommand(string RequestId, bool Commit) : ICommand<ScriptRunResult>;

// RowsAffected = סך השורות ש-INSERT/UPDATE נגעו בהן (0 כשאין שינויים)
public sealed record ScriptRunResult(bool Committed, int RowsAffected, IReadOnlyList<ScriptResultSet> ResultSets);

public sealed record ScriptResultSet(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows);

// פורט: קריאת הסקריפט שהסוכן כתב וסימון "הורץ ונשמר" (קובץ בתיקיית הריצה, שורד הפעלה מחדש)
public interface IAgentOutput
{
    string? ReadScript(string requestId);

    // spec.json של הריצה (העובדות שהסוכן קבע) ושם השרת שממנו נקרא (server.txt; null = ברירת המחדל)
    string? ReadSpec(string requestId);
    string? ReadServerName(string requestId);

    // אחרי עריכה בדף: שומר את ה-spec המעודכן ואת הסקריפט שנבנה ממנו, במקום הקודמים
    void WriteSpecAndScript(string requestId, string specJson, string script);
    bool IsCommitted(string requestId);

    // true כשהבקשה נקראה משרת לקריאה בלבד (server.txt בתיקייה): את הסקריפט שלה לא מריצים. בקשות ישנות בלי הקובץ: false
    bool IsRunBlockedByServer(string requestId);
    void MarkCommitted(string requestId);
}

// פורט: מריץ את גוף הסקריפט בעסקה אחת ומבטל או שומר. המימוש מסרב לכל שרת שלא מוגדר לו כתיבה (SQL25DEV, ו-SQL2025CLN2 עם AllowWrite)
public interface IScriptExecutor
{
    Task<Result<ScriptRunResult>> Execute(string body, bool commit, string? serverName, CancellationToken ct);
}

// פורט: זוכר (בזיכרון) איזה סקריפט עבר הרצה לניסיון. שמירה מותרת רק על סקריפט שרץ לניסיון
public interface IScriptRunLedger
{
    bool HasDryRun(string requestId, string scriptHash);
    void RecordDryRun(string requestId, string scriptHash);
}
