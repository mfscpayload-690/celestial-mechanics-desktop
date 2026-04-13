using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Desktop.Models;

public static class BodyCatalog
{
    private static readonly IReadOnlyDictionary<BodyType, IReadOnlyList<BodySubtype>> Catalog =
        new Dictionary<BodyType, IReadOnlyList<BodySubtype>>
        {
            [BodyType.Star] = new List<BodySubtype>
            {
                new(BodyType.Star, "Yellow Dwarf", 1.0, 0.10),
                new(BodyType.Star, "Red Dwarf", 0.35, 0.06),
                new(BodyType.Star, "Blue Giant", 8.0, 0.22),
            }.AsReadOnly(),
            [BodyType.Planet] = new List<BodySubtype>
            {
                new(BodyType.Planet, "Temperate Planet", 0.001, 0.03),
                new(BodyType.Planet, "Ice Planet", 0.0008, 0.028),
            }.AsReadOnly(),
            [BodyType.GasGiant] = new List<BodySubtype>
            {
                new(BodyType.GasGiant, "Jovian", 0.01, 0.06),
                new(BodyType.GasGiant, "Hot Jupiter", 0.012, 0.065),
            }.AsReadOnly(),
            [BodyType.RockyPlanet] = new List<BodySubtype>
            {
                new(BodyType.RockyPlanet, "Terrestrial", 0.0005, 0.02),
                new(BodyType.RockyPlanet, "Super Earth", 0.0015, 0.026),
            }.AsReadOnly(),
            [BodyType.Moon] = new List<BodySubtype>
            {
                new(BodyType.Moon, "Major Moon", 0.0001, 0.012),
                new(BodyType.Moon, "Minor Moon", 0.00003, 0.008),
            }.AsReadOnly(),
            [BodyType.Asteroid] = new List<BodySubtype>
            {
                new(BodyType.Asteroid, "Rocky Asteroid", 0.00001, 0.005),
                new(BodyType.Asteroid, "Metallic Asteroid", 0.000015, 0.0045),
            }.AsReadOnly(),
            [BodyType.NeutronStar] = new List<BodySubtype>
            {
                new(BodyType.NeutronStar, "Neutron Star", 2.0, 0.02),
                new(BodyType.NeutronStar, "Magnetar", 2.4, 0.022),
            }.AsReadOnly(),
            [BodyType.BlackHole] = new List<BodySubtype>
            {
                new(BodyType.BlackHole, "Stellar Black Hole", 10.0, 0.08),
                new(BodyType.BlackHole, "Intermediate Black Hole", 50.0, 0.12),
            }.AsReadOnly(),
            [BodyType.Comet] = new List<BodySubtype>
            {
                new(BodyType.Comet, "Short-period Comet", 0.000001, 0.003),
                new(BodyType.Comet, "Long-period Comet", 0.0000005, 0.0025),
            }.AsReadOnly(),
            [BodyType.Custom] = new List<BodySubtype>
            {
                new(BodyType.Custom, "Custom Body", 1.0, 0.04),
            }.AsReadOnly(),
        };

    public static IReadOnlyList<BodySubtype> GetSubtypes(BodyType bodyType)
    {
        return Catalog.TryGetValue(bodyType, out var subtypes)
            ? subtypes
            : Array.Empty<BodySubtype>();
    }
}
