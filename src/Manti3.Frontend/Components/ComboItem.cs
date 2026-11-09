namespace Manti3.Frontend.Components;

// פריט ברשימת בחירה. Label = מה שמוצג בתגית (למשל קוד מרכז העבודה), Hint = תיאור נוסף לחיפוש ולהצגה,
// Dimmed = מוצג עמום (למשל מרכז עבודה שלא מופיע במסך של MANTI 2)
public sealed record ComboItem(int Id, string Label, string? Hint = null, bool Dimmed = false)
{
    public bool Matches(string term)
        => term.Length == 0
           || Label.Contains(term, StringComparison.OrdinalIgnoreCase)
           || (Hint?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
           || Id.ToString().StartsWith(term, StringComparison.Ordinal);
}
