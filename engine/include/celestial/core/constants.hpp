#pragma once

#include <celestial/core/platform.hpp>

namespace celestial::core {

// Mirror of src/CelestialMechanics.Math/PhysicalConstants.cs
// All values must be kept in sync with the C# definitions.

/// Gravitational constant in m^3/(kg*s^2).
constexpr double G_SI = 6.67430e-11;

/// Speed of light in m/s.
constexpr double C_SI = 2.998e8;

/// Solar mass in kg.
constexpr double SolarMass = 1.989e30;

/// Solar radius in m.
constexpr double SolarRadius = 6.957e8;

/// Astronomical unit in m.
constexpr double AU = 1.496e11;

/// Time unit in seconds: sqrt(AU^3 / (G_SI * SolarMass)).
constexpr double TimeUnit = 5.0226e6;

/// Gravitational constant in simulation (normalized) units.
constexpr double G_Sim = 1.0;

/// Speed of light in simulation units (AU / TimeUnit).
/// C_Sim = C_SI * TimeUnit / AU ~ 10065.3
constexpr double C_Sim = C_SI * TimeUnit / AU;

/// c^2 in simulation units.
constexpr double C_Sim2 = C_Sim * C_Sim;

/// c^4 in simulation units (GW strain formula).
constexpr double C_Sim4 = C_Sim2 * C_Sim2;

/// c^5 in simulation units (GW energy loss rate).
constexpr double C_Sim5 = C_Sim4 * C_Sim;

/// Schwarzschild radius factor: Rs = SchwarzschildFactorSim * M.
/// 2 * G_Sim / C_Sim^2
constexpr double SchwarzschildFactorSim = 2.0 * G_Sim / C_Sim2;

/// Radiative accretion efficiency (Schwarzschild BH: ~0.057).
constexpr double AccretionEfficiency = 0.057;

/// Chandrasekhar limit in solar masses.
constexpr double ChandrasekharLimit = 1.44;

/// Tolman-Oppenheimer-Volkoff limit in solar masses (NS vs BH threshold).
constexpr double TOVLimit = 3.0;

/// Neutron star density (kg/m^3 equivalent in sim units).
constexpr double NeutronStarDensity = 1e12;

/// Sedov-Taylor dimensionless constant.
constexpr double SedovTaylorXi = 1.15;

} // namespace celestial::core
