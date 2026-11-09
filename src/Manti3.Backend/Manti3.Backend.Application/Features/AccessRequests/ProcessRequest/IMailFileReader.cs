namespace Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;

// פורט: קורא קובץ מייל שנגרר מ-Outlook (.msg) או נשמר ממנו (.eml) והופך אותו לטקסט שהסוכן קורא.
// מחזיר null כשהקובץ אינו מייל תקין
public interface IMailFileReader
{
    string? ToText(byte[] content, string fileName);

    // כשה-ToText החזיר null: סיבה טכנית קצרה שמוצגת למשתמש (סוג התוכן, אילו גופים קיימים). בלי תוכן המייל
    string Diagnose(byte[] content, string fileName);
}
