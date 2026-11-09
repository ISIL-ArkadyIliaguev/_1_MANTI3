using System.Text.RegularExpressions;

namespace Manti3.Backend.Application.Features.AccessRequests.BuildScript;

// שם משתמש חדש ב-MANTI = שם פרטי באנגלית + האות הראשונה של שם המשפחה באנגלית (Doron + Shapira -> DoronS). אם השם תפוס,
// מוסיפים אות נוספת משם המשפחה (DoronSh, DoronSha...) עד ששם פנוי (שינוי של 2026-11-08; קודם: שתי אותיות, ושם תפוס היה שאלה).
// השמות באנגלית באים מ-HR (FirstNameE / LastNameE), ואם חסרים: תרגום של השם העברי.
// החישוב כאן בקוד, לא בידי המודל, כדי שהסוכן, בדיקת "השם תפוס" והסקריפט יסכימו על אותו שם
public static partial class EnglishNames
{
    public const int MaxLength = 15; // אורך העמודות EngFirstName / EngLastName

    // המילים באותיות לטיניות בלבד (בלי רווחים, מקפים, גרשים, ספרות): "ben-david" -> ["ben", "david"]
    public static IReadOnlyList<string> Words(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? []
            : NonLetters().Split(name).Where(w => w.Length > 0).ToList();

    // הצורה שנשמרת ב-EngFirstName / EngLastName: האות הראשונה גדולה והשאר קטנות (Feras, Abdalla), מילים עם רווח.
    // null כשאין אותיות לטיניות או שהשם ארוך מ-15
    public static string? ForColumn(string? name)
    {
        var words = Words(name);
        if (words.Count == 0)
            return null;

        var text = string.Join(' ', words.Select(Title));
        return text.Length <= MaxLength ? text : null;
    }

    // השם הפרטי עם אות ראשונה גדולה בכל מילה + האות הראשונה של שם המשפחה (גדולה), או `letters` האותיות הראשונות שלו.
    // שונה ב-2026-11-08: היו שתי אותיות, ועכשיו מתחילים באות אחת וממשיכים לאות נוספת כל עוד השם תפוס (ראו Resolve).
    // null כשאחד מהשמות אינו אנגלי תקין
    public static string? UserName(string? firstName, string? lastName, int letters = 1)
    {
        if (ForColumn(firstName) is null || ForColumn(lastName) is null)
            return null;

        var first = string.Concat(Words(firstName).Select(Title));
        var lastLetters = string.Concat(Words(lastName));
        var initials = Title(lastLetters.Length > letters ? lastLetters[..letters] : lastLetters);
        return first + initials;
    }

    // כל השמות האפשריים לפי הסדר: שם פרטי + 1 אות, + 2 אותיות, ... עד כל שם המשפחה. ריק כשאחד השמות אינו תקין
    public static IReadOnlyList<string> UserNameCandidates(string? firstName, string? lastName)
    {
        if (ForColumn(firstName) is null || ForColumn(lastName) is null)
            return [];

        var length = string.Concat(Words(lastName)).Length;
        return Enumerable.Range(1, length).Select(n => UserName(firstName, lastName, n)!).Distinct().ToList();
    }

    // השם הראשון ברשימה שאינו תפוס (בלי הבדל בין גדולות לקטנות, כמו ה-DB). null כשכולם תפוסים
    public static string? Resolve(string? firstName, string? lastName, IReadOnlySet<string> taken)
        => UserNameCandidates(firstName, lastName)
            .FirstOrDefault(name => !taken.Contains(name));

    private static string Title(string word) => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();

    [GeneratedRegex("[^A-Za-z]+")]
    private static partial Regex NonLetters();
}
