using FluentAssertions;
using Wismo.Api.Couriers;

namespace Wismo.Api.Tests.Couriers;

public class AwbStatusMapperTests
{
    [Theory]
    [InlineData("delivered")]
    [InlineData("Livrat")]
    [InlineData("Colet predat destinatarului")]
    [InlineData("DELIVERED")]
    public void MapToInternalStatus_DeliveredKeywords_ReturnsDelivered(string externalStatus)
    {
        AwbStatusMapper.MapToInternalStatus(externalStatus).Should().Be("Delivered");
    }

    [Theory]
    [InlineData("returned")]
    [InlineData("Retur la expeditor")]
    [InlineData("returnat")]
    [InlineData("RETURNED")]
    public void MapToInternalStatus_ReturnedKeywords_ReturnsReturned(string externalStatus)
    {
        AwbStatusMapper.MapToInternalStatus(externalStatus).Should().Be("Returned");
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("Delivery exception")]
    [InlineData("esuat")]
    [InlineData("nereusit")]
    [InlineData("anulat")]
    [InlineData("Comanda cancelled")]
    public void MapToInternalStatus_DeliveryIssueKeywords_ReturnsDeliveryIssue(string externalStatus)
    {
        AwbStatusMapper.MapToInternalStatus(externalStatus).Should().Be("DeliveryIssue");
    }

    [Theory]
    [InlineData("in transit")]
    [InlineData("picked up")]
    [InlineData("processing")]
    [InlineData("some unknown status")]
    public void MapToInternalStatus_UnknownKeywords_ReturnsInTransit(string externalStatus)
    {
        AwbStatusMapper.MapToInternalStatus(externalStatus).Should().Be("InTransit");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MapToInternalStatus_NullOrWhitespace_ReturnsInTransit(string? externalStatus)
    {
        AwbStatusMapper.MapToInternalStatus(externalStatus!).Should().Be("InTransit");
    }

    [Fact]
    public void MapToInternalStatus_WhitespaceAroundKeyword_StillMatches()
    {
        AwbStatusMapper.MapToInternalStatus("  delivered  ").Should().Be("Delivered");
    }
}
