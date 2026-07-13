using FasonBarkod.Core.Entities;
using FasonBarkod.Core.Enums;
using FasonBarkod.Infrastructure.Data;
using FasonBarkod.Infrastructure.Printing;
using Microsoft.EntityFrameworkCore;

namespace FasonBarkod.Infrastructure.Services;

public class LabelTemplateService(ApplicationDbContext context) : ILabelTemplateService
{
    public async Task<string> ResolveTemplateContentAsync(
        LabelType labelType,
        string? brandCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedBrand = NormalizeBrand(brandCode);
        var templates = await context.LabelTemplates
            .AsNoTracking()
            .Where(x => x.IsActive && x.LabelType == labelType)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrEmpty(normalizedBrand))
        {
            var branded = templates.FirstOrDefault(x =>
                NormalizeBrand(x.BrandCode) == normalizedBrand);
            if (branded is not null)
            {
                return branded.Content;
            }
        }

        var fallback = templates.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.BrandCode));
        if (fallback is not null)
        {
            return fallback.Content;
        }

        throw new InvalidOperationException(
            $"{(labelType == LabelType.KoliUstu ? "Koli üstü" : "Koli içi")} etiket şablonu bulunamadı. " +
            "Admin panelinden bir varsayılan tasarım ekleyin.");
    }

    public async Task<IReadOnlyList<LabelTemplate>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.LabelTemplates
            .AsNoTracking()
            .OrderBy(x => x.LabelType)
            .ThenBy(x => x.BrandCode ?? string.Empty)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

    public async Task<LabelTemplate?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        await context.LabelTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<(bool Success, string? Error)> SaveAsync(
        LabelTemplate template,
        CancellationToken cancellationToken = default)
    {
        template.BrandCode = string.IsNullOrWhiteSpace(template.BrandCode)
            ? null
            : NormalizeBrand(template.BrandCode);
        template.Name = template.Name.Trim();
        template.Content = template.Content.TrimEnd();
        template.UpdatedAt = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(template.Name))
        {
            return (false, "Tasarım adı zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(template.Content))
        {
            return (false, "Şablon içeriği boş olamaz.");
        }

        var duplicate = await context.LabelTemplates.AnyAsync(
            x => x.Id != template.Id
                 && x.LabelType == template.LabelType
                 && (x.BrandCode ?? string.Empty) == (template.BrandCode ?? string.Empty),
            cancellationToken);

        if (duplicate)
        {
            var scope = string.IsNullOrWhiteSpace(template.BrandCode)
                ? "varsayılan"
                : $"marka '{template.BrandCode}'";
            return (false, $"Bu etiket tipi için {scope} tasarım zaten mevcut.");
        }

        if (template.Id == 0)
        {
            context.LabelTemplates.Add(template);
        }
        else
        {
            context.LabelTemplates.Update(template);
        }

        await context.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var template = await context.LabelTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (template is null)
        {
            return;
        }

        context.LabelTemplates.Remove(template);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task EnsureSeededFromFilesAsync(string templateRoot, CancellationToken cancellationToken = default)
    {
        if (await context.LabelTemplates.AnyAsync(cancellationToken))
        {
            return;
        }

        if (!Directory.Exists(templateRoot))
        {
            return;
        }

        foreach (var file in LabelTemplateCatalog.ListTemplates(templateRoot))
        {
            var content = await File.ReadAllTextAsync(Path.Combine(templateRoot, file), cancellationToken);
            var (brand, labelType, name) = ParseFileName(file);
            if (labelType is null)
            {
                continue;
            }

            context.LabelTemplates.Add(new LabelTemplate
            {
                Name = name,
                BrandCode = brand,
                LabelType = labelType.Value,
                Content = content,
                IsActive = true,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static (string? Brand, LabelType? LabelType, string Name) ParseFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (name.Equals("koli_ustu", StringComparison.OrdinalIgnoreCase))
        {
            return (null, LabelType.KoliUstu, "Varsayılan Koli Üstü");
        }

        if (name.Equals("koli_ici", StringComparison.OrdinalIgnoreCase))
        {
            return (null, LabelType.KoliIci, "Varsayılan Koli İçi");
        }

        if (name.EndsWith("_koli_ustu", StringComparison.OrdinalIgnoreCase))
        {
            return (name[..^"_koli_ustu".Length], LabelType.KoliUstu, $"{name} tasarımı");
        }

        if (name.EndsWith("_koli_ici", StringComparison.OrdinalIgnoreCase))
        {
            return (name[..^"_koli_ici".Length], LabelType.KoliIci, $"{name} tasarımı");
        }

        return (null, null, name);
    }

    private static string NormalizeBrand(string? brandCode)
    {
        if (string.IsNullOrWhiteSpace(brandCode))
        {
            return string.Empty;
        }

        return brandCode.Trim().ToLowerInvariant().Replace(' ', '_');
    }
}
