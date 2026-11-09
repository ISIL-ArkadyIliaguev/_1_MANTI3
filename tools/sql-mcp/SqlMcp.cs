#:package Microsoft.Data.SqlClient@6.0.2
// Read-only SQL MCP server (stdio). Windows authentication by default, no secrets in the repo.
// Env: MANTI_SQL_SERVER (default SQL25DEV), MANTI_SQL_DATABASE (default Manti_IscarData).
// A server with SQL authentication: also MANTI_SQL_USER and MANTI_SQL_PASSWORD, taken from the person's own
// environment variables (see .mcp.json), never written in a file.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

var server = Environment.GetEnvironmentVariable("MANTI_SQL_SERVER") ?? "SQL25DEV";
var database = Environment.GetEnvironmentVariable("MANTI_SQL_DATABASE") ?? "Manti_IscarData";
var sqlUser = Environment.GetEnvironmentVariable("MANTI_SQL_USER");
var sqlPassword = Environment.GetEnvironmentVariable("MANTI_SQL_PASSWORD");
var useSqlLogin = !string.IsNullOrWhiteSpace(sqlUser);
var csb = new SqlConnectionStringBuilder
{
    DataSource = server, InitialCatalog = database, IntegratedSecurity = !useSqlLogin,
    TrustServerCertificate = true, Encrypt = SqlConnectionEncryptOption.Optional,
    ApplicationIntent = ApplicationIntent.ReadOnly, ApplicationName = "manti-sql-mcp", ConnectTimeout = 15
};
if (useSqlLogin)
{
    csb.UserID = sqlUser!;
    csb.Password = sqlPassword ?? string.Empty;
}
var cs = csb.ConnectionString;

const int MaxRows = 500;
var forbidden = new Regex(@"\b(insert|update|delete|merge|drop|alter|truncate|create|exec|execute|grant|revoke|deny|into|backup|restore|shutdown|kill|openrowset|openquery|opendatasource|xp_\w+|sp_\w+)\b", RegexOptions.IgnoreCase);

string? Validate(string sql)
{
    var s = Regex.Replace(sql, @"--[^\r\n]*|/\*.*?\*/", " ", RegexOptions.Singleline).Trim().TrimEnd(';').Trim();
    var noStrings = Regex.Replace(s, @"'(?:[^']|'')*'", "''");
    if (noStrings.Contains(';')) return "Only a single statement is allowed.";
    if (!Regex.IsMatch(noStrings, @"^(select|with)\b", RegexOptions.IgnoreCase)) return "Only SELECT is allowed.";
    var m = forbidden.Match(noStrings);
    return m.Success ? $"Forbidden keyword: {m.Value}" : null;
}

async Task<string> RunQuery(string sql)
{
    var err = Validate(sql);
    if (err != null) throw new InvalidOperationException(err);
    await using var conn = new SqlConnection(cs);
    await conn.OpenAsync();
    await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
    await using var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = 60 };
    await using var r = await cmd.ExecuteReaderAsync();
    var sb = new StringBuilder();
    var cols = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToArray();
    sb.AppendLine(string.Join("\t", cols));
    int n = 0;
    while (await r.ReadAsync())
    {
        if (++n > MaxRows) { sb.AppendLine($"... truncated at {MaxRows} rows"); break; }
        sb.AppendLine(string.Join("\t", Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "NULL" : Convert.ToString(r.GetValue(i))?.Replace('\t', ' ').Replace('\n', ' '))));
    }
    return sb.ToString();
}

JsonNode Tool() => new JsonObject
{
    ["name"] = "query",
    ["description"] = $"Run ONE read-only SELECT against {server}/{database} (Windows auth). Max {MaxRows} rows. Tab-separated output.",
    ["inputSchema"] = new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["sql"] = new JsonObject { ["type"] = "string", ["description"] = "A single SELECT statement" } },
        ["required"] = new JsonArray("sql")
    }
};

var stdin = Console.In;
var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
async Task Send(JsonNode n) => await stdout.WriteLineAsync(n.ToJsonString());

string? line;
while ((line = await stdin.ReadLineAsync()) != null)
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    JsonNode? msg; try { msg = JsonNode.Parse(line); } catch { continue; }
    var id = msg?["id"]; var method = msg?["method"]?.GetValue<string>();
    if (id == null) continue; // notification
    JsonNode? result = null; JsonNode? error = null;
    switch (method)
    {
        case "initialize":
            result = new JsonObject
            {
                ["protocolVersion"] = msg!["params"]?["protocolVersion"]?.GetValue<string>() ?? "2024-11-05",
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "manti-sql", ["version"] = "1.0.0" }
            }; break;
        case "ping": result = new JsonObject(); break;
        case "tools/list": result = new JsonObject { ["tools"] = new JsonArray(Tool()) }; break;
        case "tools/call":
            try
            {
                var sql = msg!["params"]?["arguments"]?["sql"]?.GetValue<string>() ?? "";
                var text = await RunQuery(sql);
                result = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) };
            }
            catch (Exception ex)
            {
                result = new JsonObject { ["isError"] = true, ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = ex.Message }) };
            }
            break;
        default: error = new JsonObject { ["code"] = -32601, ["message"] = "Method not found" }; break;
    }
    var resp = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone() };
    if (error != null) resp["error"] = error; else resp["result"] = result;
    await Send(resp);
}
