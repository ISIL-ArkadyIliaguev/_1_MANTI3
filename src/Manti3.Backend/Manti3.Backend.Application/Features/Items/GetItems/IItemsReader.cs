namespace Manti3.Backend.Application.Features.Items.GetItems;

// פורט: המימוש ב-Infrastructure (קריאה בלבד מ-DB של MANTI)
public interface IItemsReader
{
    Task<IReadOnlyList<string?>> ReadItemCodes(CancellationToken ct);
}
