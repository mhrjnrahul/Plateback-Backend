using PlateBack.Domain.Entities;

namespace PlateBack.Domain.Tests;

public class PickupCodeTests
{
    [Fact]
    public void Generated_codes_use_the_unambiguous_alphabet()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = PickupCode.Generate();
            code.Length.ShouldBe(9);
            code[4].ShouldBe('-');
            code.Replace("-", "").ShouldAllBe(c => PickupCode.Alphabet.Contains(c));
        }
    }

    [Theory]
    [InlineData("k7m2 9qxa", "K7M2-9QXA")]
    [InlineData("K7M29QXA", "K7M2-9QXA")]
    [InlineData(" K7M2-9QXA ", "K7M2-9QXA")]
    public void Normalizes_what_people_type(string input, string expected) =>
        PickupCode.Normalize(input).ShouldBe(expected);

    [Theory]
    [InlineData("K7M2-9QX")] // too short
    [InlineData("K7M2-9QXAB")] // too long
    [InlineData("O7M2-9QXA")] // O is not in the alphabet
    [InlineData("1111-1111")] // nor is 1
    [InlineData("")]
    public void Rejects_impossible_codes(string input) => PickupCode.Normalize(input).ShouldBeNull();
}

public class RefreshTokenTests
{
    [Fact]
    public void A_rotated_token_is_inactive_and_marked_as_replaced()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "hash", TestData.Now, TimeSpan.FromDays(7));
        token.IsActive(TestData.Now).ShouldBeTrue();
        token.Revoke(TestData.Now, Guid.NewGuid());
        token.IsActive(TestData.Now).ShouldBeFalse();
        token.WasRotated.ShouldBeTrue();
    }

    [Fact]
    public void Expires_after_its_lifetime()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "hash", TestData.Now, TimeSpan.FromDays(7));
        token.IsActive(TestData.Now.AddDays(7)).ShouldBeFalse();
    }
}
