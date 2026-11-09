using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;
using MsgReader.Outlook;

namespace Manti3.Backend.Infrastructure.Agent;

// קורא .msg (Outlook) ו-.eml והופך אותם לטקסט: כותרות (מאת, תאריך, נושא) וגוף המייל, כולל שרשור שהועבר.
// שום תוכן לא נרשם ללוג: המייל מכיל שמות ומספרי עובדים
internal sealed partial class MailFileReader : IMailFileReader
{
    public string? ToText(byte[] content, string fileName)
    {
        try
        {
            // לפי התוכן ולא לפי הסיומת: Outlook החדש שומר מייל שנגרר כקובץ MIME (.eml) גם כששמו .msg, ולהפך
            using var stream = new MemoryStream(content);
            if (!IsOutlookMsg(content))
                return FromEml(stream);

            try
            {
                return FromMsg(stream);
            }
            catch (Exception)
            {
                // MsgReader נכשל (סוג הודעה לא מוכר, או כשל פנימי בספרייה): קוראים ישירות את שדות ה-MAPI הרגילים
                return FromRawMsg(content);
            }
        }
        catch (Exception)
        {
            // קובץ פגום או לא נתמך: הקורא מחזיר null וה-API עונה 400
            return null;
        }
    }

    public string Diagnose(byte[] content, string fileName)
    {
        try
        {
            using var stream = new MemoryStream(content);
            if (IsOutlookMsg(content))
            {
                // סוג ההודעה (למשל IPM.Note) הוא נתון טכני ולא תוכן המייל
                var messageClass = RawText(content, "001A") ?? "unknown";
                try
                {
                    using var msg = new Storage.Message(stream);
                    return $"Outlook msg format, class {messageClass}, {content.Length / 1024} KB: text body {Has(msg.BodyText)}, " +
                           $"HTML body {Has(msg.BodyHtml)}, RTF body {Has(msg.BodyRtf)}, {msg.Attachments.Count} attachments";
                }
                catch (Exception libraryError)
                {
                    return $"Outlook msg format, class {messageClass}, the .msg library failed ({Describe(libraryError)}), " +
                           $"raw body {Has(RawText(content, "1000"))}, raw HTML {Has(RawHtml(content))}, {content.Length / 1024} KB";
                }
            }

            var eml = MsgReader.Mime.Message.Load(stream);
            return $"MIME (eml) format, {content.Length / 1024} KB: content type {eml.Headers.ContentType?.MediaType ?? "unknown"}, " +
                   $"text body {Has(eml.TextBody?.GetBodyAsText())}, HTML body {Has(eml.HtmlBody?.GetBodyAsText())}, " +
                   $"{eml.Attachments?.Count ?? 0} attachments";
        }
        catch (Exception ex)
        {
            // סוג החריגה בלבד: ההודעה עלולה להכיל תוכן
            return $"the file is not a readable mail ({(IsOutlookMsg(content) ? "Outlook msg format" : "not Outlook msg format")}, " +
                   $"extension {Path.GetExtension(fileName)}, {Describe(ex)}), {content.Length / 1024} KB";
        }
    }

    // קובץ Outlook .msg הוא מסמך מורכב (OLE) שמתחיל בחתימה D0 CF 11 E0 A1 B1 1A E1
    private static bool IsOutlookMsg(byte[] content)
        => content.Length >= 8
           && content[0] == 0xD0 && content[1] == 0xCF && content[2] == 0x11 && content[3] == 0xE0
           && content[4] == 0xA1 && content[5] == 0xB1 && content[6] == 0x1A && content[7] == 0xE1;

    private static string Has(string? body) => string.IsNullOrWhiteSpace(body) ? "empty" : "present";

    // סוגי חריגה ושם הסוג שנכשל באתחול בלבד: ההודעה עצמה עלולה להכיל תוכן
    private static string Describe(Exception ex)
        => ex is TypeInitializationException { TypeName: { } type }
            ? $"{ex.GetType().Name} in {type}, caused by {ex.InnerException?.GetType().Name ?? "unknown"}"
            : ex.GetType().Name;

    // הודעה מסוג שלא נתמך: קוראים ישירות את שדות ה-MAPI הרגילים של קובץ ה-.msg (נושא, גוף, HTML, שולח)
    private static string? FromRawMsg(byte[] content)
    {
        var body = RawText(content, "1000");
        if (string.IsNullOrWhiteSpace(body))
            body = HtmlToText(RawHtml(content));

        return Compose(RawText(content, "0C1F") ?? RawText(content, "0C1A"), null, RawText(content, "0037"), body);
    }

    private static string? RawHtml(byte[] content)
        => RawStream(content, "1013") is { } bytes ? Encoding.UTF8.GetString(bytes) : null;

    // שדה טקסט: Unicode (001F) או 8 סיביות (001E)
    private static string? RawText(byte[] content, string tag)
    {
        if (RawStream(content, tag + "001F") is { } unicode)
            return Encoding.Unicode.GetString(unicode).TrimEnd('\0');
        if (RawStream(content, tag + "001E") is { } ansi)
            return Encoding.Latin1.GetString(ansi).TrimEnd('\0');
        return null;
    }

    private static byte[]? RawStream(byte[] content, string suffix)
    {
        try
        {
            using var stream = new MemoryStream(content);
            using var root = OpenMcdf.RootStorage.Open(stream);
            var name = $"__substg1.0_{(suffix.Length == 4 ? suffix + "0102" : suffix)}";
            if (!root.TryOpenStream(name, out var cfb))
                return null;

            using (cfb)
            {
                using var copy = new MemoryStream();
                cfb.CopyTo(copy);
                return copy.ToArray();
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? FromMsg(Stream stream)
    {
        using var message = new Storage.Message(stream);
        var body = message.BodyText;
        if (string.IsNullOrWhiteSpace(body))
            body = HtmlToText(message.BodyHtml);

        return Compose(message.Sender?.Email ?? message.Sender?.DisplayName, message.SentOn?.ToString("yyyy-MM-dd HH:mm"),
            message.Subject, body);
    }

    private static string? FromEml(Stream stream)
    {
        var message = MsgReader.Mime.Message.Load(stream);
        var body = message.TextBody?.GetBodyAsText();
        if (string.IsNullOrWhiteSpace(body))
            body = HtmlToText(message.HtmlBody?.GetBodyAsText());

        return Compose(message.Headers.From?.Address, message.Headers.DateSent.ToString("yyyy-MM-dd HH:mm"),
            message.Headers.Subject, body);
    }

    private static string? Compose(string? from, string? sent, string? subject, string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(from)) text.AppendLine($"From: {from}");
        if (!string.IsNullOrWhiteSpace(sent)) text.AppendLine($"Date: {sent}");
        if (!string.IsNullOrWhiteSpace(subject)) text.AppendLine($"Subject: {subject}");
        text.AppendLine();
        text.Append(body.Trim());
        return text.ToString();
    }

    // מייל שיש לו רק גוף HTML: מורידים תגיות ומשאירים שורות
    private static string? HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var text = StyleOrScript().Replace(html, " ");
        text = LineBreaks().Replace(text, "\n");
        text = AnyTag().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        return BlankLines().Replace(text, "\n\n").Trim();
    }

    [GeneratedRegex(@"<(style|script)\b.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleOrScript();

    [GeneratedRegex(@"<\s*(br|/p|/div|/tr|/li|/h\d)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreaks();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"(\r?\n[ \t]*){3,}")]
    private static partial Regex BlankLines();
}
