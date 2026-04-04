using FluentAssertions;
using Wismo.Api.Auth;

namespace Wismo.Api.Tests.Auth;

public class PasswordHasherTests
{
    private readonly PasswordHasher _sut = new();

    [Fact]
    public void Hash_ReturnsNonEmptyHashAndSalt()
    {
        var (hash, salt) = _sut.Hash("my-password");

        hash.Should().NotBeNullOrWhiteSpace();
        salt.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Hash_ProducesDifferentSaltsEachTime()
    {
        var (_, salt1) = _sut.Hash("password");
        var (_, salt2) = _sut.Hash("password");

        salt1.Should().NotBe(salt2);
    }

    [Fact]
    public void Verify_ReturnsTrueForCorrectPassword()
    {
        var (hash, salt) = _sut.Hash("correct-password");

        _sut.Verify("correct-password", hash, salt).Should().BeTrue();
    }

    [Fact]
    public void Verify_ReturnsFalseForWrongPassword()
    {
        var (hash, salt) = _sut.Hash("correct-password");

        _sut.Verify("wrong-password", hash, salt).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "somesalt")]
    [InlineData("", "somesalt")]
    [InlineData("   ", "somesalt")]
    [InlineData("somehash", null)]
    [InlineData("somehash", "")]
    [InlineData("somehash", "   ")]
    public void Verify_ReturnsFalseForNullOrWhitespaceInputs(string? hash, string? salt)
    {
        _sut.Verify("password", hash!, salt!).Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalseForInvalidBase64()
    {
        _sut.Verify("password", "not-base64!!!", "not-base64!!!").Should().BeFalse();
    }
}
