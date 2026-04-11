namespace CelestialMechanics.Data;

public sealed record AddMenuEntry(
    string TopCategory,
    string Category,
    string SubCategory,
    string DisplayName,
    string TemplateName,
    string Description);

public static class CelestialAddMenuCatalog
{
    public static IReadOnlyList<AddMenuEntry> Entries { get; } =
    [
        new("Planets and Planetary Bodies", "Planets", "Terrestrial", "Terrestrial Planet", "Terrestrial Planet", "Large rocky planet that reflects stellar light."),
        new("Planets and Planetary Bodies", "Planets", "Gas Giants", "Gas Giant", "Gas Giant", "Massive gas-dominant planet with thick atmosphere."),
        new("Planets and Planetary Bodies", "Dwarf Planets", "General", "Dwarf Planet", "Dwarf Planet", "Rounded planetary body that has not cleared its orbit."),
        new("Planets and Planetary Bodies", "Moons", "Natural Satellites", "Moon", "Moon", "Natural satellite orbiting a planetary body."),
        new("Planets and Planetary Bodies", "Small Solar System Bodies", "Asteroids", "Asteroid", "Asteroid", "Rocky small body with irregular shape."),
        new("Planets and Planetary Bodies", "Small Solar System Bodies", "Comets", "Comet", "Comet", "Icy body with volatile-driven tails near stars."),
        new("Planets and Planetary Bodies", "Small Solar System Bodies", "Meteoroids", "Meteoroid", "Meteoroid", "Very small rocky fragment."),

        new("Stars and Stellar Systems", "Stellar Classification (MK)", "OBAFGKM", "O-Type Star", "O-Type Star", "Very hot and massive blue star."),
        new("Stars and Stellar Systems", "Stellar Classification (MK)", "OBAFGKM", "B-Type Star", "B-Type Star", "Hot luminous blue-white star."),
        new("Stars and Stellar Systems", "Stellar Classification (MK)", "OBAFGKM", "A-Type Star", "A-Type Star", "White star with strong Balmer lines."),
        new("Stars and Stellar Systems", "Stellar Classification (MK)", "OBAFGKM", "F-Type Star", "F-Type Star", "Yellow-white main sequence star."),
        new("Stars and Stellar Systems", "Stellar Classification (MK)", "OBAFGKM", "G-Type Star", "G-Type Star", "Sun-like yellow star."),
        new("Stars and Stellar Systems", "Stellar Classification (MK)", "OBAFGKM", "K-Type Star", "K-Type Star", "Orange main sequence star."),
        new("Stars and Stellar Systems", "Stellar Classification (MK)", "OBAFGKM", "M-Type Star", "M-Type Star", "Cool red dwarf star."),

        new("Stars and Stellar Systems", "Luminosity Classes", "0-I-V-VII", "Hypergiant (Class 0)", "Hypergiant", "Extremely luminous giant star."),
        new("Stars and Stellar Systems", "Luminosity Classes", "0-I-V-VII", "Supergiant (Class I)", "Supergiant", "Massive evolved high-luminosity star."),
        new("Stars and Stellar Systems", "Luminosity Classes", "0-I-V-VII", "Main Sequence (Class V)", "Main Sequence Star", "Hydrogen-burning stable star."),
        new("Stars and Stellar Systems", "Luminosity Classes", "0-I-V-VII", "White Dwarf (Class VII)", "White Dwarf", "Compact stellar remnant with no fusion."),

        new("Stars and Stellar Systems", "Compact Objects", "Remnants", "Neutron Star", "Neutron Star", "Ultra-dense compact remnant."),
        new("Stars and Stellar Systems", "Compact Objects", "Remnants", "Pulsar", "Pulsar", "Rotating neutron star emitting regular beams."),
        new("Stars and Stellar Systems", "Compact Objects", "Remnants", "Magnetar", "Magnetar", "Highly magnetized neutron star."),
        new("Stars and Stellar Systems", "Compact Objects", "Remnants", "Black Hole", "Black Hole (Stellar)", "Collapsed compact object with event horizon."),
        new("Stars and Stellar Systems", "Nebulae", "Interstellar Clouds", "Emission Nebula", "Emission Nebula", "Gas and dust cloud often associated with star formation."),

        new("Galaxies and Large-Scale Structures", "Morphology", "Galaxy Types", "Spiral Galaxy", "Spiral Galaxy", "Disk galaxy with spiral arms."),
        new("Galaxies and Large-Scale Structures", "Morphology", "Galaxy Types", "Elliptical Galaxy", "Elliptical Galaxy", "Spheroidal stellar system with little gas."),
        new("Galaxies and Large-Scale Structures", "Morphology", "Galaxy Types", "Lenticular Galaxy", "Lenticular Galaxy", "Disk-like galaxy without prominent spiral arms."),
        new("Galaxies and Large-Scale Structures", "Morphology", "Galaxy Types", "Irregular Galaxy", "Irregular Galaxy", "Galaxy lacking regular morphology."),
        new("Galaxies and Large-Scale Structures", "Active Galaxies", "Energetic Cores", "Quasar", "Quasar", "Extremely luminous active galactic nucleus."),
        new("Galaxies and Large-Scale Structures", "Active Galaxies", "Energetic Cores", "Blazar", "Blazar", "Jet-aligned active galactic nucleus."),
        new("Galaxies and Large-Scale Structures", "Higher Groupings", "Hierarchy", "Galaxy Group", "Galaxy Group", "Small gravitationally bound galaxy collection."),
        new("Galaxies and Large-Scale Structures", "Higher Groupings", "Hierarchy", "Galaxy Cluster", "Galaxy Cluster", "Large gravitationally bound galaxy aggregation."),
        new("Galaxies and Large-Scale Structures", "Higher Groupings", "Hierarchy", "Supercluster", "Supercluster", "Largest mapped galaxy-scale structure."),
    ];
}
