using Manti3.Backend.Api.Extensions;
using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Application.Features.AccessRequests.RunScript;

namespace Manti3.Backend.Api.Endpoints.AccessRequests;

// מריץ את script.sql של ריצה קיימת: Commit=false הרצה לניסיון (מתבטלת), Commit=true שמירה (רק אחרי ניסיון).
// הדפדפן שולח מזהה ריצה בלבד, לא SQL: הסקריפט נקרא מ-.agent-output ועובר בדיקת הגנה לפני ההרצה
internal sealed class RunAccessScriptEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        // כמו נקודת הקצה של הסוכן: אין עדיין הזדהות (פער ידוע), לכן רק בסביבת פיתוח על המחשב המקומי
        if (!app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            return;

        app.MapPost("/api/access-requests/{requestId}/run-script", async (
                string requestId,
                RunAccessScriptBody body,
                ICommandHandler<RunAccessScriptCommand, ScriptRunResult> handler,
                CancellationToken ct) =>
            (await handler.Handle(new RunAccessScriptCommand(requestId, body.Commit), ct)).ToHttpResult())
            .WithName("RunAccessScript")
            .WithTags("AccessRequests");
    }
}

internal sealed record RunAccessScriptBody(bool Commit);
