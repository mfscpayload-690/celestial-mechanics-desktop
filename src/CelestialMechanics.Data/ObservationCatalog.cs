namespace CelestialMechanics.Data;

/// <summary>
/// A single entry in the observation catalog describing a real celestial object.
/// </summary>
public record CatalogEntry(
    string Name,
    string Type,
    double MassSolar,       // in solar masses
    double RadiusSolar,     // in solar radii
    string SpectralClass,
    double LuminositySolar, // in solar luminosities
    string Description
);

/// <summary>
/// Static catalog of real celestial objects for the observation mode.
/// Data sourced from well-known astrophysical references.
/// </summary>
public static class ObservationCatalog
{
    public static readonly CatalogEntry[] Entries =
    {
        new("Sun", "G-type star",
            1.0, 1.0, "G2V", 1.0,
            "Our star"),

        new("Proxima Centauri", "Red dwarf",
            0.122, 0.154, "M5.5Ve", 0.0017,
            "Closest star to the Solar System"),

        new("Sirius A", "A-type star",
            2.063, 1.711, "A1V", 25.4,
            "Brightest star in the night sky"),

        new("Betelgeuse", "Red supergiant",
            11.6, 887.0, "M1Ia", 126000.0,
            "Orion's shoulder star"),

        new("Vega", "A-type star",
            2.135, 2.362, "A0V", 40.12,
            "Former pole star"),

        new("Crab Pulsar", "Neutron star",
            1.4, 1.437e-5, "-", 75000.0,
            "Remnant of SN 1054"),

        new("Sgr A*", "Supermassive black hole",
            4.15e6, 17244.0, "-", 0.0,
            "Milky Way galactic center"),

        new("Cygnus X-1", "Stellar black hole",
            21.2, 0.125, "-", 0.0,
            "First widely accepted black hole"),

        new("Earth", "Rocky planet",
            3.0e-6, 0.00916, "-", 0.0,
            "Third planet from the Sun"),

        new("Jupiter", "Gas giant",
            9.546e-4, 0.10045, "-", 0.0,
            "Largest planet in Solar System"),
    };
}
