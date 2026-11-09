using Manti3.Backend.Application.Abstractions;

namespace Manti3.Backend.Application.Features.AccessRequests.EditSpec;

// העובדים שבבקשה, כפי שהסוכן קבע ב-spec.json, בצורה שאדם יכול לערוך (טבלת פרטים בדף).
// רק מה שמוצג כאן ניתן לעריכה; שאר ה-spec (משתמש הדוגמה, מסכים, הסמכה של קיים) נשאר כפי שהסוכן כתב
public sealed record SpecView(IReadOnlyList<WorkerView> Workers);

// Included = false: האדם ביטל את הסימון של העובד בטבלה, והוא לא יישמר
// עובד קיים (IsNewUser = false): Name = שם לתצוגה בלבד; WorkCenterIds ו-ScreenIds = מה שיתווסף לו (הוספה בלבד, בלי הסרה);
// ExistingScreenIds = היישומים שכבר יש לו ב-DB (לתצוגה בלבד: מסומנים ונעולים בתפריט)
public sealed record WorkerView(
    string UserCode, bool IsNewUser, NewUserFields? NewUser, IReadOnlyList<int> WorkCenterIds, bool Included = true,
    string? Name = null, IReadOnlyList<int>? ScreenIds = null, IReadOnlyList<int>? ExistingScreenIds = null);

// פרטי משתמש חדש שניתנים לעריכה (העמודות בטבלה). בתצוגה: UserName = השם בפועל (שהוקלד, או מחושב משמות האנגלית),
// Password = הסיסמה בפועל (שהוקלדה, או מספר העובד).
// בעריכה: UserName / Password = null (לא שונה): נשאר מה שהיה; ריק: חזרה לברירת המחדל; ערך: הערך שהוקלד.
// טלפון, תאריך הסמכה וכל שאר ה-spec אינם נערכים כאן ונשארים כפי שהסוכן כתב
public sealed record NewUserFields(
    string? FirstName, string? LastName, string? UserName, string? Password, int GroupId, int DepartmentId,
    string? EngFirstName, string? EngLastName, string? Email);

public sealed record GetAccessSpecQuery(string RequestId) : IQuery<SpecView>;

// עריכה אחת של אדם. NewUser רק לעובד שהסקריפט יוצר לו משתמש; WorkCenterIds = הרשימה המלאה אחרי העריכה (null = ללא שינוי)
// Include = false: העובד לא יישמר (תיבת הסימון בטבלה); ברירת המחדל: נשמר
// ScreenIds = היישומים (ObjId) שיתווספו לעובד, הרשימה המלאה אחרי העריכה (null = ללא שינוי). רק הוספה: הרשאת גישה בלבד
public sealed record WorkerEdit(
    string UserCode, NewUserFields? NewUser, IReadOnlyList<int>? WorkCenterIds, bool Include = true,
    IReadOnlyList<int>? ScreenIds = null);

// הדפדפן שולח ערכים מובנים, לעולם לא SQL: השרת מחיל אותם על ה-spec השמור ובונה את הסקריפט מחדש מהתבניות
public sealed record UpdateAccessSpecCommand(string RequestId, IReadOnlyList<WorkerEdit> Workers)
    : ICommand<UpdatedSpec>;

public sealed record UpdatedSpec(SpecView Spec, string Script, bool ScriptHasChanges);

// ---- רשימות לתפריטי הבחירה ----

public sealed record LookupItem(int Id, string Name);

public sealed record DepartmentItem(int Id, string Name, string? Code);

// Display = false: מרכז עבודה שלא מוצג במסך של MANTI 2 (WorkCtrDisplay = 0)
public sealed record WorkCenterItem(int Id, string Code, string Name, int? DepartmentId, bool Display);

// יישום (מסך) שאפשר לתת למשתמש: ObjId, שם פנימי וכותרת (AppsAuthorizations_V)
public sealed record ApplicationItem(int Id, string Name, string Caption);

public sealed record AccessLookupData(
    IReadOnlyList<LookupItem> Groups, IReadOnlyList<DepartmentItem> Departments, IReadOnlyList<WorkCenterItem> WorkCenters,
    IReadOnlyList<ApplicationItem>? Applications = null);

public sealed record GetAccessLookupsQuery(string? Server) : IQuery<AccessLookupData>;

// פורט: קריאה בלבד (SELECT) מהשרת שנבחר. הרשימות נשמרות בזיכרון כמה דקות
public interface IAccessLookups
{
    Task<Result<AccessLookupData>> Get(string? server, CancellationToken ct);

    // אילו משמות המשתמש האלה כבר קיימים ב-User_Profiles (UserName ייחודי). בדיקה טרייה בכל פעם, בלי מטמון
    Task<Result<IReadOnlySet<string>>> TakenUserNames(string? server, IReadOnlyCollection<string> names, CancellationToken ct);

    // היישומים (ObjId) שכבר יש למשתמשים האלה ב-Authorizations (SELECT בלבד, בלי מטמון): כדי שהדף יסמן מה כבר קיים
    Task<Result<IReadOnlyDictionary<long, IReadOnlyList<int>>>> ExistingApplications(
        string? server, IReadOnlyCollection<long> userIds, CancellationToken ct);
}
