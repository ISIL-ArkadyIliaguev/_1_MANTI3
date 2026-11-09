using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Application.Features.AccessRequests.EditSpec;
using Manti3.Backend.Application.Features.AccessRequests.RunScript;
using NSubstitute;
using Shouldly;

namespace Manti3.Backend.UnitTests;

public class UpdateAccessSpecTests
{
    private const string Id = "web-20261105-101500";

    private const string Spec = """
        { "requestId": "web-20261105-101500",
          "workers": [
            { "userCode": "10001", "userId": 5001, "branchId": 1, "name": "Worker A" },
            { "userCode": "10002", "branchId": 1,
              "newUser": { "firstName": "דנה", "lastName": "כהן", "engFirstName": "Dana", "engLastName": "Cohen",
                           "phone": "3006", "email": "a@example.test", "departmentId": 31341, "groupId": 196 },
              "workCenters": [ { "id": 926, "code": "N1S" } ] }
          ] }
        """;

    private static readonly AccessLookupData Lookups = new(
        [new LookupItem(196, "Trainees"), new LookupItem(146, "Group 146")],
        [new DepartmentItem(31341, "Dept A", "A1"), new DepartmentItem(31086, "Dept B", "B1")],
        [new WorkCenterItem(926, "N1S", "Center 1", 31341, true), new WorkCenterItem(927, "N2S", "Center 2", 31341, true),
         new WorkCenterItem(928, "X9", "Hidden", 31086, false)],
        [new ApplicationItem(742, "AppA", "Screen A"), new ApplicationItem(788, "AppB", "Screen B")]);

    private readonly IAgentOutput _output = Substitute.For<IAgentOutput>();
    private readonly IAccessLookups _lookups = Substitute.For<IAccessLookups>();

    public UpdateAccessSpecTests()
    {
        _output.ReadSpec(Id).Returns(Spec);
        _lookups.Get(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Result<AccessLookupData>.Success(Lookups));
        _lookups.TakenUserNames(Arg.Any<string?>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlySet<string>>.Success(new HashSet<string>()));
        // worker 10001 (user id 5001) already has applications 788 and 1712
        _lookups.ExistingApplications(Arg.Any<string?>(), Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<long, IReadOnlyList<int>>>.Success(
                new Dictionary<long, IReadOnlyList<int>> { [5001] = [788, 1712] }));
    }

    [Fact]
    public async Task Edit_TypedUserNameAndPassword_AreUsedInsteadOfTheComputedOnes()
    {
        var result = await Run(new WorkerEdit("10002", Fields(userName: "dana.s", password: "pw1234"), null));

        result.IsSuccess.ShouldBeTrue(result.Error?.Description);
        result.Value.Script.ShouldContain("('dana.s', '10002', 'pw1234', 196, 31341,");
        result.Value.Spec.Workers.Single(w => w.UserCode == "10002").NewUser!.UserName.ShouldBe("dana.s");
        result.Value.Spec.Workers.Single(w => w.UserCode == "10002").NewUser!.Password.ShouldBe("pw1234");
    }

    [Fact]
    public async Task Edit_UserNameLeftUnchanged_FollowsTheEnglishNames_AndKeepsAPreviousTypedOne()
    {
        // first a typed name, then an edit that does not touch the user name (null = unchanged): the typed name stays
        var typed = await Run(new WorkerEdit("10002", Fields(userName: "custom1"), null));
        typed.IsSuccess.ShouldBeTrue(typed.Error?.Description);
        _output.ReadSpec(Id).Returns(typed.Value.Script.Contains("custom1") ? CapturedSpec() : Spec);

        var again = await Run(new WorkerEdit("10002", Fields("Yossi", "Levi"), null));

        again.IsSuccess.ShouldBeTrue(again.Error?.Description);
        again.Value.Script.ShouldContain("('custom1', '10002'");
    }

    private string CapturedSpec()
        => (string)_output.ReceivedCalls().Last(c => c.GetMethodInfo().Name == nameof(IAgentOutput.WriteSpecAndScript))
            .GetArguments()[1]!;

    [Fact]
    public async Task Edit_EmptyUserName_GoesBackToTheComputedName()
    {
        var typed = await Run(new WorkerEdit("10002", Fields(userName: "custom1"), null));
        _output.ReadSpec(Id).Returns(CapturedSpec());

        var cleared = await Run(new WorkerEdit("10002", Fields(userName: ""), null));

        cleared.IsSuccess.ShouldBeTrue(cleared.Error?.Description);
        typed.IsSuccess.ShouldBeTrue();
        cleared.Value.Script.ShouldContain("('DanaC', '10002'");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("quo'te")]
    [InlineData("עברית")]
    public async Task Edit_UserNameWithBadCharacters_IsRejected(string name)
    {
        var result = await Run(new WorkerEdit("10002", Fields(userName: name), null));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Description.ShouldContain("userName");
    }

    [Theory]
    [InlineData("12345678901")] // 11 characters, the column is 10
    [InlineData("has space")]
    public async Task Edit_BadPassword_IsRejected(string password)
    {
        var result = await Run(new WorkerEdit("10002", Fields(password: password), null));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Description.ShouldContain("password");
    }

    [Fact]
    public async Task Edit_TypedUserNameTakenByAnExistingUser_IsRejectedAndNothingIsSaved()
    {
        _lookups.TakenUserNames(Arg.Any<string?>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlySet<string>>.Success(new HashSet<string> { "dana.s" }));

        var result = await Run(new WorkerEdit("10002", Fields(userName: "dana.s"), null));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Description.ShouldContain("already taken");
        _output.DidNotReceiveWithAnyArgs().WriteSpecAndScript(default!, default!, default!);
    }

    [Fact]
    public async Task Edit_ComputedUserNameTaken_GetsTheNextLetterOfTheLastName_UntilFree()
    {
        // Dana + Cohen: DanaC and DanaCo are taken, so the user name is DanaCoh
        _lookups.TakenUserNames(Arg.Any<string?>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlySet<string>>.Success(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DanaC", "danaco" }));

        var result = await Run(new WorkerEdit("10002", null, [926]));

        result.IsSuccess.ShouldBeTrue(result.Error?.Description);
        result.Value.Script.ShouldContain("('DanaCoh', '10002'");
        result.Value.Spec.Workers.Single(w => w.UserCode == "10002").NewUser!.UserName.ShouldBe("DanaCoh");
    }

    [Fact]
    public async Task Edit_EveryComputedUserNameTaken_IsRejectedWithAClearMessage()
    {
        _lookups.TakenUserNames(Arg.Any<string?>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlySet<string>>.Success(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DanaC", "DanaCo", "DanaCoh", "DanaCohe", "DanaCohen" }));

        var result = await Run(new WorkerEdit("10002", null, [926]));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Description.ShouldContain("No free user name");
        _output.DidNotReceiveWithAnyArgs().WriteSpecAndScript(default!, default!, default!);
    }

    private Task<Result<UpdatedSpec>> Run(params WorkerEdit[] edits)
        => new UpdateAccessSpecHandler(_output, _lookups).Handle(new UpdateAccessSpecCommand(Id, edits), CancellationToken.None);

    private static NewUserFields Fields(string eng1 = "Dana", string eng2 = "Cohen", int department = 31341, int group = 196,
        string? userName = null, string? password = null)
        => new("דנה", "כהן", userName, password, group, department, eng1, eng2, "a@example.test");

    [Fact]
    public async Task Edit_NewUserFieldsAndWorkCenters_RebuildsTheScriptAndSavesIt()
    {
        var result = await Run(new WorkerEdit("10002", Fields("Yossi", "Levi", 31086, 146), [926, 927]));

        result.IsSuccess.ShouldBeTrue(result.Error?.Description);
        result.Value.Script.ShouldContain("('YossiL', '10002', '10002', 146, 31086,");
        result.Value.Script.ShouldContain("'Yossi', 'Levi'");
        result.Value.Script.ShouldContain("work center N2S");
        result.Value.ScriptHasChanges.ShouldBeTrue();
        result.Value.Spec.Workers.Single(w => w.UserCode == "10002").NewUser!.UserName.ShouldBe("YossiL");
        result.Value.Spec.Workers.Select(w => w.UserCode).ShouldBe(["10001", "10002"]); // the existing worker is a row too
        _output.Received(1).WriteSpecAndScript(Id, Arg.Is<string>(s => s.Contains("\"engFirstName\":\"Yossi\"")),
            Arg.Is<string>(s => s.Contains("YossiL")));
    }

    [Fact]
    public async Task Edit_WorkCentersOnly_KeepsTheNewUserAsTheAgentWroteIt()
    {
        var result = await Run(new WorkerEdit("10002", null, [926, 927]));

        result.IsSuccess.ShouldBeTrue(result.Error?.Description);
        result.Value.Script.ShouldContain("('DanaC', '10002', '10002', 196, 31341,");
    }

    [Fact]
    public async Task Edit_RemovingAllWorkCenters_IsAllowed()
    {
        var result = await Run(new WorkerEdit("10002", null, []));

        result.IsSuccess.ShouldBeTrue(result.Error?.Description);
        result.Value.Script.ShouldNotContain("10002 -> work center");
        result.Value.Script.ShouldNotContain("10001 -> work center"); // nothing was asked for the existing worker
    }

    [Fact]
    public async Task Edit_UncheckedWorker_IsNotSavedAtAll()
    {
        var result = await Run(new WorkerEdit("10002", Fields(), [926], Include: false));

        result.IsSuccess.ShouldBeTrue(result.Error?.Description);
        result.Value.Script.ShouldContain("-- Worker 10002 was unchecked on the page: not saved.");
        result.Value.Script.ShouldNotContain("INSERT");
        result.Value.Script.ShouldNotContain("User_Toolbars");
        result.Value.ScriptHasChanges.ShouldBeFalse();
        result.Value.Spec.Workers.Single(w => w.UserCode == "10002").Included.ShouldBeFalse();
    }

    [Fact]
    public async Task Edit_UncheckedWorker_AnInvalidRowDoesNotBlockTheScript()
    {
        // an unchecked row is not validated: it is not saved, so a half-typed English name is no problem
        var result = await Run(new WorkerEdit("10002", Fields("", ""), [926], Include: false));

        result.IsSuccess.ShouldBeTrue(result.Error?.Description);
    }

    [Fact]
    public async Task Edit_UncheckedThenChecked_IsSavedAgain()
    {
        var unchecked_ = await Run(new WorkerEdit("10002", Fields(), [926], Include: false));
        _output.ReadSpec(Id).Returns(CapturedSpec());

        var again = await Run(new WorkerEdit("10002", Fields(), [926], Include: true));

        unchecked_.IsSuccess.ShouldBeTrue();
        again.IsSuccess.ShouldBeTrue(again.Error?.Description);
        again.Value.Script.ShouldContain("IF @Skip10002 = 0");
        again.Value.Spec.Workers.Single(w => w.UserCode == "10002").Included.ShouldBeTrue();
    }

    [Fact]
    public async Task Edit_UncheckedWorkersUserNameIsNotCheckedAsTaken()
    {
        _lookups.TakenUserNames(Arg.Any<string?>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlySet<string>>.Success(new HashSet<string> { "DanaC" }));

        var result = await Run(new WorkerEdit("10002", null, [926], Include: false));

        result.IsSuccess.ShouldBeTrue(result.Error?.Description);
    }

    [Fact]
    public async Task Edit_ExistingWorker_AddingWorkCentersAndApplications_WritesOnlyInserts()
    {
        var result = await Run(new WorkerEdit("10001", null, [927], ScreenIds: [742]));

        result.IsSuccess.ShouldBeTrue(result.Error?.Description);
        result.Value.Script.ShouldContain("SELECT @U10001, 927");
        result.Value.Script.ShouldContain("add application 742 \"Screen A\"");
        result.Value.Script.ShouldContain("WHERE t.UserId = @U10001 AND t.ObjId = 742");
        result.Value.Script.ShouldNotContain("New user 10001"); // worker 10002 in this spec is new; 10001 is only added to
        result.Value.Script.ShouldNotContain("UPDATE");
        result.Value.ScriptHasChanges.ShouldBeTrue();
        var row =result.Value.Spec.Workers.Single(w => w.UserCode == "10001");
        row.IsNewUser.ShouldBeFalse();
        row.Name.ShouldBe("Worker A");
        row.ScreenIds.ShouldBe([742]);
        row.WorkCenterIds.ShouldBe([927]);
    }

    [Fact]
    public async Task Edit_ExistingWorker_DetailsCannotBeEdited()
    {
        var result = await Run(new WorkerEdit("10001", Fields(), null));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Description.ShouldContain("already has a MANTI user");
        _output.DidNotReceiveWithAnyArgs().WriteSpecAndScript(default!, default!, default!);
    }

    [Fact]
    public async Task Edit_ApplicationNotInTheList_IsRejected()
    {
        var result = await Run(new WorkerEdit("10001", null, null, ScreenIds: [999999]));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Description.ShouldContain("application");
    }

    [Fact]
    public async Task Edit_ForAWorkerNotInTheRequest_IsRejected()
    {
        var result = await Run(new WorkerEdit("99999", null, [927]));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("AccessRequest.InvalidEdit");
    }

    [Fact]
    public async Task Edit_UncheckedExistingWorker_IsNotWritten()
    {
        var result = await Run(new WorkerEdit("10001", null, [927], Include: false));

        result.IsSuccess.ShouldBeTrue(result.Error?.Description);
        result.Value.Script.ShouldContain("-- Worker 10001 was unchecked on the page: not saved.");
        result.Value.Script.ShouldNotContain("@U10001, 927");
    }

    [Fact]
    public async Task Edit_WorkCenterNotInTheList_IsRejectedAndNothingIsSaved()
    {
        var result = await Run(new WorkerEdit("10001", null, [999999]));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("AccessRequest.InvalidEdit");
        _output.DidNotReceiveWithAnyArgs().WriteSpecAndScript(default!, default!, default!);
    }

    [Theory]
    [InlineData(99999, 196)] // unknown department
    [InlineData(31341, 99999)] // unknown group
    public async Task Edit_DepartmentOrGroupNotInTheList_IsRejected(int department, int group)
    {
        var result = await Run(new WorkerEdit("10002", Fields(department: department, group: group), null));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("AccessRequest.InvalidEdit");
    }

    [Fact]
    public async Task Edit_NewUserDetailsForAnExistingUser_IsRejected()
    {
        var result = await Run(new WorkerEdit("10001", Fields(), null));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Description.ShouldContain("already has a MANTI user");
    }

    [Fact]
    public async Task Edit_WorkerNotInTheRequest_IsRejected()
    {
        var result = await Run(new WorkerEdit("77777", null, [926]));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Description.ShouldContain("not in this request");
    }

    [Fact]
    public async Task Edit_EnglishNameWithoutLetters_IsRejectedByTheBuilder()
    {
        var result = await Run(new WorkerEdit("10002", Fields("דנה", "Cohen"), null));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Description.ShouldContain("English");
        _output.DidNotReceiveWithAnyArgs().WriteSpecAndScript(default!, default!, default!);
    }

    [Fact]
    public async Task Edit_AfterTheScriptWasSaved_IsRefused()
    {
        _output.IsCommitted(Id).Returns(true);

        var result = await Run(new WorkerEdit("10001", null, [927]));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("AccessRequest.AlreadyCommitted");
    }

    [Fact]
    public async Task Edit_WhenTheListsCannotBeRead_Fails()
    {
        _lookups.Get(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<AccessLookupData>.Failure(Error.Failure("AccessRequest.LookupsUnavailable", "down")));

        var result = await Run(new WorkerEdit("10001", null, [926]));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("AccessRequest.LookupsUnavailable");
    }

    [Fact]
    public async Task Get_ReturnsTheWorkersAsEditableRows()
    {
        var result = await new GetAccessSpecHandler(_output, _lookups).Handle(new GetAccessSpecQuery(Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        // worker 10001 already has a MANTI user: a row with a name only; worker 10002 is new
        result.Value.Workers.Count.ShouldBe(2);
        result.Value.Workers[0].UserCode.ShouldBe("10001");
        result.Value.Workers[0].IsNewUser.ShouldBeFalse();
        result.Value.Workers[0].Name.ShouldBe("Worker A");
        result.Value.Workers[1].UserCode.ShouldBe("10002");
        result.Value.Workers[1].IsNewUser.ShouldBeTrue();
        result.Value.Workers[1].NewUser!.UserName.ShouldBe("DanaC");
        result.Value.Workers[1].NewUser!.Password.ShouldBe("10002"); // default: the employee number
        result.Value.Workers[1].NewUser!.DepartmentId.ShouldBe(31341);
    }

    [Fact]
    public async Task Get_ExistingWorker_ListsTheApplicationsAlreadyInTheDatabase()
    {
        var result = await new GetAccessSpecHandler(_output, _lookups).Handle(new GetAccessSpecQuery(Id), CancellationToken.None);

        result.Value.Workers.Single(w => w.UserCode == "10001").ExistingScreenIds.ShouldBe([788, 1712]);
        result.Value.Workers.Single(w => w.UserCode == "10002").ExistingScreenIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_WhenReadingTheExistingApplicationsFails_TheTableStillLoads()
    {
        _lookups.ExistingApplications(Arg.Any<string?>(), Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<long, IReadOnlyList<int>>>.Failure(Error.Failure("AccessRequest.LookupsUnavailable", "x")));

        var result = await new GetAccessSpecHandler(_output, _lookups).Handle(new GetAccessSpecQuery(Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Workers.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Edit_Response_KeepsTheExistingApplicationsToo()
    {
        var result = await Run(new WorkerEdit("10001", null, null, ScreenIds: [742]));

        result.Value.Spec.Workers.Single(w => w.UserCode == "10001").ExistingScreenIds.ShouldBe([788, 1712]);
    }

    [Fact]
    public async Task Get_NoSpec_IsNotFound()
    {
        _output.ReadSpec(Id).Returns((string?)null);

        var result = await new GetAccessSpecHandler(_output, _lookups).Handle(new GetAccessSpecQuery(Id), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    [Theory]
    [InlineData("web-20261105-101500", true)]
    [InlineData("20261105-1015", true)]
    [InlineData("..\\..\\x", false)]
    [InlineData("", false)]
    public void RequestId_OnlyAgentOutputFolderNames(string id, bool valid)
        => new UpdateAccessSpecValidator().Validate(new UpdateAccessSpecCommand(id, [new WorkerEdit("1", null, null)]))
            .Errors.Any(e => e.PropertyName == nameof(UpdateAccessSpecCommand.RequestId)).ShouldBe(!valid);
}
