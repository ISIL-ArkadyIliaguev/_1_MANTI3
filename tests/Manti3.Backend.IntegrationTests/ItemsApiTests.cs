using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Testcontainers.MsSql;

namespace Manti3.Backend.IntegrationTests;

// SQL Server אמיתי בקונטיינר (לא ה-DB של MANTI), דרך ה-pipeline האמיתי. דורש Docker.
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE dbo.HoningPeripheralPage (ItemCode NVARCHAR(50) NULL);";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ResetAsync(params string?[] itemCodes)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM dbo.HoningPeripheralPage;";
        await cmd.ExecuteNonQueryAsync();

        foreach (var code in itemCodes)
        {
            await using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO dbo.HoningPeripheralPage (ItemCode) VALUES (@code);";
            insert.Parameters.AddWithValue("@code", (object?)code ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync();
        }
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

public class ItemsApiTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    private WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["Db:User"] = null,
            }));
        });

    [Fact]
    public async Task GetItems_RowsExist_Returns200WithTrimmedCodes()
    {
        await sql.ResetAsync("8421348        ", "8421132");
        await using var factory = CreateFactory(sql.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/items", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var items = await response.Content.ReadFromJsonAsync<string[]>(TestContext.Current.CancellationToken);
        items.ShouldBe(["8421348", "8421132"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetItems_EmptyTable_Returns200WithEmptyArray()
    {
        await sql.ResetAsync();
        await using var factory = CreateFactory(sql.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/items", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<string[]>(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Live_Always_Returns200EvenWhenDatabaseIsDown()
    {
        await using var factory = CreateFactory("Server=127.0.0.1,1;Database=x;User Id=x;Password=x;Connect Timeout=2;TrustServerCertificate=True");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_DatabaseUp_Returns200()
    {
        await using var factory = CreateFactory(sql.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_DatabaseDown_Returns503()
    {
        await using var factory = CreateFactory("Server=127.0.0.1,1;Database=x;User Id=x;Password=x;Connect Timeout=2;TrustServerCertificate=True");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GetItems_DatabaseDown_Returns500ProblemDetailsWithoutLeakingDetails()
    {
        await using var factory = CreateFactory("Server=127.0.0.1,1;Database=x;User Id=x;Password=x;Connect Timeout=2;TrustServerCertificate=True");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/items", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("\"status\":500");
        body.ShouldNotContain("SqlException");
        body.ShouldNotContain("127.0.0.1");
    }
}
