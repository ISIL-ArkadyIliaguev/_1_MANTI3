using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Application.Features.AccessRequests.RunScript;
using NSubstitute;
using Shouldly;

namespace Manti3.Backend.UnitTests;

public class RunAccessScriptTests
{
    private const string Id = "20261104-1152";
    private const string GoodScript = "BEGIN TRAN;\nINSERT INTO dbo.Authorisations_WorkCtrs (UserId, WorkCtrId) SELECT 1, 2;\nROLLBACK TRAN;";

    private readonly IAgentOutput _output = Substitute.For<IAgentOutput>();
    private readonly IScriptExecutor _executor = Substitute.For<IScriptExecutor>();
    private readonly IScriptRunLedger _ledger = new InMemoryLedger();
    private readonly RunAccessScriptValidator _validator = new();

    private static readonly ScriptRunResult Ok = new(false, 3, []);

    private RunAccessScriptHandler Handler() => new(_output, _executor, _ledger);

    public RunAccessScriptTests()
    {
        _output.ReadScript(Id).Returns(GoodScript);
        _executor.Execute(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptRunResult>.Success(Ok));
    }

    [Fact]
    public async Task Handle_DryRun_RunsWithoutCommitAndRemembersIt()
    {
        var result = await Handler().Handle(new RunAccessScriptCommand(Id, false), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _executor.Received(1).Execute(
            Arg.Is<string>(s => !s.Contains("ROLLBACK") && s.Contains("INSERT INTO dbo.Authorisations_WorkCtrs")),
            false, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        _output.DidNotReceive().MarkCommitted(Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_CommitWithoutADryRun_IsRefused()
    {
        var result = await Handler().Handle(new RunAccessScriptCommand(Id, true), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("AccessRequest.DryRunRequired");
        await _executor.DidNotReceiveWithAnyArgs().Execute(default!, default, default, default);
    }

    [Fact]
    public async Task Handle_CommitAfterADryRun_CommitsAndMarksTheRequest()
    {
        await Handler().Handle(new RunAccessScriptCommand(Id, false), CancellationToken.None);

        var result = await Handler().Handle(new RunAccessScriptCommand(Id, true), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _executor.Received(1).Execute(Arg.Any<string>(), true, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        _output.Received(1).MarkCommitted(Id);
    }

    [Fact]
    public async Task Handle_CommitAfterTheScriptChanged_IsRefused()
    {
        await Handler().Handle(new RunAccessScriptCommand(Id, false), CancellationToken.None);
        _output.ReadScript(Id).Returns(GoodScript + "\nSELECT 1;");

        var result = await Handler().Handle(new RunAccessScriptCommand(Id, true), CancellationToken.None);

        result.Error!.Code.ShouldBe("AccessRequest.DryRunRequired");
    }

    [Fact]
    public async Task Handle_AlreadyCommitted_IsAConflict()
    {
        _output.IsCommitted(Id).Returns(true);

        var result = await Handler().Handle(new RunAccessScriptCommand(Id, false), CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        result.Error.Code.ShouldBe("AccessRequest.AlreadyCommitted");
        await _executor.DidNotReceiveWithAnyArgs().Execute(default!, default, default, default);
    }

    [Fact]
    public async Task Handle_NoScriptForTheRequest_IsNotFound()
    {
        _output.ReadScript(Id).Returns((string?)null);

        var result = await Handler().Handle(new RunAccessScriptCommand(Id, false), CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_ScriptWithADelete_IsRefusedAndNeverReachesTheDatabase()
    {
        _output.ReadScript(Id).Returns("BEGIN TRAN;\nDELETE FROM dbo.Authorizations;\nROLLBACK TRAN;");

        var result = await Handler().Handle(new RunAccessScriptCommand(Id, false), CancellationToken.None);

        result.Error!.Code.ShouldBe("AccessRequest.ScriptNotAllowed");
        await _executor.DidNotReceiveWithAnyArgs().Execute(default!, default, default, default);
    }

    [Fact]
    public async Task Handle_ExecutorFails_NothingIsRemembered()
    {
        _executor.Execute(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptRunResult>.Failure(Error.Failure("AccessRequest.ScriptFailed", "boom")));

        var dry = await Handler().Handle(new RunAccessScriptCommand(Id, false), CancellationToken.None);
        var commit = await Handler().Handle(new RunAccessScriptCommand(Id, true), CancellationToken.None);

        dry.Error!.Code.ShouldBe("AccessRequest.ScriptFailed");
        commit.Error!.Code.ShouldBe("AccessRequest.DryRunRequired");
    }

    [Theory]
    [InlineData("20261104-1152", true)]
    [InlineData("20261104-1152-2", true)]
    [InlineData("web-20261104-140313", true)]
    [InlineData("web-20261104-140313-2", true)]
    [InlineData("web-..\\..\\x", false)]
    [InlineData("web-", false)]
    [InlineData("..\\..\\Windows", false)]
    [InlineData("../secret", false)]
    [InlineData("inputs", false)]
    [InlineData("", false)]
    public void Validate_RequestId_OnlyAFolderNameOfTheAgent(string id, bool valid)
        => _validator.Validate(new RunAccessScriptCommand(id, false)).IsValid.ShouldBe(valid);

    // לדג'ר של דקה אחת לבדיקות: זהה בהתנהגות למימוש בזיכרון
    private sealed class InMemoryLedger : IScriptRunLedger
    {
        private readonly Dictionary<string, string> _dryRuns = [];
        public bool HasDryRun(string requestId, string scriptHash) => _dryRuns.GetValueOrDefault(requestId) == scriptHash;
        public void RecordDryRun(string requestId, string scriptHash) => _dryRuns[requestId] = scriptHash;
    }
}
