#pragma once

#include <celestial/physics/particle_system.hpp>
#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>

namespace celestial::physics {

/// Configuration for the density model.
struct DensityConfig {
    double default_density = 1000.0;  ///< Default density (kg/m³, water-like)
    double min_radius = 1e-6;         ///< Floor to prevent degenerate radii
};

/// Computes and manages body densities and density-derived radii.
/// Density: ρ = m / (4/3 π r³)
/// Radius:  R = (3m / (4πρ))^(1/3)
class CELESTIAL_API DensityModel {
public:
    /// Configure density model parameters.
    void configure(const DensityConfig& cfg) { config_ = cfg; }

    /// Compute density from mass and radius.
    /// ρ = m / (4/3 π r³), with radius clamped to min_radius.
    static double compute_density(double mass, double radius, double min_radius);

    /// Compute radius from mass and density.
    /// R = (3m / (4πρ))^(1/3), clamped to min_radius.
    static double compute_radius(double mass, double density, double min_radius);

    /// Bulk-update density[] array for all active particles.
    void update_densities(ParticleSystem& particles) const;

    /// Update a single particle's radius from its mass and density.
    void update_radius(ParticleSystem& particles, i32 idx) const;

    const DensityConfig& config() const { return config_; }

private:
    DensityConfig config_;
};

} // namespace celestial::physics
