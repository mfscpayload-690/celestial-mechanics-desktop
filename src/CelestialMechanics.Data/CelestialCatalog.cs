namespace CelestialMechanics.Data;

public sealed record CelestialObjectCategory(
    string Name,
    string Description,
    IReadOnlyList<BodyTemplate> Templates);

public static class CelestialCatalog
{
    private static readonly Dictionary<string, BodyTemplate> _templateByName =
        ObjectTemplates.All.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<CelestialObjectCategory> Categories { get; } = BuildCategories();

    public static bool TryGetTemplate(string templateName, out BodyTemplate template)
    {
        return _templateByName.TryGetValue(templateName, out template!);
    }

    private static IReadOnlyList<CelestialObjectCategory> BuildCategories()
    {
        var templatesByTopCategory = CelestialAddMenuCatalog.Entries
            .Where(e => _templateByName.ContainsKey(e.TemplateName))
            .GroupBy(e => e.TopCategory, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CelestialObjectCategory(
                group.Key,
                $"Objects from {group.Key}.",
                group.Select(e => _templateByName[e.TemplateName])
                    .DistinctBy(t => t.Name)
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();

        if (templatesByTopCategory.Length > 0)
            return templatesByTopCategory;

        return new[]
        {
            new CelestialObjectCategory(
                "All Objects",
                "All available templates.",
                ObjectTemplates.All.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToArray())
        };
    }
}
