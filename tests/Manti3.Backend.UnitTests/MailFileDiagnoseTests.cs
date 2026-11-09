using System.Text;
using Manti3.Backend.Infrastructure.Agent;
using Shouldly;

namespace Manti3.Backend.UnitTests;

public class MailFileDiagnoseTests
{
    private readonly MailFileReader _reader = new();

    [Fact]
    public void Diagnose_EmlWithoutBody_NamesTheContentTypeAndEmptyBodies()
    {
        var eml = Encoding.UTF8.GetBytes(
            "From: a@example.test\r\nSubject: x\r\nMIME-Version: 1.0\r\nContent-Type: application/pkcs7-mime\r\n\r\nAAAA\r\n");

        var reason = _reader.Diagnose(eml, "mail.eml");

        reason.ShouldContain("pkcs7-mime");
        reason.ShouldContain("text body empty");
        reason.ShouldNotContain("example.test"); // never mail content
    }

    [Fact]
    public void Diagnose_NotAMailFile_SaysSoWithoutContent()
    {
        var reason = _reader.Diagnose(Encoding.UTF8.GetBytes("secret text"), "mail.msg");

        reason.ShouldNotContain("secret");
    }

    [Fact]
    public void ToText_MimeContentNamedMsg_IsReadByContent()
    {
        // new Outlook saves a dragged mail as MIME even when the name says .msg
        var eml = Encoding.UTF8.GetBytes(
            "From: a@example.test\r\nSubject: hello\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nbody 10001\r\n");

        var text = _reader.ToText(eml, "dragged.msg");

        text.ShouldNotBeNull();
        text.ShouldContain("body 10001");
    }

    // a .msg compound file with only the standard MAPI text streams and a message class the reader does not know
    private static byte[] FakeMsg(string messageClass, string subject, string body)
    {
        using var stream = new MemoryStream();
        using (var root = OpenMcdf.RootStorage.Create(stream, OpenMcdf.Version.V3, OpenMcdf.StorageModeFlags.LeaveOpen))
        {
            void Put(string name, string text)
            {
                using var s = root.CreateStream($"__substg1.0_{name}");
                s.Write(System.Text.Encoding.Unicode.GetBytes(text));
            }

            Put("001A001F", messageClass);
            Put("0037001F", subject);
            Put("1000001F", body);
            Put("0C1F001F", "sender@example.test");
        }

        return stream.ToArray();
    }

    [Fact]
    public void ToText_MsgOfAClassTheLibraryDoesNotKnow_IsReadFromRawFields()
    {
        var msg = FakeMsg("IPM.Note.Custom.Unknown", "subject-1", "worker 10001 to center N1S");

        var text = _reader.ToText(msg, "x.msg");

        text.ShouldNotBeNull();
        text.ShouldContain("worker 10001 to center N1S");
        text.ShouldContain("Subject: subject-1");
        _reader.Diagnose(msg, "x.msg").ShouldContain("IPM.Note");
    }

    [Fact]
    public void ToText_JunkNamedMsg_IsNull() => _reader.ToText([1, 2, 3, 4], "x.msg").ShouldBeNull();
}
