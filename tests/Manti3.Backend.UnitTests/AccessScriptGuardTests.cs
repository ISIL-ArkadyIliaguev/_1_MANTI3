using Manti3.Backend.Application.Features.AccessRequests.RunScript;
using Shouldly;

namespace Manti3.Backend.UnitTests;

// סקריפטים מומצאים בלבד: אין כאן שמות או מספרי עובדים אמיתיים
public class AccessScriptGuardTests
{
    // צורת הסקריפט שהסוכן כותב (script-templates.md): עסקה אחת, INSERT עם NOT EXISTS, בדיקה, ROLLBACK
    private const string TypicalScript = """
        -- Request web-x. Review before running. Generated, not executed.
        BEGIN TRAN;

        -- New user 10001: Worker A
        IF NOT EXISTS (SELECT 1 FROM dbo.User_Profiles WHERE UserCode = '10001')
            INSERT INTO dbo.User_Profiles
                (UserName, UserCode, UserPassword, UserGroupId, DepartmentId, UserFirstName, UserLastName)
            VALUES ('10001', '10001', '10001', 196, 30000, N'עובד', N'א');
        DECLARE @U10001 int = (SELECT UserId FROM dbo.User_Profiles WHERE UserCode = '10001');

        INSERT INTO dbo.Authorisations_WorkCtrs (UserId, WorkCtrId)
        SELECT @U10001, 926
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Authorisations_WorkCtrs WHERE UserId = @U10001 AND WorkCtrId = 926);

        INSERT INTO dbo.Authorizations (UserId, BranchId, ObjId, ControlId)
        SELECT @U10001, 1, 1712, '100000000111111111011111111111'
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Authorizations t WHERE t.UserId = @U10001 AND t.ObjId = 1712);

        SELECT u.UserCode FROM dbo.User_Profiles u WHERE u.UserCode IN ('10001');

        ROLLBACK TRAN;
        -- To apply: change the ROLLBACK TRAN above to COMMIT TRAN after reviewing the check result.
        """;

    [Fact]
    public void Check_TypicalAgentScript_IsAllowed()
        => AccessScriptGuard.Check(TypicalScript).ShouldBeNull();

    [Fact]
    public void Check_CertifiedWorkerUpdate_IsAllowed()
        => AccessScriptGuard.Check("""
            BEGIN TRAN;
            UPDATE dbo.User_Profiles
               SET UserGroupId = CASE WHEN UserGroupId = 196 THEN 146 ELSE UserGroupId END,
                   IsCertified = 1,
                   CertificationDate = '20261104'
             WHERE UserCode = '10001';
            ROLLBACK TRAN;
            """).ShouldBeNull();

    [Fact]
    public void Check_CertifiedWorkerUpdateWithDateLastModified_IsAllowed()
        => AccessScriptGuard.Check("""
            BEGIN TRAN;
            UPDATE dbo.User_Profiles
               SET UserGroupId = CASE WHEN UserGroupId = 196 THEN 146 ELSE UserGroupId END,
                   IsCertified = 1,
                   CertificationDate = '20261104',
                   DateLastModified = GETDATE()
             WHERE UserCode = '10001';
            ROLLBACK TRAN;
            """).ShouldBeNull();

    [Theory]
    [InlineData("DELETE FROM dbo.Authorizations WHERE UserId = 1")]
    [InlineData("DROP TABLE dbo.User_Profiles")]
    [InlineData("TRUNCATE TABLE dbo.Authorizations")]
    [InlineData("ALTER TABLE dbo.User_Profiles ADD x int")]
    [InlineData("EXEC uspAuthorization_WorkCtrs_IUD 1, 2")]
    [InlineData("EXEC('INSERT INTO dbo.Authorizations (UserId) VALUES (1)')")]
    [InlineData("MERGE dbo.Authorizations AS t USING (SELECT 1 AS a) s ON 1=0 WHEN NOT MATCHED THEN INSERT (UserId) VALUES (1);")]
    [InlineData("SELECT * INTO dbo.Copy FROM dbo.User_Profiles")]
    [InlineData("GRANT SELECT ON dbo.User_Profiles TO public")]
    [InlineData("SELECT 1; GO")]
    [InlineData("USE master")]
    [InlineData("EXEC xp_cmdshell 'dir'")]
    public void Check_ForbiddenStatement_IsRejected(string sql)
        => AccessScriptGuard.Check(sql).ShouldNotBeNull();

    [Theory]
    [InlineData("INSERT INTO dbo.Users_Other (a) VALUES (1)")]
    [InlineData("INSERT INTO Manti_IscarDeleted..User_Profiles (a) VALUES (1)")]
    [InlineData("INSERT INTO SQLCLN1.prs_IscarData.dbo.PRS_WORKERS_V6 (a) VALUES (1)")]
    [InlineData("UPDATE dbo.Authorizations SET ControlId = '0' WHERE UserId = 1")]
    [InlineData("UPDATE dbo.User_Profiles SET UserPassword = 'x' WHERE UserCode = '1'")]
    [InlineData("UPDATE dbo.User_Profiles SET IsCertified = 1, UsersStatus = '1' WHERE UserCode = '1'")]
    public void Check_WriteToATableOrColumnOutsideTheAllowList_IsRejected(string sql)
        => AccessScriptGuard.Check(sql).ShouldNotBeNull();

    [Fact]
    public void Check_ForbiddenWordInsideCommentOrString_IsAllowed()
        => AccessScriptGuard.Check("""
            -- we never DELETE or DROP anything here
            /* EXEC is not used */
            SELECT 'DROP TABLE' AS note, N'DELETE' AS note2;
            """).ShouldBeNull();

    [Fact]
    public void Check_CommentMarkerInsideAString_DoesNotHideWhatFollows()
        => AccessScriptGuard.Check("SELECT 'a -- b', 'c'; DROP TABLE dbo.User_Profiles").ShouldNotBeNull();

    [Fact]
    public void Check_BlockCommentMarkerInsideAString_DoesNotHideWhatFollows()
        => AccessScriptGuard.Check("SELECT 'a /* b'; DELETE FROM dbo.Authorizations; SELECT 'c */'").ShouldNotBeNull();

    [Fact]
    public void Check_EscapedQuoteInsideAString_IsHandled()
        => AccessScriptGuard.Check("SELECT N'O''Neil -- DROP', 1").ShouldBeNull();

    [Fact]
    public void Check_TransactionControlInTheMiddle_IsRejected()
        => AccessScriptGuard.Check("BEGIN TRAN;\nSELECT 1;\nCOMMIT TRAN;\nSELECT 2;\nROLLBACK TRAN;")
            .ShouldNotBeNull();

    [Fact]
    public void Check_EmptyScript_IsRejected()
        => AccessScriptGuard.Check("  \n -- nothing\n").ShouldNotBeNull();

    [Fact]
    public void Prepare_RemovesTheScriptsOwnTransactionLines()
    {
        var body = AccessScriptGuard.Prepare(TypicalScript);

        body.ShouldNotContain("BEGIN TRAN;");
        body.ShouldNotContain("ROLLBACK TRAN;");
        body.ShouldContain("INSERT INTO dbo.Authorisations_WorkCtrs");
    }

    [Theory]
    [InlineData("COMMIT TRAN;")]
    [InlineData("rollback transaction")]
    public void Prepare_RemovesEitherEnding(string ending)
        => AccessScriptGuard.Prepare("BEGIN TRANSACTION\nSELECT 1;\n" + ending).ShouldNotContain("TRAN");

    [Fact]
    public void HasWrites_ScriptWithInsert_IsTrue()
        => AccessScriptGuard.HasWrites(TypicalScript).ShouldBeTrue();

    [Fact]
    public void HasWrites_VerificationSelectsOnly_IsFalse()
        => AccessScriptGuard.HasWrites("-- INSERT is not here\nSELECT UserId FROM dbo.User_Profiles;").ShouldBeFalse();
}
