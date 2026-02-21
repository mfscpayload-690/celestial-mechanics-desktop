#pragma once

#include <celestial/physics/particle_system.hpp>
#include <celestial/core/platform.hpp>

namespace celestial::physics {

/// 1PN (Einstein-Infeld-Hoffmann) correction to gravitational acceleration.
/// Port of C# PostNewtonian1Correction.
class CELESTIAL_API PostNewtonianCorrection {
public:
    double softening_epsilon = 1e-4;
    double max_velocity_fraction_c = 0.3;
    double schwarzschild_warning_factor = 3.0;

    /// Apply 1PN corrections to all active bodies.
    /// Adds corrections to acc_x/y/z (call after Newtonian forces).
    void apply_corrections(ParticleSystem& particles);

    /// Compute correction for a single body.
    void compute_correction(
        const double* px, const double* py, const double* pz,
        const double* vx, const double* vy, const double* vz,
        const double* mass, i32 body_index, i32 count,
        double& dax, double& day, double& daz);
};

} // namespace celestial::physics
