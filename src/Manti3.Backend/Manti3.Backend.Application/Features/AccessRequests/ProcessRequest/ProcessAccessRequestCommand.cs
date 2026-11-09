using Manti3.Backend.Application.Abstractions;

namespace Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;

// בקשת הרשאות אחת: כל שילוב של טקסט מהתיבה, קבצי מייל (.msg/.eml שנגררו מ-Outlook) וטפסי הסמכה (PDF).
// הכול יחד נשלח לסוכן כבקשה אחת
public sealed record ProcessAccessRequestCommand(
    string? EmailText, IReadOnlyList<PdfFileInput>? PdfFiles, IReadOnlyList<MailFileInput>? MailFiles = null,
    string? Server = null)
    : ICommand<AccessRequestResult>;

public sealed record MailFileInput(string Name, byte[] Content);

public sealed record PdfFileInput(string Name, byte[] Content);

// מה שהסוכן הכין. הסקריפט לא הורץ: אדם מריץ אותו ב-SSMS
// ScriptHasChanges = false לסקריפט בדיקה בלבד (SELECT): אז אין מה להריץ ולכפתור "הרץ סקריפט" אין מקום
// Server = שם השרת שממנו נקרא המידע; CanRunScript = false כשהשרת הוא לקריאה בלבד (אז אין "הרץ סקריפט")
public sealed record AccessRequestResult(string RequestId, string Summary, string? Plan, string? Script,
    string? Explanation = null, bool ScriptHasChanges = false, string? Server = null, bool CanRunScript = true);
