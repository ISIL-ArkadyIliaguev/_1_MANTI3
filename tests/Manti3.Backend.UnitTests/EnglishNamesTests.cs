using Manti3.Backend.Application.Features.AccessRequests.BuildScript;
using Shouldly;

namespace Manti3.Backend.UnitTests;

public class EnglishNamesTests
{
    [Theory]
    [InlineData("Doron", "Shapira", "DoronS")]
    [InlineData("DORON", "SHAPIRA", "DoronS")]          // HR holds capitals
    [InlineData("  yossi ", " cohen ", "YossiC")]
    [InlineData("Mary Ann", "Lee", "MaryAnnL")]        // two words in the first name
    [InlineData("Dan", "Ben-David", "DanB")]           // hyphen in the last name
    [InlineData("Tal", "Y", "TalY")]                    // a one-letter last name
    [InlineData("Ilya", "O'Neil", "IlyaO")]
    public void UserName_FirstNamePlusFirstLetterOfLastName(string first, string last, string expected)
        => EnglishNames.UserName(first, last).ShouldBe(expected);

    [Fact]
    public void UserNameCandidates_AddOneLetterOfTheLastNameEachTime()
        => EnglishNames.UserNameCandidates("Dana", "Cohen").ShouldBe(["DanaC", "DanaCo", "DanaCoh", "DanaCohe", "DanaCohen"]);

    [Fact]
    public void UserNameCandidates_OfAnInvalidName_AreEmpty()
        => EnglishNames.UserNameCandidates("Dana", "כהן").ShouldBeEmpty();

    [Fact]
    public void Resolve_TakesTheFirstFreeName_IgnoringCase()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DanaC", "DANACO" };

        EnglishNames.Resolve("Dana", "Cohen", taken).ShouldBe("DanaCoh");
        EnglishNames.Resolve("Dana", "Levi", taken).ShouldBe("DanaL");
    }

    [Fact]
    public void Resolve_EveryNameTaken_IsNull()
        => EnglishNames.Resolve("Tal", "Yo", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TalY", "TalYo" }).ShouldBeNull();

    [Theory]
    [InlineData(null, "Cohen")]
    [InlineData("", "Cohen")]
    [InlineData("דנה", "Cohen")]                        // Hebrew only: not English
    [InlineData("Dana", "כהן")]
    [InlineData("ABCDEFGHIJKLMNOP", "Cohen")]           // more than 15 letters
    [InlineData("1234", "Cohen")]
    public void UserName_NoUsableEnglishName_IsNull(string? first, string? last)
        => EnglishNames.UserName(first, last).ShouldBeNull();

    [Theory]
    [InlineData("FERAS", "Feras")]
    [InlineData("abdALLA", "Abdalla")]
    [InlineData("ben-david", "Ben David")]
    [InlineData("Moe'; DROP x;--", "Moe Drop X")]
    public void ForColumn_CapitalsLettersAndSingleSpaces(string name, string expected)
        => EnglishNames.ForColumn(name).ShouldBe(expected);
}
