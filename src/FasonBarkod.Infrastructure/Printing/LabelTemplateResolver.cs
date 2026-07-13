using FasonBarkod.Core.Enums;

namespace FasonBarkod.Infrastructure.Printing;

internal static class LabelTemplateResolver
{
    public static string ResolveTemplatePath(
        string templateRoot,
        LabelType labelType,
        string? brandCode)
    {
        var suffix = labelType == LabelType.KoliUstu ? "koli_ustu" : "koli_ici";
        var normalizedBrand = NormalizeBrand(brandCode);

        if (!string.IsNullOrEmpty(normalizedBrand))
        {
            var branded = Path.Combine(templateRoot, $"{normalizedBrand}_{suffix}.prn");
            if (File.Exists(branded))
            {
                return branded;
            }
        }

        var fallback = Path.Combine(templateRoot, $"{suffix}.prn");
        if (File.Exists(fallback))
        {
            return fallback;
        }

        throw new FileNotFoundException(
            $"Etiket şablonu bulunamadı. Beklenen: {fallback}" +
            (string.IsNullOrEmpty(normalizedBrand) ? "" : $" veya {normalizedBrand}_{suffix}.prn"));
    }

    private static string NormalizeBrand(string? brandCode)
    {
        if (string.IsNullOrWhiteSpace(brandCode))
        {
            return string.Empty;
        }

        return brandCode.Trim().ToLowerInvariant()
            .Replace(' ', '_');
    }
}
