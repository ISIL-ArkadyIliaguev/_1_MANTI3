using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;
using Manti3.Backend.Application.Features.AccessRequests.RunScript;
using Manti3.Backend.Infrastructure.Agent;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Shouldly;

namespace Manti3.Backend.UnitTests;

public class AgentServersTests
{
    private static readonly ScriptRunnerOptions Runner = new("Server=SQL25DEV;Integrated Security=True", "SQL25DEV");

    private static IConfiguration Config(params (string Name, string DataSource)[] servers)
        => new ConfigurationBuilder().AddInMemoryCollection(
            servers.SelectMany((s, i) => new[]
            {
                KeyValuePair.Create($"Agent:Servers:{i}:Name", (string?)s.Name),
                KeyValuePair.Create($"Agent:Servers:{i}:DataSource", (string?)s.DataSource),
            })).Build();

    private static AgentServers Two()
        => new(Config(("SQL25DEV", "SQL25DEV"), ("SQL2025CLN2", "SQL2025CLN2.iscar.com,2433")), Runner);

    [Fact]
    public void NoSettings_OnlyTheScriptServer_AsDefault()
    {
        var servers = new AgentServers(Config(), Runner);

        servers.All.Count.ShouldBe(1);
        servers.Default.Name.ShouldBe("SQL25DEV");
        servers.Default.CanRunScripts.ShouldBeTrue();
    }

    [Fact]
    public void Servers_FirstIsDefault_AndOnlyTheScriptServerCanRunScripts()
    {
        var servers = Two();

        servers.Default.Name.ShouldBe("SQL25DEV");
        servers.Find("SQL25DEV")!.CanRunScripts.ShouldBeTrue();
        servers.Find("SQL2025CLN2")!.CanRunScripts.ShouldBeFalse();
        servers.Find("SQL2025CLN2")!.DataSource.ShouldBe("SQL2025CLN2.iscar.com,2433");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Find_NoName_GivesTheDefault(string? name) => Two().Find(name)!.Name.ShouldBe("SQL25DEV");

    [Theory]
    [InlineData("PROD")]
    [InlineData("SQL2025CLN2.iscar.com,2433")] // a data source is not a name: the browser sends names only
    [InlineData("SQL25DEV;Initial Catalog=x")]
    public void Find_UnknownName_IsNull(string name) => Two().Find(name).ShouldBeNull();

    [Fact]
    public void Find_IsCaseInsensitive() => Two().Find("sql2025cln2")!.Name.ShouldBe("SQL2025CLN2");

    [Theory]
    [InlineData("SQL25DEV", "SQL25DEV")]
    [InlineData("tcp:SQL25DEV,2433", "SQL25DEV")]
    [InlineData("SQL2025CLN2.iscar.com,2433", "SQL2025CLN2.iscar.com")]
    [InlineData(@"HOST\INST", "HOST")]
    public void HostOf_StripsPortInstanceAndProtocol(string dataSource, string host)
        => AgentServers.HostOf(dataSource).ShouldBe(host);

    private static AgentServers WithSqlLogin(params (string Key, string Value)[] secrets)
        => new(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Agent:Servers:0:Name"] = "SQL25DEV",
                ["Agent:Servers:0:DataSource"] = "SQL25DEV",
                ["Agent:Servers:1:Name"] = "SQL2025CLN2",
                ["Agent:Servers:1:DataSource"] = "SQL2025CLN2.iscar.com,2433",
                ["Agent:Servers:1:Auth"] = "Sql",
            }.Concat(secrets.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value)))).Build(), Runner);

    [Fact]
    public void SqlLoginServer_WithoutSecrets_IsListedButNotReady()
    {
        var servers = WithSqlLogin();

        var cln2 = servers.Find("SQL2025CLN2")!;
        cln2.UsesSqlLogin.ShouldBeTrue();
        cln2.Ready.ShouldBeFalse();
        servers.LoginFor(cln2).ShouldBeNull();
        servers.Find("SQL25DEV")!.Ready.ShouldBeTrue();
    }

    [Fact]
    public void SqlLoginServer_WithSecrets_IsReady_AndNeverRunsScripts()
    {
        var servers = WithSqlLogin(
            ("Agent:ServerLogins:SQL2025CLN2:User", "reader"), ("Agent:ServerLogins:SQL2025CLN2:Password", "pw-123"));

        var cln2 = servers.Find("SQL2025CLN2")!;
        cln2.Ready.ShouldBeTrue();
        cln2.CanRunScripts.ShouldBeFalse();
        servers.LoginFor(cln2).ShouldBe(("reader", "pw-123"));
        servers.LoginFor(servers.Default).ShouldBeNull();
        cln2.ToString().ShouldNotContain("pw-123");
        cln2.ToString().ShouldNotContain("reader");
    }

    [Fact]
    public void SqlLoginServer_WithAllowWrite_CanRunScripts_WithAReadWriteConnection()
    {
        var servers = new AgentServers(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Agent:Servers:0:Name"] = "SQL25DEV",
                ["Agent:Servers:0:DataSource"] = "SQL25DEV",
                ["Agent:Servers:1:Name"] = "SQL2025CLN2",
                ["Agent:Servers:1:DataSource"] = "SQL2025CLN2.iscar.com,2433",
                ["Agent:Servers:1:Auth"] = "Sql",
                ["Agent:Servers:1:AllowWrite"] = "True",
                ["Agent:ServerLogins:SQL2025CLN2:User"] = "writer",
                ["Agent:ServerLogins:SQL2025CLN2:Password"] = "pw-123",
            }).Build(), Runner);

        var cln2 = servers.Find("SQL2025CLN2")!;
        cln2.CanRunScripts.ShouldBeTrue();

        var write = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
            servers.ScriptConnectionStringFor(cln2, "t", 5)!);
        write.DataSource.ShouldBe("SQL2025CLN2.iscar.com,2433");
        write.ApplicationIntent.ShouldBe(Microsoft.Data.SqlClient.ApplicationIntent.ReadWrite);
        write.IntegratedSecurity.ShouldBeFalse();
    }

    [Fact]
    public void ScriptConnectionString_ForAReadOnlyServer_IsNull()
    {
        var servers = WithSqlLogin(
            ("Agent:ServerLogins:SQL2025CLN2:User", "reader"), ("Agent:ServerLogins:SQL2025CLN2:Password", "pw-123"));

        servers.ScriptConnectionStringFor(servers.Find("SQL2025CLN2")!, "t", 5).ShouldBeNull();
    }

    [Fact]
    public void SqlLoginServer_EmptyPassword_IsNotReady()
    {
        var servers = WithSqlLogin(
            ("Agent:ServerLogins:SQL2025CLN2:User", "reader"), ("Agent:ServerLogins:SQL2025CLN2:Password", ""));

        servers.Find("SQL2025CLN2")!.Ready.ShouldBeFalse();
    }

    [Fact]
    public async Task RunScript_RequestFromReadOnlyServer_IsRefused()
    {
        var output = Substitute.For<IAgentOutput>();
        output.ReadScript("20261104-1152").Returns("BEGIN TRAN;\nROLLBACK TRAN;");
        output.IsRunBlockedByServer("20261104-1152").Returns(true);
        var executor = Substitute.For<IScriptExecutor>();
        var handler = new RunAccessScriptHandler(output, executor, Substitute.For<IScriptRunLedger>());

        var result = await handler.Handle(new RunAccessScriptCommand("20261104-1152", false), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("AccessRequest.ReadOnlyServer");
        await executor.DidNotReceiveWithAnyArgs().Execute(default!, default, default, default);
    }

    [Fact]
    public void OutputStore_ServerFile_BlocksOnlyReadOnlyServers()
    {
        var root = Path.Combine(Path.GetTempPath(), "manti3-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".agent-output", "20261104-1100"));
            Directory.CreateDirectory(Path.Combine(root, ".agent-output", "20261104-1101"));
            Directory.CreateDirectory(Path.Combine(root, ".agent-output", "20261104-1102"));
            Directory.CreateDirectory(Path.Combine(root, ".agent-output", "20261104-1103"));
            File.WriteAllText(Path.Combine(root, ".agent-output", "20261104-1100", "server.txt"), "SQL2025CLN2");
            File.WriteAllText(Path.Combine(root, ".agent-output", "20261104-1101", "server.txt"), "SQL25DEV");
            File.WriteAllText(Path.Combine(root, ".agent-output", "20261104-1103", "server.txt"), "Removed");
            var store = new AgentOutputStore(new AgentOptions(root, "claude.cmd", 10), Two());

            store.IsRunBlockedByServer("20261104-1100").ShouldBeTrue();  // read-only server
            store.IsRunBlockedByServer("20261104-1101").ShouldBeFalse(); // the script server
            store.IsRunBlockedByServer("20261104-1102").ShouldBeFalse(); // old request, no server.txt
            store.IsRunBlockedByServer("20261104-1103").ShouldBeTrue();  // a server no longer in the list
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
