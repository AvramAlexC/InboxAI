using FluentAssertions;
using Wismo.Api.Couriers;

namespace Wismo.Api.Tests.Couriers;

public class AwbReferenceParserTests
{
    [Theory]
    [InlineData("SAMEDAY:AWB12345", "SAMEDAY", "AWB12345")]
    [InlineData("FAN:FAN9876", "FAN", "FAN9876")]
    [InlineData("CARGUS:CRG555", "CARGUS", "CRG555")]
    public void TryParse_ColonSeparator_ExtractsCourierAndAwb(string reference, string expectedCourier, string expectedAwb)
    {
        var result = AwbReferenceParser.TryParse(reference, out var courier, out var awb);

        result.Should().BeTrue();
        courier.Should().Be(expectedCourier);
        awb.Should().Be(expectedAwb);
    }

    [Theory]
    [InlineData("SAMEDAY|AWB12345", "SAMEDAY", "AWB12345")]
    [InlineData("FAN|FAN9876", "FAN", "FAN9876")]
    [InlineData("CARGUS|CRG555", "CARGUS", "CRG555")]
    public void TryParse_PipeSeparator_ExtractsCourierAndAwb(string reference, string expectedCourier, string expectedAwb)
    {
        var result = AwbReferenceParser.TryParse(reference, out var courier, out var awb);

        result.Should().BeTrue();
        courier.Should().Be(expectedCourier);
        awb.Should().Be(expectedAwb);
    }

    [Theory]
    [InlineData("SAMEDAY-AWB12345", "SAMEDAY", "AWB12345")]
    [InlineData("FAN-FAN9876", "FAN", "FAN9876")]
    [InlineData("CARGUS-CRG555", "CARGUS", "CRG555")]
    public void TryParse_DashSeparator_ExtractsCourierAndAwb(string reference, string expectedCourier, string expectedAwb)
    {
        var result = AwbReferenceParser.TryParse(reference, out var courier, out var awb);

        result.Should().BeTrue();
        courier.Should().Be(expectedCourier);
        awb.Should().Be(expectedAwb);
    }

    [Theory]
    [InlineData("smd:AWB111", "SAMEDAY", "AWB111")]
    [InlineData("smdy:AWB222", "SAMEDAY", "AWB222")]
    [InlineData("sameday:AWB333", "SAMEDAY", "AWB333")]
    public void TryParse_SamedayAliases_NormalizedToSameday(string reference, string expectedCourier, string expectedAwb)
    {
        var result = AwbReferenceParser.TryParse(reference, out var courier, out var awb);

        result.Should().BeTrue();
        courier.Should().Be(expectedCourier);
        awb.Should().Be(expectedAwb);
    }

    [Theory]
    [InlineData("fan:FAN111", "FAN", "FAN111")]
    [InlineData("fancourier:FAN222", "FAN", "FAN222")]
    public void TryParse_FanAliases_NormalizedToFan(string reference, string expectedCourier, string expectedAwb)
    {
        var result = AwbReferenceParser.TryParse(reference, out var courier, out var awb);

        result.Should().BeTrue();
        courier.Should().Be(expectedCourier);
        awb.Should().Be(expectedAwb);
    }

    [Theory]
    [InlineData("cargus:CRG111", "CARGUS", "CRG111")]
    [InlineData("urgentcargus:CRG222", "CARGUS", "CRG222")]
    public void TryParse_CargusAliases_NormalizedToCargus(string reference, string expectedCourier, string expectedAwb)
    {
        var result = AwbReferenceParser.TryParse(reference, out var courier, out var awb);

        result.Should().BeTrue();
        courier.Should().Be(expectedCourier);
        awb.Should().Be(expectedAwb);
    }

    [Theory]
    [InlineData("  SAMEDAY : AWB999  ", "SAMEDAY", "AWB999")]
    [InlineData("  FAN - FAN888  ", "FAN", "FAN888")]
    public void TryParse_WhitespaceAroundParts_TrimsCorrectly(string reference, string expectedCourier, string expectedAwb)
    {
        var result = AwbReferenceParser.TryParse(reference, out var courier, out var awb);

        result.Should().BeTrue();
        courier.Should().Be(expectedCourier);
        awb.Should().Be(expectedAwb);
    }

    [Theory]
    [InlineData("same_day:AWB111", "SAMEDAY", "AWB111")]
    [InlineData("urgent-cargus:CRG111", "CARGUS", "CRG111")]
    [InlineData("fan courier:FAN111", "FAN", "FAN111")]
    public void TryParse_CourierWithSpecialChars_NormalizesCorrectly(string reference, string expectedCourier, string expectedAwb)
    {
        var result = AwbReferenceParser.TryParse(reference, out var courier, out var awb);

        result.Should().BeTrue();
        courier.Should().Be(expectedCourier);
        awb.Should().Be(expectedAwb);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_NullOrWhitespace_ReturnsFalse(string? reference)
    {
        var result = AwbReferenceParser.TryParse(reference!, out var courier, out var awb);

        result.Should().BeFalse();
        courier.Should().BeEmpty();
        awb.Should().BeEmpty();
    }

    [Theory]
    [InlineData("UNKNOWN:AWB123")]
    [InlineData("DHL:12345")]
    [InlineData("UPS-TRACK999")]
    public void TryParse_UnknownCourier_ReturnsFalse(string reference)
    {
        var result = AwbReferenceParser.TryParse(reference, out var courier, out var awb);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("SAMEDAY:")]
    [InlineData("SAMEDAY:   ")]
    [InlineData("FAN|")]
    public void TryParse_EmptyAwb_ReturnsFalse(string reference)
    {
        var result = AwbReferenceParser.TryParse(reference, out var courier, out var awb);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("justtext")]
    [InlineData("12345")]
    [InlineData("no-separator-here-at-all")]
    public void TryParse_NoValidFormat_ReturnsFalse(string reference)
    {
        var result = AwbReferenceParser.TryParse(reference, out var courier, out var awb);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryParse_DashSeparatorWithEmptyAwb_ReturnsFalse()
    {
        var result = AwbReferenceParser.TryParse("FAN-", out var courier, out var awb);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryParse_ColonAtStart_ReturnsFalse()
    {
        var result = AwbReferenceParser.TryParse(":AWB123", out var courier, out var awb);

        result.Should().BeFalse();
    }
}
