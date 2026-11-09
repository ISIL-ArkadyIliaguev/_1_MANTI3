using Manti3.Backend.Api.Extensions;
using Manti3.Backend.Application.Abstractions;
using Manti3.Backend.Application.Features.Items.GetItems;

namespace Manti3.Backend.Api.Endpoints.Items;

// מחזיר את עמודת ItemCode מכל שורות הטבלה
internal sealed class GetItemsEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
        => app.MapGet("/api/items", async (
                IQueryHandler<GetItemsQuery, IReadOnlyList<string>> handler,
                CancellationToken ct) =>
            (await handler.Handle(new GetItemsQuery(), ct)).ToHttpResult())
            .WithName("GetItems")
            .WithTags("Items");
}
