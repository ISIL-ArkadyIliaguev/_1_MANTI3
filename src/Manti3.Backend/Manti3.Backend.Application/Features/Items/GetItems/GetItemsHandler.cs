using Manti3.Backend.Application.Abstractions;

namespace Manti3.Backend.Application.Features.Items.GetItems;

internal sealed class GetItemsHandler(IItemsReader reader) : IQueryHandler<GetItemsQuery, IReadOnlyList<string>>
{
    public async Task<Result<IReadOnlyList<string>>> Handle(GetItemsQuery query, CancellationToken ct)
    {
        var codes = await reader.ReadItemCodes(ct);

        IReadOnlyList<string> items = codes
            .Select(code => code?.Trim() ?? string.Empty)
            .ToList();

        return Result<IReadOnlyList<string>>.Success(items);
    }
}
