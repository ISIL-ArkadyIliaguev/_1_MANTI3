using Manti3.Backend.Api.Extensions;
using Manti3.Backend.Application.Features.AccessRequests.ProcessRequest;

namespace Manti3.Backend.Api.Endpoints.AccessRequests;

// POST /api/access-requests/jobs: מתחיל ריצת סוכן ברקע ומחזיר מיד jobId (202). הגוף זהה ל-POST /api/access-requests.
// כך התוסף של Outlook שולח את המייל הפתוח ופותח את הדף הקופץ על הריצה, בלי שלב של הוספת קבצים
internal sealed class StartAccessRequestJobEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        if (!app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            return;

        // ?id=<32 hex> (אופציונלי): ה-id שהתוסף כבר פתח איתו את החלון הקופץ
        app.MapPost("/api/access-requests/jobs", (ProcessAccessRequestBody body, AccessRequestJobs jobs, IAgentServers servers, string? id) =>
            {
                if (servers.Find(body.Server) is not { Ready: true })
                    return ProcessAccessRequestEndpoint.UnknownServer();

                if (id is null)
                {
                    var newId = jobs.Start(body.ToCommand());
                    return Results.Accepted($"/api/access-requests/jobs/{newId}", new { jobId = newId });
                }

                return jobs.TryStart(body.ToCommand(), id) switch
                {
                    StartOutcome.Started => Results.Accepted($"/api/access-requests/jobs/{id}", new { jobId = id }),
                    StartOutcome.AlreadyExists => Results.Conflict(),
                    _ => Results.BadRequest()
                };
            })
            .WithName("StartAccessRequestJob")
            .WithTags("AccessRequests");
    }
}

// GET /api/access-requests/jobs/{jobId}: סטטוס (running | done | failed), ובסיום התוצאה כמו בנקודת הקצה הרגילה
internal sealed class GetAccessRequestJobEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        if (!app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            return;

        app.MapGet("/api/access-requests/jobs/{jobId}", (string jobId, AccessRequestJobs jobs) =>
                jobs.Get(jobId) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound())
            .WithName("GetAccessRequestJob")
            .WithTags("AccessRequests");
    }
}
