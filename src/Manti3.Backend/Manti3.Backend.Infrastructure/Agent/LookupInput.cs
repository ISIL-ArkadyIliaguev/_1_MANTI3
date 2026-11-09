using System.Text.RegularExpressions;

namespace Manti3.Backend.Infrastructure.Agent;

// מה אפשר לחלץ מטקסט הבקשה בלי מודל: מספרי עובד וקודי מרכז עבודה. אלה מועמדים בלבד: השאילתות מחזירות את מה שקיים
// ב-DB, והסוכן מחליט מה נכון. אף ערך מהטקסט לא נכנס ל-SQL כטקסט (רק כפרמטר)
internal static partial class LookupInput
{
    public const int MaxNumbers = 40;
    private const int MaxCodes = 20;

    // מספר של 4-7 ספרות. אותיות עבריות צמודות מותרות (טקסט מימין לשמאל), אותיות לטיניות וספרות צמודות לא
    public static IReadOnlyList<string> EmployeeNumbers(string text)
        => Number().Matches(text).Select(m => m.Groups[1].Value).Distinct().Take(MaxNumbers).ToList();

    // מספרי עובד שמופיעים אחרי המילה "דוגמא/דוגמה" (עד 40 תווים לא-ספרתיים): מועמדים למשתמש הדוגמה
    public static IReadOnlyList<string> ExampleNumbers(string text)
        => AfterExample().Matches(text).Select(m => m.Groups[1].Value).Distinct().Take(MaxNumbers).ToList();

    // קוד מרכז עבודה: 2-6 תווים לטיניים/ספרות עם אות וספרה לפחות (N1S, H2). בגלל טקסט מימין לשמאל הקוד
    // עלול להופיע הפוך (S1N), לכן מוחזרים גם הקודים ההפוכים, וה-DB קובע איזה קיים
    public static IReadOnlyList<string> WorkCenterCodes(string text)
    {
        var codes = new List<string>();
        foreach (Match m in Code().Matches(text))
        {
            var code = m.Value.ToUpperInvariant();
            if (code.Any(char.IsLetter) && code.Any(char.IsDigit))
                codes.Add(code);
        }

        return codes
            .Distinct()
            .Take(MaxCodes)
            .SelectMany(c => new[] { c, new string(c.Reverse().ToArray()) })
            .Distinct()
            .ToList();
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])(\d{4,7})(?![A-Za-z0-9])")]
    private static partial Regex Number();

    [GeneratedRegex(@"דוגמ[א-ת]*[^\d\r\n]{0,40}(?<![A-Za-z0-9])(\d{4,7})(?![A-Za-z0-9])")]
    private static partial Regex AfterExample();

    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z0-9]{2,6}(?![A-Za-z0-9])")]
    private static partial Regex Code();
}
