using Manti3.Backend.Application.Features.Items.GetItems;
using Microsoft.EntityFrameworkCore;

namespace Manti3.Backend.Infrastructure.Persistence;

internal sealed class ItemsReader(AppDbContext db) : IItemsReader
{
    public async Task<IReadOnlyList<string?>> ReadItemCodes(CancellationToken ct)
        => await db.HoningPeripheralItems
            .AsNoTracking()
            .Select(i => i.ItemCode)
            .ToListAsync(ct);
}
