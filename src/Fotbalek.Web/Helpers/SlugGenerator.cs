using System.Text.RegularExpressions;

namespace Fotbalek.Web.Helpers;

public static partial class SlugGenerator
{
    public static string GenerateSlug(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Convert to lowercase
        var slug = input.ToLowerInvariant();

        // Replace spaces with hyphens
        slug = slug.Replace(' ', '-');

        // Remove special characters, keep only alphanumeric and hyphens
        slug = AlphanumericAndHyphensRegex().Replace(slug, "");

        // Remove multiple consecutive hyphens
        slug = MultipleHyphensRegex().Replace(slug, "-");

        // Trim hyphens from start and end
        slug = slug.Trim('-');

        return slug;
    }

    public static string MakeUnique(string baseSlug, IEnumerable<string> existingSlugs)
    {
        var existing = existingSlugs.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(baseSlug))
            return baseSlug;

        var counter = 2;
        string newSlug;
        do
        {
            newSlug = $"{baseSlug}-{counter}";
            counter++;
        } while (existing.Contains(newSlug));

        return newSlug;
    }

    [GeneratedRegex("[^a-z0-9-]")]
    private static partial Regex AlphanumericAndHyphensRegex();

    [GeneratedRegex("-+")]
    private static partial Regex MultipleHyphensRegex();
}
