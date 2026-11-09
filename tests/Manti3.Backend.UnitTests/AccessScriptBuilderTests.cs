using Manti3.Backend.Application.Features.AccessRequests.BuildScript;
using Manti3.Backend.Application.Features.AccessRequests.EditSpec;
using Manti3.Backend.Application.Features.AccessRequests.RunScript;
using Shouldly;

namespace Manti3.Backend.UnitTests;

// נתונים מומצאים בלבד: אין כאן שמות או מספרי עובדים אמיתיים
public class AccessScriptBuilderTests
{
    // עובד קיים (UserId ידוע) + עובד חדש, מרכז עבודה, מסכים מהדוגמה ומסך ברירת מחדל
    private const string FullSpec = """
        {
          "requestId": "web-20261104-100000",
          "example": { "userCode": "10005", "userId": 2412,
                       "screens": [ { "objId": 742, "name": "Screen A" }, { "objId": 788, "name": "Screen B" } ] },
          "workers": [
            { "userCode": "10001", "userId": 5001, "branchId": 1 },
            { "userCode": "10002", "branchId": 1,
              "newUser": { "firstName": "Worker", "lastName": "B", "engFirstName": "Dana", "engLastName": "Shapira",
                           "phone": "3006", "email": "b@example.test", "departmentId": 31341, "groupId": 196 },
              "workCenters": [ { "id": 926, "code": "N1S" } ], "copyScreens": true,
              "defaultScreens": [ { "objId": 1712, "name": "Reports", "controlId": "100000000111111111011111111111" } ] }
          ]
        }
        """;

    private static string Build(string json)
    {
        var result = AccessScriptBuilder.Build(json);
        result.IsSuccess.ShouldBeTrue(result.Error?.Description);
        return result.Value;
    }

    [Fact]
    public void Build_FullSpec_ProducesAScriptTheGuardAllows()
        => AccessScriptGuard.Check(Build(FullSpec)).ShouldBeNull();

    [Fact]
    public void Build_FullSpec_RunsInOneTransactionThatEndsWithRollback()
    {
        var script = Build(FullSpec);

        script.ShouldContain("BEGIN TRAN;");
        script.TrimEnd().ShouldContain("ROLLBACK TRAN;");
        // the closing comment mentions COMMIT, but no statement may
        System.Text.RegularExpressions.Regex.IsMatch(script, @"^\s*COMMIT", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ShouldBeFalse();
        script.ShouldNotContain("\nGO");
    }

    [Fact]
    public void Build_ExistingWorker_IsLeftAloneCompletely_NewWorkerGetsOneStatementPerScreen()
    {
        var script = Build(FullSpec);

        // worker 10001 already has a MANTI user (userId 5001): no work center, no screens, nothing
        script.ShouldNotContain("SELECT 5001");
        script.ShouldNotContain("UserId = 5001");
        script.ShouldContain("-- Worker 10001 already has a MANTI user and nothing is added for this worker.");
        // worker 10002 is new: the screens of the example, one statement each
        script.ShouldContain("WHERE ex.UserId = 2412 AND ex.ObjId = 742");
        script.ShouldContain("WHERE ex.UserId = 2412 AND ex.ObjId = 788");
        System.Text.RegularExpressions.Regex.Matches(script, @"SELECT @U10002,").Count.ShouldBe(5); // 1 work center + 2 screens + 1 default + the Lobby toolbar
    }

    [Fact]
    public void Build_NewWorker_GetsTheLobbyToolbarWithApplication742_InsideTheGuard()
    {
        var script = Build(FullSpec);

        script.ShouldContain("INSERT INTO dbo.User_Toolbars (UserId, BranchId, USER_TBCODE, USER_TBTEXT, USER_B1, USER_OPERCODE1)");
        script.ShouldContain("SELECT @U10002, 1, 1, 'Lobby', '1', 742");
        script.ShouldContain("WHERE t.UserId = @U10002 AND t.USER_TBCODE = 1");
        script.ShouldContain("AS Lobby_present");

        // one toolbar, for the new worker only (the existing worker 10001 gets nothing), and before the END of the guard
        System.Text.RegularExpressions.Regex.Matches(script, "INSERT INTO dbo.User_Toolbars").Count.ShouldBe(1);
        script.IndexOf("INSERT INTO dbo.User_Toolbars", StringComparison.Ordinal)
            .ShouldBeLessThan(script.IndexOf("END", script.IndexOf("IF @Skip10002 = 0", StringComparison.Ordinal), StringComparison.Ordinal));
        AccessScriptGuard.Check(script).ShouldBeNull();
    }

    [Fact]
    public void Build_OnlyExistingWorkers_HaveNoToolbarInsert()
    {
        var script = Build("""{ "workers": [ { "userCode": "10004", "userId": 77 } ] }""");

        script.ShouldNotContain("User_Toolbars");
    }

    [Fact]
    public void Build_OnlyExistingWorkers_ProducesNoWritesAtAll()
    {
        var script = Build("""
            { "workers": [ { "userCode": "10004", "userId": 77, "branchId": 1,
                "certifyExisting": { "groupId": 146, "date": "20261104" } } ] }
            """);

        AccessScriptGuard.HasWrites(script).ShouldBeFalse();
        script.ShouldNotContain("UPDATE");
        script.ShouldNotContain("INSERT");
        AccessScriptGuard.Check(script).ShouldBeNull();
    }

    [Fact]
    public void Build_ExistingWorkerWithOnlyACodeAndAnId_IsAccepted()
    {
        // the agent may write just {userCode, userId} for a worker who already has a user
        var script = Build("""
            { "workers": [ { "userCode": "10004", "userId": 77 },
                           { "userCode": "10005", "branchId": 1,
                             "newUser": { "firstName": "a", "lastName": "b", "engFirstName": "Dana", "engLastName": "Shapira",
                                          "departmentId": 1, "groupId": 196 },
                             "workCenters": [ { "id": 926, "code": "N1S" } ] } ] }
            """);

        script.ShouldContain("-- Worker 10004 already has a MANTI user and nothing is added for this worker.");
        script.ShouldContain("IF @Skip10005 = 0");
        AccessScriptGuard.Check(script).ShouldBeNull();
    }

    [Fact]
    public void Build_NewWorker_EverythingIsInsideAGuardThatSkipsAnExistingUser()
    {
        var script = Build(FullSpec);

        var guard = script.IndexOf("IF @Skip10002 = 0", StringComparison.Ordinal);
        guard.ShouldBeGreaterThan(0);
        var begin = script.IndexOf("BEGIN", guard, StringComparison.Ordinal);
        var end = script.IndexOf("END", begin, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(begin);

        // the user insert, the work center and every screen of worker 10002 sit between BEGIN and END
        var block = script[begin..end];
        block.ShouldContain("INSERT INTO dbo.User_Profiles");
        block.ShouldContain("SELECT @U10002, 926");
        block.ShouldContain("ex.ObjId = 742");
        block.ShouldContain("SELECT @U10002, 1, 1712"); // the default screen (branch 1)
    }

    [Fact]
    public void Build_NewWorker_CreatesTheUserWithGroupAndUsesTheVariable()
    {
        var script = Build(FullSpec);

        script.ShouldContain("DECLARE @Skip10002 bit = CASE WHEN EXISTS (SELECT 1 FROM dbo.User_Profiles WHERE UserCode = '10002') THEN 1 ELSE 0 END;");
        script.ShouldContain("IF @Skip10002 = 0");
        script.ShouldContain("('DanaS', '10002', '10002', 196, 31341,");
        script.ShouldContain("N'Worker', N'B', 'Dana', 'Shapira', '3006', 'b@example.test', '', ''");
        script.ShouldContain("DECLARE @U10002 int = (SELECT UserId FROM dbo.User_Profiles WHERE UserCode = '10002');");
        script.ShouldContain("SELECT @U10002, 926");
    }

    [Fact]
    public void Build_DefaultScreen_IsAfterTheCopiedScreensSoTheExamplesVersionWins()
    {
        var script = Build(FullSpec);

        script.IndexOf("ex.ObjId = 788", StringComparison.Ordinal)
            .ShouldBeLessThan(script.IndexOf("default screen 1712", StringComparison.Ordinal));
        script.ShouldContain("STUFF(REPLACE(CONVERT(char(30), '100000000111111111011111111111'), ' ', '0'), 30, 1, '1')");
    }

    [Fact]
    public void Build_EndsWithACheckSelectForAllWorkers()
    {
        var script = Build(FullSpec);

        script.ShouldContain("WHERE u.UserCode IN ('10001', '10002')");
        script.ShouldContain("u.UserId AND w.WorkCtrId IN (926)");
        script.ShouldContain("a.ObjId IN (742, 788, 1712)");
    }

    [Fact]
    public void Build_CertifiedNewWorker_AddsCertificationColumns()
    {
        var script = Build("""
            { "workers": [ { "userCode": "10003", "branchId": 1,
                "newUser": { "firstName": "W", "lastName": "C", "engFirstName": "Wen", "engLastName": "Cohen",
                             "departmentId": 31086, "groupId": 146, "certificationDate": "20261104" } } ] }
            """);

        script.ShouldContain("IsCertified, CertificationDate");
        script.ShouldContain("146");
        script.ShouldContain("1, '20261104'");
        AccessScriptGuard.Check(script).ShouldBeNull();
    }

    [Fact]
    public void Build_NewUser_UserNameIsEnglishFirstNamePlusOneLetterOfLastName_PasswordStaysTheEmployeeNumber()
    {
        var script = Build(FullSpec);

        script.ShouldContain("('DanaS', '10002', '10002', 196, 31341,");
        script.ShouldContain("EngFirstName, EngLastName");
        script.ShouldContain("UserBarCode, DateLastModified");
        script.ShouldContain("'', '', GETDATE()");
        script.ShouldContain("'Dana', 'Shapira'");
        script.ShouldContain("user name DanaS,");
    }

    [Fact]
    public void Build_WithResolvedNames_UsesThemInsteadOfTheFirstLetterName()
    {
        var result = AccessScriptBuilder.Build(FullSpec, new Dictionary<string, string> { ["10002"] = "DanaShap" });

        result.IsSuccess.ShouldBeTrue(result.Error?.Description);
        result.Value.ShouldContain("('DanaShap', '10002', '10002', 196, 31341,");
    }

    [Fact]
    public void ResolveUserNames_TwoNewUsersWithTheSameNames_GetDifferentOnes_AndTypedNamesCountAsUsed()
    {
        var spec = """
            { "workers": [
              { "userCode": "10001", "branchId": 1, "newUser": { "firstName": "a", "lastName": "b", "engFirstName": "Dana", "engLastName": "Cohen", "departmentId": 1, "groupId": 196 } },
              { "userCode": "10002", "branchId": 1, "newUser": { "firstName": "a", "lastName": "b", "engFirstName": "Dana", "engLastName": "Cohen", "departmentId": 1, "groupId": 196 } },
              { "userCode": "10003", "branchId": 1, "newUser": { "firstName": "a", "lastName": "b", "engFirstName": "Dana", "engLastName": "Cohen", "departmentId": 1, "groupId": 196, "userName": "DanaCoh" } },
              { "userCode": "10004", "branchId": 1, "skip": true, "newUser": { "firstName": "a", "lastName": "b", "engFirstName": "Dana", "engLastName": "Cohen", "departmentId": 1, "groupId": 196 } }
            ] }
            """;

        var names = AccessScriptBuilder.ResolveUserNames(spec, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DanaC" });

        names["10001"].ShouldBe("DanaCo");
        names["10002"].ShouldBe("DanaCohe"); // DanaC taken in the DB, DanaCo taken by 10001, DanaCoh typed for 10003
        names.ContainsKey("10003").ShouldBeFalse(); // typed: nothing to compute
        names.ContainsKey("10004").ShouldBeFalse(); // unchecked: not saved
    }

    [Fact]
    public void CandidateUserNames_ListsEveryNameToCheckAgainstTheDatabase()
    {
        var names = AccessScriptBuilder.CandidateUserNames(FullSpec);

        names.ShouldBe(["DanaS", "DanaSh", "DanaSha", "DanaShap", "DanaShapi", "DanaShapir", "DanaShapira"]);
    }

    [Fact]
    public void Build_HostileEnglishNames_AreReducedToLetters()
    {
        var script = Build("""
            { "workers": [ { "userCode": "10006", "branchId": 1,
                "newUser": { "firstName": "a", "lastName": "b", "engFirstName": "Moe'; DROP x;--", "engLastName": "Lee/*",
                             "departmentId": 1, "groupId": 196 } } ] }
            """);

        // only letters survive: no quote, semicolon or comment marker from the English names reaches the script
        script.ShouldContain("'Moe Drop X', 'Lee'");
        script.ShouldContain("('MoeDropXL', '10006'");
        AccessScriptGuard.Check(script).ShouldBeNull();
    }

    [Theory]
    [InlineData("""{ "workers": [ { "userCode": "10001", "branchId": 1, "newUser": { "firstName": "a", "lastName": "b", "departmentId": 1, "groupId": 1 } } ] }""")] // no English names
    [InlineData("""{ "workers": [ { "userCode": "10001", "branchId": 1, "newUser": { "firstName": "a", "lastName": "b", "engFirstName": "דנה", "engLastName": "כהן", "departmentId": 1, "groupId": 1 } } ] }""")] // Hebrew, not English
    [InlineData("""{ "workers": [ { "userCode": "10001", "branchId": 1, "newUser": { "firstName": "a", "lastName": "b", "engFirstName": "ABCDEFGHIJKLMNOP", "engLastName": "Lee", "departmentId": 1, "groupId": 1 } } ] }""")] // > 15
    public void Build_NewUserWithoutAUsableEnglishName_IsRejected(string json)
    {
        var result = AccessScriptBuilder.Build(json);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Description.ShouldContain("English");
    }

    [Fact]
    public void Build_CertifyExistingWorker_IsIgnored_NothingIsDoneForAnExistingUser()
    {
        var script = Build("""
            { "workers": [ { "userCode": "10004", "userId": 77, "branchId": 1,
                "certifyExisting": { "groupId": 146, "date": "20261104" } } ] }
            """);

        script.ShouldNotContain("UPDATE dbo.User_Profiles");
        script.ShouldContain("-- Worker 10004 already has a MANTI user and nothing is added for this worker.");
        AccessScriptGuard.Check(script).ShouldBeNull();
    }

    private const string ExistingWithAdditions = """
        { "requestId": "web-20261108-100000",
          "workers": [ { "userCode": "104", "userId": 77, "branchId": 3, "name": "Worker C",
              "workCenters": [ { "id": 926, "code": "N1S" } ],
              "addScreens": [ { "objId": 742, "name": "Screen A", "controlId": "100000000000000000000000000000" } ] } ] }
        """;

    [Fact]
    public void Build_ExistingWorker_GetsOnlyMissingWorkCentersAndApplications_ThreeDigitCodeIsFine()
    {
        var script = Build(ExistingWithAdditions);

        script.ShouldContain("DECLARE @U104 int = (SELECT UserId FROM dbo.User_Profiles WHERE UserCode = '104');");
        script.ShouldContain("IF @U104 IS NOT NULL");
        script.ShouldContain("SELECT @U104, 926");
        script.ShouldContain("WHERE NOT EXISTS (SELECT 1 FROM dbo.Authorisations_WorkCtrs WHERE UserId = @U104 AND WorkCtrId = 926);");
        script.ShouldContain("SELECT @U104, 3, 742,");
        script.ShouldContain("WHERE t.UserId = @U104 AND t.ObjId = 742");
        AccessScriptGuard.Check(script).ShouldBeNull();
        AccessScriptGuard.HasWrites(script).ShouldBeTrue();
    }

    [Fact]
    public void Build_ExistingWorker_NeverCreatesAUserChangesDetailsOrToolbarsAndNeverDeletes()
    {
        var script = Build(ExistingWithAdditions);

        script.ShouldNotContain("INSERT INTO dbo.User_Profiles");
        script.ShouldNotContain("UPDATE");
        script.ShouldNotContain("DELETE");
        script.ShouldNotContain("User_Toolbars");
    }

    [Fact]
    public void Build_UncheckedExistingWorker_IsNotWritten()
    {
        var script = Build(ExistingWithAdditions.Replace("\"userId\": 77,", "\"userId\": 77, \"skip\": true,"));

        script.ShouldContain("-- Worker 104 was unchecked on the page: not saved.");
        script.ShouldNotContain("INSERT");
        AccessScriptGuard.HasWrites(script).ShouldBeFalse();
    }

    [Fact]
    public void Build_WorkerWhoseCertificationGateFailed_ShowsAsUncheckedAndIsNotWritten()
    {
        // the agent writes the known data with skip = true and no work centers: the row is shown, nothing is saved
        var spec = """
            { "workers": [ { "userCode": "10009", "branchId": 1, "skip": true,
                "newUser": { "firstName": "Worker", "lastName": "Z", "departmentId": 31341, "groupId": 196 } } ] }
            """;

        var script = Build(spec);

        script.ShouldContain("-- Worker 10009 was unchecked on the page: not saved.");
        AccessScriptGuard.HasWrites(script).ShouldBeFalse();
        var view = SpecMapper.ToView(AccessScriptBuilder.Parse(spec)!);
        view.Workers.Single().Included.ShouldBeFalse();
        view.Workers.Single().IsNewUser.ShouldBeTrue();
    }

    [Fact]
    public void Build_ExistingWorkerAddingAnApplicationWithoutABranch_IsRejected()
    {
        var result = AccessScriptBuilder.Build(ExistingWithAdditions.Replace("\"branchId\": 3,", string.Empty));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Description.ShouldContain("branchId");
    }

    [Fact]
    public void Build_ExistingWorkerWithABadApplicationBitString_IsRejected()
    {
        var result = AccessScriptBuilder.Build(ExistingWithAdditions.Replace("100000000000000000000000000000", "1'; DROP TABLE x;--"));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Description.ShouldContain("addScreens");
    }

    [Fact]
    public void Build_HostileNames_StayInsideTheirLiteralAndDoNotBreakTheGuard()
    {
        var script = Build("""
            { "workers": [ { "userCode": "10006", "branchId": 1,
                "newUser": { "firstName": "O'Ne -- x", "lastName": "a/*b", "engFirstName": "Moe'; DROP x;--", "engLastName": "Lee/*",
                             "departmentId": 1, "groupId": 196 } } ] }
            """);

        script.ShouldContain("N'O''Ne -- x'");
        script.ShouldContain("N'a/*b'");
        AccessScriptGuard.Check(script).ShouldBeNull();
    }

    [Theory]
    [InlineData("""{ "workers": [] }""")]
    [InlineData("""{ "workers": [ { "userCode": "10001; DROP TABLE x", "userId": 1, "branchId": 1 } ] }""")]
    [InlineData("""{ "workers": [ { "userCode": "10001", "branchId": 1 } ] }""")]                       // neither userId nor newUser
    [InlineData("""{ "workers": [ { "userCode": "10001", "userId": 1, "branchId": 1, "newUser": { "firstName": "a", "lastName": "b", "departmentId": 1, "groupId": 1 } } ] }""")] // both
    [InlineData("""{ "workers": [ { "userCode": "10001", "branchId": 1, "newUser": { "firstName": "a", "lastName": "b", "engFirstName": "Dana", "engLastName": "Shapira", "departmentId": 1, "groupId": 1 }, "copyScreens": true } ] }""")] // copy without example
    [InlineData("""{ "workers": [ { "userCode": "10001", "branchId": 1, "newUser": { "firstName": "ABCDEFGHIJKLMNOP", "lastName": "b", "departmentId": 1, "groupId": 1 } } ] }""")] // name > 15
    [InlineData("""{ "workers": [ { "userCode": "10001", "branchId": 1, "newUser": { "firstName": "a", "lastName": "b", "phone": "123456789", "departmentId": 1, "groupId": 1 } } ] }""")] // phone > 8
    [InlineData("""{ "workers": [ { "userCode": "10001", "branchId": 1, "newUser": { "firstName": "a", "lastName": "b", "engFirstName": "Dana", "engLastName": "Shapira", "departmentId": 1, "groupId": 1 }, "defaultScreens": [ { "objId": 1, "controlId": "1'; DROP" } ] } ] }""")] // bad bits
    [InlineData("not json")]
    public void Build_InvalidSpec_IsRejectedWithAReason(string json)
    {
        var result = AccessScriptBuilder.Build(json);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Description.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Build_RejectionReason_NeverContainsTheValueThatWasWrong()
    {
        var result = AccessScriptBuilder.Build(
            """{ "workers": [ { "userCode": "SECRET-12345; DROP", "userId": 1, "branchId": 1 } ] }""");

        result.Error!.Description.ShouldNotContain("SECRET");
    }
}
