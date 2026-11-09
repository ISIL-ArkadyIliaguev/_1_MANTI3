using Manti3.Backend.Api.Extensions;
using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Application.Features.AccessRequests.EditSpec;

namespace Manti3.Backend.Api.Endpoints.AccessRequests;

// טבלת העריכה בדף: מה שהסוכן הכין לכל עובד (פרטי משתמש חדש, מרכזי עבודה), שמירת העריכות של האדם ובנייה מחדש של
// הסקריפט, והרשימות לתפריטי הבחירה. הדפדפן שולח ערכים מובנים בלבד, לעולם לא SQL
internal sealed class EditSpecEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        // כמו שאר נקודות הקצה של הסוכן: אין עדיין הזדהות (פער ידוע), לכן רק בסביבת פיתוח על המחשב המקומי
        if (!app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            return;

        app.MapGet("/api/access-requests/{requestId}/spec", async (
                string requestId, IQueryHandler<GetAccessSpecQuery, SpecView> handler, CancellationToken ct) =>
            (await handler.Handle(new GetAccessSpecQuery(requestId), ct)).ToHttpResult())
            .WithName("GetAccessSpec")
            .WithTags("AccessRequests");

        app.MapPut("/api/access-requests/{requestId}/spec", async (
                string requestId, UpdateAccessSpecBody body,
                ICommandHandler<UpdateAccessSpecCommand, UpdatedSpec> handler, CancellationToken ct) =>
            (await handler.Handle(new UpdateAccessSpecCommand(requestId, body.Workers ?? []), ct)).ToHttpResult())
            .WithName("UpdateAccessSpec")
            .WithTags("AccessRequests");

        app.MapGet("/api/access-requests/lookups", async (
                string? server, IQueryHandler<GetAccessLookupsQuery, AccessLookupData> handler, CancellationToken ct) =>
            (await handler.Handle(new GetAccessLookupsQuery(server), ct)).ToHttpResult())
            .WithName("GetAccessLookups")
            .WithTags("AccessRequests");
    }
}

internal sealed record UpdateAccessSpecBody(List<WorkerEdit>? Workers);
