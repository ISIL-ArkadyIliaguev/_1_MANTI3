using Manti3.Backend.Infrastructure.Agent;
using Shouldly;

namespace Manti3.Backend.UnitTests;

// טקסטים מומצאים בלבד: אין כאן שמות או מספרי עובדים אמיתיים
public class LookupInputTests
{
    [Fact]
    public void EmployeeNumbers_FindsDistinctNumbersOfFourToSevenDigits()
    {
        var text = "Worker A 10001, Worker B 10002 ושוב 10001. טלפון 054 (3006) וקוד 12";

        LookupInput.EmployeeNumbers(text).ShouldBe(["10001", "10002", "3006"]);
    }

    [Fact]
    public void EmployeeNumbers_IgnoresLongerDigitRunsAndDigitsInsideCodes()
        => LookupInput.EmployeeNumbers("1234567890 and N1S2345X").ShouldBeEmpty();

    [Fact]
    public void EmployeeNumbers_NextToHebrewLetters_AreFound()
        => LookupInput.EmployeeNumbers("העובד10001והעובד10002").ShouldBe(["10001", "10002"]);

    [Fact]
    public void WorkCenterCodes_FindsCodesWithLetterAndDigit_AndTheirReversedForm()
    {
        var codes = LookupInput.WorkCenterCodes("למרכז עבודה N1S ולאחר מכן S1N, וגם H2");

        codes.ShouldContain("N1S");
        codes.ShouldContain("S1N");
        codes.ShouldContain("H2");
        codes.ShouldContain("2H");
    }

    [Fact]
    public void WorkCenterCodes_IgnoresPlainWordsAndPlainNumbers()
        => LookupInput.WorkCenterCodes("hello world 12345 boss@example.test").ShouldBeEmpty();

    [Fact]
    public void WorkCenterCodes_ReversedCodeAdjacentToHebrew_IsFound()
        => LookupInput.WorkCenterCodes("למרכזS1Nבבקשה").ShouldContain("S1N");

    [Fact]
    public void ExampleNumbers_TakesTheNumbersAfterTheWordExample()
    {
        var text = "נא להוסיף את Worker A 10001. דוגמא Worker C 10005";

        LookupInput.ExampleNumbers(text).ShouldBe(["10005"]);
    }

    [Fact]
    public void ExampleNumbers_NoExampleWord_IsEmpty()
        => LookupInput.ExampleNumbers("רק 10001 ו-10002").ShouldBeEmpty();

    [Fact]
    public void Lists_AreCapped()
    {
        var many = string.Join(' ', Enumerable.Range(10000, 200));

        LookupInput.EmployeeNumbers(many).Count.ShouldBe(LookupInput.MaxNumbers);
    }
}
