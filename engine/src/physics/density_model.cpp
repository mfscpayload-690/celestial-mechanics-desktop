#include <celestial/physics/density_model.hpp>
#include <cmath>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

namespace celestial::physics {

double DensityModel::compute_density(double mass, double radius, double min_radius) {
    double r = (radius > min_radius) ? radius : min_radius;
    double volume = (4.0 / 3.0) * M_PI * r * r * r;
    if (volume < 1e-30) return 0.0;
    return mass / volume;
}

double DensityModel::compute_radius(double mass, double density, double min_radius) {
    if (density < 1e-30 || mass < 1e-30) return min_radius;
    double r = std::cbrt(3.0 * mass / (4.0 * M_PI * density));
    return (r > min_radius) ? r : min_radius;
}

void DensityModel::update_densities(ParticleSystem& particles) const {
    if (!particles.density) return;
    for (i32 i = 0; i < particles.count; i++) {
        if (!particles.is_active[i]) continue;
        particles.density[i] = compute_density(
            particles.mass[i], particles.radius[i], config_.min_radius);
    }
}

void DensityModel::update_radius(ParticleSystem& particles, i32 idx) const {
    if (!particles.density || idx < 0 || idx >= particles.count) return;
    particles.radius[idx] = compute_radius(
        particles.mass[idx], particles.density[idx], config_.min_radius);
}

} // namespace celestial::physics
