namespace FasonBarkod.Infrastructure.Printing;

public static class LabelTemplateCatalog
{
    public static IReadOnlyList<string> ListTemplates(string templateRoot)
    {
        if (!Directory.Exists(templateRoot))
        {
            return [];
        }

        return Directory.GetFiles(templateRoot, "*.prn", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string ResolveTemplatePath(
        string templateRoot,
        string? templateFileName,
        string fallbackFileName)
    {
        if (!string.IsNullOrWhiteSpace(templateFileName))
        {
            var explicitPath = Path.Combine(templateRoot, templateFileName.Trim());
            if (File.Exists(explicitPath))
            {
                return explicitPath;
            }

            throw new FileNotFoundException($"Seçilen etiket şablonu bulunamadı: {templateFileName}");
        }

        var fallbackPath = Path.Combine(templateRoot, fallbackFileName);
        if (File.Exists(fallbackPath))
        {
            return fallbackPath;
        }

        throw new FileNotFoundException($"Varsayılan etiket şablonu bulunamadı: {fallbackFileName}");
    }
}
