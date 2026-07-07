using System.Globalization;
using System.Text;

namespace WordReviewReminder.Core;

public static class WordNormalizer
{
    public static string NormalizeTerm(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return string.Empty;
        }

        var normalized = term.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category != UnicodeCategory.NonSpacingMark && !char.IsPunctuation(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
