using Manti3.Backend.Application.Features.Items.GetItems;
using NSubstitute;
using Shouldly;

namespace Manti3.Backend.UnitTests;

public class GetItemsHandlerTests
{
    private readonly IItemsReader _reader = Substitute.For<IItemsReader>();

    [Fact]
    public async Task Handle_RowsWithPadding_ReturnsTrimmedCodes()
    {
        _reader.ReadItemCodes(Arg.Any<CancellationToken>())
            .Returns(["8421348        ", "  8421132"]);

        var result = await new GetItemsHandler(_reader).Handle(new GetItemsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(["8421348", "8421132"]);
    }

    [Fact]
    public async Task Handle_NullCode_ReturnsEmptyString()
    {
        _reader.ReadItemCodes(Arg.Any<CancellationToken>()).Returns([null]);

        var result = await new GetItemsHandler(_reader).Handle(new GetItemsQuery(), CancellationToken.None);

        result.Value.ShouldBe([string.Empty]);
    }

    [Fact]
    public async Task Handle_EmptyTable_ReturnsEmptyList()
    {
        _reader.ReadItemCodes(Arg.Any<CancellationToken>()).Returns([]);

        var result = await new GetItemsHandler(_reader).Handle(new GetItemsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }
}
