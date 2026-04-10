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
        return new List<CelestialObjectCategory>
        {
            new(
                "Stars",
                "Main sequence and compact stellar objects.",
                ObjectTemplates.All.Where(t => t.BodyType is "Star" or "NeutronStar").ToArray()),
            new(
                "Planets & Moons",
                "Rocky and gaseous planets plus natural satellites.",
                ObjectTemplates.All.Where(t => t.BodyType is "Planet" or "RockyPlanet" or "GasGiant" or "Moon").ToArray()),
            new(
                "Small Bodies",
                "Asteroids and comets for orbit design.",
                ObjectTemplates.All.Where(t => t.BodyType is "Asteroid" or "Comet").ToArray()),
            new(
                "Compact Objects",
                "Extreme gravity remnants.",
                ObjectTemplates.All.Where(t => t.BodyType is "BlackHole").ToArray())
        }.Where(c => c.Templates.Count > 0).ToArray();
    }
}
