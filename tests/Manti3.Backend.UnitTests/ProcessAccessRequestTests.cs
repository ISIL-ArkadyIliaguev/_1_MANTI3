using System.Text;
using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;
using NSubstitute;
using Shouldly;

namespace Manti3.Backend.UnitTests;

public class ProcessAccessRequestTests
{
    private readonly IAccessRequestAgent _agent = Substitute.For<IAccessRequestAgent>();
    private readonly IMailFileReader _mailReader = Substitute.For<IMailFileReader>();
    private readonly ProcessAccessRequestValidator _validator = new();

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7 test");
    private static readonly byte[] Bytes1 = [1, 2, 3];
    private static readonly byte[] Bytes2 = [4, 5, 6];

    private static MailFileInput Mail(string name, byte[]? content = null) => new(name, content ?? Bytes1);

    private static PdfFileInput PdfFile(byte[]? content = null) => new("form.pdf", content ?? Pdf);

    private ProcessAccessRequestHandler Handler() => new(_agent, _mailReader);

    private string? SentToAgent()
        => Encoding.UTF8.GetString(
            _agent.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IAccessRequestAgent.Process))
                .GetArguments().OfType<AgentInput>().Single().Content);

    private void AgentSucceeds()
        => _agent.Process(Arg.Any<AgentInput>(), Arg.Any<CancellationToken>())
            .Returns(new AgentRun(true, "summary", "20261028-1400", "plan", "script"));

    [Fact]
    public async Task Handle_SeveralMails_AreSentTogetherInOrderToTheAgent()
    {
        _mailReader.ToText(Bytes1, "a.msg").Returns("Subject: first\n\nהוסף את 10001");
        _mailReader.ToText(Bytes2, "b.eml").Returns("Subject: second\n\nהוסף את 10002");
        AgentSucceeds();

        var result = await Handler().Handle(
            new ProcessAccessRequestCommand(null, null, [Mail("a.msg"), Mail("b.eml", Bytes2)]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var sent = SentToAgent()!;
        sent.ShouldContain("=== Mail 1 of 2 ===");
        sent.ShouldContain("=== Mail 2 of 2 ===");
        sent.IndexOf("הוסף את 10001", StringComparison.Ordinal)
            .ShouldBeLessThan(sent.IndexOf("הוסף את 10002", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_MailsAndTextBox_AreSentTogetherWithTheTextFirst()
    {
        _mailReader.ToText(Bytes1, "a.msg").Returns("Subject: first\n\nהוסף את 10001");
        AgentSucceeds();

        var result = await Handler().Handle(
            new ProcessAccessRequestCommand("גם את 10009, בהצלחה", null, [Mail("a.msg")]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var sent = SentToAgent()!;
        sent.ShouldContain("גם את 10009, בהצלחה");
        sent.ShouldContain("הוסף את 10001");
        sent.IndexOf("גם את 10009", StringComparison.Ordinal)
            .ShouldBeLessThan(sent.IndexOf("הוסף את 10001", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_OneOfTheMailsCannotBeRead_FailsAndNamesWhichOne()
    {
        _mailReader.ToText(Bytes1, "a.msg").Returns("fine");
        _mailReader.ToText(Bytes2, "b.eml").Returns((string?)null);

        var result = await Handler().Handle(
            new ProcessAccessRequestCommand(null, null, [Mail("a.msg"), Mail("b.eml", Bytes2)]), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("AccessRequest.UnreadableMail");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Description.ShouldContain("2");
        await _agent.DidNotReceiveWithAnyArgs().Process(default!, default);
    }

    [Fact]
    public async Task Handle_AllTheTextTogetherIsTooLong_IsRefusedBeforeTheAgent()
    {
        _mailReader.ToText(Arg.Any<byte[]>(), Arg.Any<string>()).Returns(new string('א', 60_000));

        var result = await Handler().Handle(
            new ProcessAccessRequestCommand(null, null, [Mail("a.msg"), Mail("b.msg", Bytes2)]), CancellationToken.None);

        result.Error!.Code.ShouldBe("AccessRequest.TooMuchText");
        await _agent.DidNotReceiveWithAnyArgs().Process(default!, default);
    }

    [Theory]
    [InlineData("request.msg", true)]
    [InlineData("REQUEST.EML", true)]
    [InlineData("request.docx", false)]
    [InlineData("", false)]
    public void Validate_MailFile_ByExtension(string name, bool valid)
        => _validator.Validate(new ProcessAccessRequestCommand(null, null, [Mail(name)])).IsValid.ShouldBe(valid);

    [Fact]
    public void Validate_MailsAndTextBoxTogether_IsValid()
        => _validator.Validate(new ProcessAccessRequestCommand("text", null, [Mail("a.msg"), Mail("b.eml")]))
            .IsValid.ShouldBeTrue();


    [Fact]
    public void Validate_MoreThanTenMails_IsInvalid()
        => _validator.Validate(new ProcessAccessRequestCommand(null, null,
                Enumerable.Range(0, 11).Select(i => Mail($"m{i}.msg")).ToList()))
            .IsValid.ShouldBeFalse();

    [Fact]
    public void Validate_TenMails_IsValid()
        => _validator.Validate(new ProcessAccessRequestCommand(null, null,
                Enumerable.Range(0, 10).Select(i => Mail($"m{i}.msg")).ToList()))
            .IsValid.ShouldBeTrue();

    [Fact]
    public void Validate_MailFileOver10Mb_IsInvalid()
        => _validator.Validate(new ProcessAccessRequestCommand(null, null,
                [Mail("a.msg", new byte[10 * 1024 * 1024 + 1])]))
            .IsValid.ShouldBeFalse();

    [Fact]
    public void Validate_EmptyMailList_AndNoText_IsInvalid()
        => _validator.Validate(new ProcessAccessRequestCommand(null, null, [])).IsValid.ShouldBeFalse();

    [Fact]
    public async Task Handle_EmailText_SendsItToTheAgentAsMarkdown()
    {
        _agent.Process(Arg.Any<AgentInput>(), Arg.Any<CancellationToken>())
            .Returns(new AgentRun(true, "summary", "20261028-1400", "plan", "script"));

        var result = await Handler()
            .Handle(new ProcessAccessRequestCommand("בוקר טוב", null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new AccessRequestResult("20261028-1400", "summary", "plan", "script"));
        await _agent.Received(1).Process(
            Arg.Is<AgentInput>(i => i.Extension == ".md" && Encoding.UTF8.GetString(i.Content) == "בוקר טוב"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AgentExplanation_IsReturnedWithTheResult()
    {
        _agent.Process(Arg.Any<AgentInput>(), Arg.Any<CancellationToken>())
            .Returns(new AgentRun(true, "summary", "20261028-1400", "plan", "script",
                "- עובד 10001 יצורף למרכז עבודה 777"));

        var result = await Handler()
            .Handle(new ProcessAccessRequestCommand("בוקר טוב", null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Explanation.ShouldBe("- עובד 10001 יצורף למרכז עבודה 777");
    }

    [Fact]
    public async Task Handle_OnlyOnePdf_SendsItToTheAgentAsPdfLikeBefore()
    {
        _agent.Process(Arg.Any<AgentInput>(), Arg.Any<CancellationToken>())
            .Returns(new AgentRun(true, "summary", "20261028-1400", "plan", null));

        var result = await Handler()
            .Handle(new ProcessAccessRequestCommand(null, [PdfFile()]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _agent.Received(1).Process(
            Arg.Is<AgentInput>(i => i.Extension == ".pdf" && i.Content.SequenceEqual(Pdf) && i.Attachments == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PdfWithMailsAndText_SendsOneDocumentWithThePdfAttached()
    {
        _mailReader.ToText(Bytes1, "a.msg").Returns("Subject: first\n\nהוסף את 10001");
        AgentSucceeds();

        var result = await Handler().Handle(
            new ProcessAccessRequestCommand("הערה לסוכן", [PdfFile()], [Mail("a.msg")]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var call = _agent.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IAccessRequestAgent.Process))
            .GetArguments().OfType<AgentInput>().Single();
        call.Extension.ShouldBe(".md");
        var sent = Encoding.UTF8.GetString(call.Content);
        sent.ShouldContain("הערה לסוכן");
        sent.ShouldContain("הוסף את 10001");
        call.Attachments!.Count.ShouldBe(1);
        call.Attachments[0].Extension.ShouldBe(".pdf");
        call.Attachments[0].Content.ShouldBe(Pdf);
    }

    [Fact]
    public async Task Handle_TwoPdfsOnly_AreBothAttachedToAnOtherwiseEmptyDocument()
    {
        AgentSucceeds();

        var result = await Handler().Handle(
            new ProcessAccessRequestCommand(null, [PdfFile(), PdfFile()]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var call = _agent.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IAccessRequestAgent.Process))
            .GetArguments().OfType<AgentInput>().Single();
        call.Extension.ShouldBe(".md");
        call.Attachments!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_PdfPlusText_AttachesThePdfAndKeepsTheTextAsIs()
    {
        AgentSucceeds();

        await Handler().Handle(new ProcessAccessRequestCommand("הוראות", [PdfFile()]), CancellationToken.None);

        var call = _agent.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IAccessRequestAgent.Process))
            .GetArguments().OfType<AgentInput>().Single();
        Encoding.UTF8.GetString(call.Content).ShouldBe("הוראות");
        call.Attachments!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_AgentFails_ReturnsFailure()
    {
        _agent.Process(Arg.Any<AgentInput>(), Arg.Any<CancellationToken>())
            .Returns(new AgentRun(false, "boom", null, null, null));

        var result = await Handler()
            .Handle(new ProcessAccessRequestCommand("text", null), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("AccessRequest.AgentFailed");
    }

    [Fact]
    public async Task Handle_AgentWritesNoOutputFolderAndNoReply_ReturnsFailure()
    {
        _agent.Process(Arg.Any<AgentInput>(), Arg.Any<CancellationToken>())
            .Returns(new AgentRun(true, "  ", null, null, null));

        var result = await Handler()
            .Handle(new ProcessAccessRequestCommand("text", null), CancellationToken.None);

        result.Error!.Code.ShouldBe("AccessRequest.NoOutput");
    }

    [Fact]
    public async Task Handle_AgentWritesNoSpecButReplies_ShowsItsReplyInsteadOfFailing()
    {
        _agent.Process(Arg.Any<AgentInput>(), Arg.Any<CancellationToken>())
            .Returns(new AgentRun(true, "העובד לא נמצא ב-HR", null, null, null));

        var result = await Handler()
            .Handle(new ProcessAccessRequestCommand("text", null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Summary.ShouldBe("העובד לא נמצא ב-HR");
        result.Value.Script.ShouldBeNull();
        result.Value.ScriptHasChanges.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, false)]   // nothing
    [InlineData("  ", false)]   // blank text
    [InlineData("text", true)]  // text only
    public void Validate_Text(string? text, bool valid)
        => _validator.Validate(new ProcessAccessRequestCommand(text, null)).IsValid.ShouldBe(valid);

    [Fact]
    public void Validate_TextAndPdfTogether_IsValid()
        => _validator.Validate(new ProcessAccessRequestCommand("text", [PdfFile()])).IsValid.ShouldBeTrue();

    [Fact]
    public void Validate_PdfOnly_IsValid()
        => _validator.Validate(new ProcessAccessRequestCommand(null, [PdfFile()])).IsValid.ShouldBeTrue();

    [Fact]
    public void Validate_MailsAndPdfAndText_AllTogether_IsValid()
        => _validator.Validate(new ProcessAccessRequestCommand("text", [PdfFile()], [Mail("a.msg")]))
            .IsValid.ShouldBeTrue();

    [Fact]
    public void Validate_FileThatIsNotPdf_IsInvalid()
        => _validator.Validate(new ProcessAccessRequestCommand(null,
                [new PdfFileInput("x.pdf", Encoding.ASCII.GetBytes("MZ not a pdf"))]))
            .IsValid.ShouldBeFalse();

    [Fact]
    public void Validate_MoreThanFivePdfs_IsInvalid()
        => _validator.Validate(new ProcessAccessRequestCommand(null, Enumerable.Range(0, 6).Select(_ => PdfFile()).ToList()))
            .IsValid.ShouldBeFalse();

    [Fact]
    public void Validate_PdfOver5Mb_IsInvalid()
    {
        var big = new byte[5 * 1024 * 1024 + 1];
        Pdf.CopyTo(big, 0);
        _validator.Validate(new ProcessAccessRequestCommand(null, [PdfFile(big)])).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_TextOverTheLimit_IsInvalid()
        => _validator.Validate(new ProcessAccessRequestCommand(new string('א', 20_001), null))
            .IsValid.ShouldBeFalse();
}
