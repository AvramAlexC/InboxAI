using FluentAssertions;
using Wismo.Api.Couriers;

namespace Wismo.Api.Tests.Couriers;

public class CourierResponseParserTests
{
    [Fact]
    public void Parse_FlatObjectWithStatus_ReturnsResult()
    {
        var json = """{"status": "Delivered", "timestamp": "2026-04-01T10:30:00Z"}""";

        var result = CourierResponseParser.Parse(json);

        result.Should().NotBeNull();
        result!.ExternalStatus.Should().Be("Delivered");
        result.EventTime.Should().Be(DateTimeOffset.Parse("2026-04-01T10:30:00Z"));
    }

    [Fact]
    public void Parse_StatusNameKey_ReturnsResult()
    {
        var json = """{"statusName": "In Transit"}""";

        var result = CourierResponseParser.Parse(json);

        result.Should().NotBeNull();
        result!.ExternalStatus.Should().Be("In Transit");
        result.EventTime.Should().BeNull();
    }

    [Theory]
    [InlineData("currentStatus")]
    [InlineData("shipmentStatus")]
    [InlineData("state")]
    [InlineData("description")]
    public void Parse_AllStatusKeyVariants_Recognized(string key)
    {
        var json = $$"""{"{{key}}": "Livrat"}""";

        var result = CourierResponseParser.Parse(json);

        result.Should().NotBeNull();
        result!.ExternalStatus.Should().Be("Livrat");
    }

    [Theory]
    [InlineData("eventTime")]
    [InlineData("timestamp")]
    [InlineData("updatedAt")]
    [InlineData("date")]
    [InlineData("statusDate")]
    public void Parse_AllEventTimeKeyVariants_Recognized(string key)
    {
        var json = $$"""{"status": "OK", "{{key}}": "2026-03-15T14:00:00+02:00"}""";

        var result = CourierResponseParser.Parse(json);

        result.Should().NotBeNull();
        result!.EventTime.Should().NotBeNull();
    }

    [Fact]
    public void Parse_NestedStatus_FoundRecursively()
    {
        var json = """{"data": {"shipment": {"status": "Returnat", "eventTime": "2026-04-02T08:00:00Z"}}}""";

        var result = CourierResponseParser.Parse(json);

        result.Should().NotBeNull();
        result!.ExternalStatus.Should().Be("Returnat");
        result.EventTime.Should().NotBeNull();
    }

    [Fact]
    public void Parse_StatusInsideArray_FoundRecursively()
    {
        var json = """{"events": [{"status": "Picked up"}, {"status": "In Transit"}]}""";

        var result = CourierResponseParser.Parse(json);

        result.Should().NotBeNull();
        result!.ExternalStatus.Should().Be("Picked up");
    }

    [Fact]
    public void Parse_StatusAsNumber_ReturnsTostringValue()
    {
        var json = """{"status": 42}""";

        var result = CourierResponseParser.Parse(json);

        result.Should().NotBeNull();
        result!.ExternalStatus.Should().Be("42");
    }

    [Fact]
    public void Parse_NoStatusKey_ReturnsNull()
    {
        var json = """{"tracking": "ABC123", "info": "no status here"}""";

        var result = CourierResponseParser.Parse(json);

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_StatusIsEmptyString_ReturnsNull()
    {
        var json = """{"status": ""}""";

        var result = CourierResponseParser.Parse(json);

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_InvalidEventTime_EventTimeIsNull()
    {
        var json = """{"status": "Delivered", "timestamp": "not-a-date"}""";

        var result = CourierResponseParser.Parse(json);

        result.Should().NotBeNull();
        result!.ExternalStatus.Should().Be("Delivered");
        result.EventTime.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrWhitespace_ReturnsNull(string? input)
    {
        CourierResponseParser.Parse(input!).Should().BeNull();
    }

    [Fact]
    public void Parse_DeeplyNestedInArrayAndObject_Found()
    {
        var json = """{"response": {"items": [{"detail": {"state": "Esuat"}}]}}""";

        var result = CourierResponseParser.Parse(json);

        result.Should().NotBeNull();
        result!.ExternalStatus.Should().Be("Esuat");
    }

    [Fact]
    public void Parse_EmptyNestedObjects_ReturnsNull()
    {
        var json = """{"data": {"inner": {}}}""";

        var result = CourierResponseParser.Parse(json);

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsNull()
    {
        var json = """{"events": []}""";

        var result = CourierResponseParser.Parse(json);

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_ArrayWithEmptyObjects_ReturnsNull()
    {
        var json = """{"events": [{}, {}]}""";

        var result = CourierResponseParser.Parse(json);

        result.Should().BeNull();
    }
}
