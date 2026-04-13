using CelestialMechanics.Physics.Types;

namespace CelestialMechanics.Desktop.Models;

public sealed class SceneNodeItem
{
    public required Guid NodeId { get; init; }
    public int? BodyId { get; init; }
    public required string Name { get; init; }
    public required BodyType BodyType { get; init; }

    public string TypeLabel => BodyType.ToString();
    public string IconGlyph => GetIconForBodyType(BodyType);

    public static string GetIconForBodyType(BodyType bodyType) => bodyType switch
    {
        BodyType.Star => "★",
        BodyType.Planet => "●",
        BodyType.GasGiant => "◉",
        BodyType.RockyPlanet => "▪",
        BodyType.Moon => "◦",
        BodyType.Asteroid => "◇",
        BodyType.NeutronStar => "✦",
        BodyType.BlackHole => "◯",
        BodyType.Comet => "☄",
        _ => "◈",
    };
}
