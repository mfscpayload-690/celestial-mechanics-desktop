#include <celestial/physics/post_newtonian.hpp>
#include <celestial/core/constants.hpp>
#include <cmath>

namespace celestial::physics {

void PostNewtonianCorrection::apply_corrections(ParticleSystem& particles) {
    i32 n = particles.count;

    for (i32 i = 0; i < n; i++) {
        if (!particles.is_active[i]) continue;

        double dax = 0.0, day = 0.0, daz = 0.0;
        compute_correction(
            particles.pos_x, particles.pos_y, particles.pos_z,
            particles.vel_x, particles.vel_y, particles.vel_z,
            particles.mass, i, n, dax, day, daz);

        particles.acc_x[i] += dax;
        particles.acc_y[i] += day;
        particles.acc_z[i] += daz;
    }
}

void PostNewtonianCorrection::compute_correction(
    const double* px, const double* py, const double* pz,
    const double* vx, const double* vy, const double* vz,
    const double* mass, i32 body_index, i32 count,
    double& dax, double& day, double& daz)
{
    dax = 0.0; day = 0.0; daz = 0.0;

    const double G = core::G_Sim;
    const double inv_c2 = 1.0 / core::C_Sim2;
    const double max_vel = max_velocity_fraction_c * core::C_Sim;
    const double max_vel2 = max_vel * max_vel;
    const double eps2 = softening_epsilon * softening_epsilon;

    double xi = px[body_index];
    double yi = py[body_index];
    double zi = pz[body_index];
    double vxi = vx[body_index];
    double vyi = vy[body_index];
    double vzi = vz[body_index];
    double mi = mass[body_index];
    double vi2 = vxi * vxi + vyi * vyi + vzi * vzi;

    for (i32 j = 0; j < count; j++) {
        if (j == body_index) continue;

        double mj = mass[j];
        if (mj <= 0.0) continue;

        // Relative position: r = pos_j - pos_i
        double dx = px[j] - xi;
        double dy = py[j] - yi;
        double dz = pz[j] - zi;

        double dist2 = dx * dx + dy * dy + dz * dz + eps2;
        double dist = std::sqrt(dist2);
        double inv_dist = 1.0 / dist;

        // Relative velocity check
        double dvx = vxi - vx[j];
        double dvy = vyi - vy[j];
        double dvz = vzi - vz[j];
        double v_rel2 = dvx * dvx + dvy * dvy + dvz * dvz;
        if (v_rel2 > max_vel2) continue;

        // Schwarzschild proximity check
        double total_mass = mi + mj;
        double rs = core::SchwarzschildFactorSim * total_mass;
        if (dist < schwarzschild_warning_factor * rs) continue;

        // Unit vector from i to j
        double nx = dx * inv_dist;
        double ny = dy * inv_dist;
        double nz = dz * inv_dist;

        // Body j velocities
        double vxj = vx[j], vyj = vy[j], vzj = vz[j];
        double vj2 = vxj * vxj + vyj * vyj + vzj * vzj;

        // Dot products
        double vi_dot_vj = vxi * vxj + vyi * vyj + vzi * vzj;
        double n_dot_vi = nx * vxi + ny * vyi + nz * vzi;
        double n_dot_vj = nx * vxj + ny * vyj + nz * vzj;

        // Gravitational potential terms
        double gmi_over_r = G * mi * inv_dist;
        double gmj_over_r = G * mj * inv_dist;

        // 1PN coefficient: G*mj / (r^2 * c^2)
        double prefactor = gmj_over_r * inv_dist * inv_c2;

        // Term 1 (along n-hat)
        double term1 = -vi2 - 2.0 * vj2 + 4.0 * vi_dot_vj
                       + 1.5 * n_dot_vj * n_dot_vj
                       + 5.0 * gmi_over_r + 4.0 * gmj_over_r;

        // Term 2 (along v_i - v_j)
        double term2 = 4.0 * n_dot_vi - 3.0 * n_dot_vj;

        dax += prefactor * (term1 * nx + term2 * dvx);
        day += prefactor * (term1 * ny + term2 * dvy);
        daz += prefactor * (term1 * nz + term2 * dvz);
    }
}

} // namespace celestial::physics
