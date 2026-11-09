using System.Text;
using Manti3.Backend.Infrastructure.Agent;
using Shouldly;

namespace Manti3.Backend.UnitTests;

// קבצי מייל מומצאים בלבד: אין כאן שמות או מספרי עובדים אמיתיים
public class MailFileReaderTests
{
    private readonly MailFileReader _reader = new();

    private static byte[] Eml(string contentType, string body)
        => Encoding.UTF8.GetBytes(
            "From: Boss <boss@example.test>\r\n" +
            "To: Agent <agent@example.test>\r\n" +
            "Date: Wed, 04 Nov 2026 09:30:00 +0200\r\n" +
            "Subject: =?utf-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes("הרשאות")) + "?=\r\n" +
            "MIME-Version: 1.0\r\n" +
            $"Content-Type: {contentType}; charset=utf-8\r\n" +
            "Content-Transfer-Encoding: base64\r\n\r\n" +
            Convert.ToBase64String(Encoding.UTF8.GetBytes(body)) + "\r\n");

    [Fact]
    public void ToText_PlainEml_ReturnsHeadersAndHebrewBody()
    {
        var text = _reader.ToText(Eml("text/plain", "נא להוסיף את 10001 למרכז עבודה N1S"), "mail.eml");

        text.ShouldNotBeNull();
        text.ShouldContain("From: boss@example.test");
        text.ShouldContain("Subject: הרשאות");
        text.ShouldContain("נא להוסיף את 10001 למרכז עבודה N1S");
    }

    [Fact]
    public void ToText_HtmlOnlyEml_StripsTagsAndKeepsLines()
    {
        var html = "<html><head><style>p{color:red}</style></head><body><p>שורה ראשונה</p><p>עובד 10002 &amp; 10003</p></body></html>";

        var text = _reader.ToText(Eml("text/html", html), "mail.eml");

        text.ShouldNotBeNull();
        text.ShouldContain("שורה ראשונה");
        text.ShouldContain("עובד 10002 & 10003");
        text.ShouldNotContain("<p>");
        text.ShouldNotContain("color:red");
    }

    [Theory]
    [InlineData("mail.msg")]
    [InlineData("mail.eml")]
    public void ToText_FileThatIsNotAMail_ReturnsNull(string name)
        => _reader.ToText(Encoding.ASCII.GetBytes("this is not a mail"), name).ShouldBeNull();
}
