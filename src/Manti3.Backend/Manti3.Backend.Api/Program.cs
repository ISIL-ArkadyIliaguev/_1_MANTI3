using Manti3.Backend.Api.Extensions;
using Manti3.Backend.Application;
using Manti3.Backend.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog הוא ה-API היחיד ללוגים
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpoints(typeof(Program).Assembly);
builder.Services.AddSingleton<Manti3.Backend.Api.Endpoints.AccessRequests.AccessRequestJobs>(); // ריצות סוכן ברקע (Outlook)

// Liveness בודק רק שהתהליך עונה. תלויות (DB) נבדקות ב-readiness/startup בלבד.
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["live"]);

// CORS: ה-Frontend רץ על פורט אחר
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors();

// בלי בדיקות health בלוג הבקשות
app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
    ex is null && ctx.Request.Path.StartsWithSegments("/health")
        ? Serilog.Events.LogEventLevel.Verbose
        : Serilog.Events.LogEventLevel.Information);

app.MapEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });
app.MapHealthChecks("/health/startup", new HealthCheckOptions { Predicate = r => r.Tags.Contains("startup") });

app.Run();

// נחשף לבדיקות אינטגרציה (WebApplicationFactory)
public partial class Program;
