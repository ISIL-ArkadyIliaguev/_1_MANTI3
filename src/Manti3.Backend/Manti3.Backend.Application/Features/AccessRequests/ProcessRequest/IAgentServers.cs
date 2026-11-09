namespace Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;

// שרת שמהם הסוכן רשאי לקרוא (SELECT בלבד). הרשימה מוגדרת בהגדרות השרת (Agent:Servers), לא בדפדפן:
// הדפדפן שולח רק שם מהרשימה. הראשון הוא ברירת המחדל (SQL25DEV).
// CanRunScripts = true רק לשרת שעליו מותר לכפתור "הרץ סקריפט" (SQL25DEV); בשאר, קריאה בלבד
// UsesSqlLogin = השרת דורש משתמש וסיסמה של SQL (לא Windows). Ready = false כשההתחברות הזו עוד לא הוגדרה
// (user-secrets / משתני סביבה): השרת מוצג אבל אי אפשר לבחור בו. הסיסמה עצמה לא נמצאת כאן
public sealed record AgentServer(string Name, string DataSource, bool CanRunScripts,
    bool UsesSqlLogin = false, bool Ready = true);

public interface IAgentServers
{
    IReadOnlyList<AgentServer> All { get; }
    AgentServer Default { get; }

    // null או ריק = ברירת המחדל; שם שאינו ברשימה = null
    AgentServer? Find(string? name);
}
