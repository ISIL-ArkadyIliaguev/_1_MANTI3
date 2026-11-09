using Manti3.Backend.Application.Abstractions;

namespace Manti3.Backend.Application.Features.Items.GetItems;

public sealed record GetItemsQuery : IQuery<IReadOnlyList<string>>;
