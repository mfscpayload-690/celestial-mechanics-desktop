#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>

namespace celestial::hooks {

/// Phase 13 extension point interfaces.
/// These define injection points for future astrophysical features
/// without implementing them now. The engine calls these hooks at
/// appropriate points in the simulation loop.

/// Injection point for accretion disk particle streams.
/// Phase 13 will implement particle generation around massive bodies.
struct CELESTIAL_API AccretionHook {
    virtual ~AccretionHook() = default;

    /// Called after force computation to inject accretion forces.
    /// @param pos_x/y/z  Position arrays (read)
    /// @param vel_x/y/z  Velocity arrays (read)
    /// @param mass        Mass array (read/write for accretion)
    /// @param acc_x/y/z   Acceleration arrays (read/write)
    /// @param is_active   Active flags (read/write)
    /// @param count       Number of bodies
    /// @param dt          Current timestep
    virtual void apply(
        double* pos_x, double* pos_y, double* pos_z,
        double* vel_x, double* vel_y, double* vel_z,
        double* mass, double* acc_x, double* acc_y, double* acc_z,
        u8* is_active, i32 count, double dt) = 0;

    /// True if this hook is active.
    virtual bool is_enabled() const { return false; }
};

/// Injection point for radiation pressure forces.
/// Phase 13 will add radiation pressure from luminous bodies.
struct CELESTIAL_API RadiationPressureHook {
    virtual ~RadiationPressureHook() = default;

    /// Called after gravitational force computation.
    /// Adds radiation pressure acceleration to acc arrays.
    virtual void apply(
        const double* pos_x, const double* pos_y, const double* pos_z,
        const double* mass, const double* radius,
        double* acc_x, double* acc_y, double* acc_z,
        const u8* is_active, i32 count) = 0;

    virtual bool is_enabled() const { return false; }
};

/// Injection point for relativistic mass adjustments.
/// Phase 13 will modify effective gravitational mass at high velocities.
struct CELESTIAL_API RelativisticMassHook {
    virtual ~RelativisticMassHook() = default;

    /// Called before force computation to compute effective masses.
    /// @param vel_x/y/z   Velocity arrays (read)
    /// @param rest_mass    Rest mass array (read)
    /// @param eff_mass     Effective mass output (write)
    /// @param count        Number of bodies
    virtual void compute_effective_mass(
        const double* vel_x, const double* vel_y, const double* vel_z,
        const double* rest_mass, double* eff_mass,
        const u8* is_active, i32 count) = 0;

    virtual bool is_enabled() const { return false; }
};

/// Injection point for variable precision zones.
/// Phase 13 will allow different precision levels in different
/// spatial regions (e.g., higher precision near black holes).
struct CELESTIAL_API PrecisionZoneHook {
    virtual ~PrecisionZoneHook() = default;

    /// Called before force computation to determine precision zones.
    /// @param pos_x/y/z   Position arrays (read)
    /// @param zone_ids     Output zone ID per body (write)
    /// @param count        Number of bodies
    virtual void classify_zones(
        const double* pos_x, const double* pos_y, const double* pos_z,
        i32* zone_ids, const u8* is_active, i32 count) = 0;

    /// Get the softening override for a given zone.
    virtual double zone_softening(i32 zone_id) const { (void)zone_id; return -1.0; }

    /// Get the theta override for a given zone.
    virtual double zone_theta(i32 zone_id) const { (void)zone_id; return -1.0; }

    virtual bool is_enabled() const { return false; }
};

/// Injection point for black hole event horizon detection.
/// Phase 13 will detect when bodies cross event horizons
/// and handle absorption/spaghettification.
struct CELESTIAL_API EventHorizonHook {
    virtual ~EventHorizonHook() = default;

    /// Called after position update to check for event horizon crossings.
    /// Can deactivate absorbed bodies by clearing is_active flags.
    virtual void detect_crossings(
        double* pos_x, double* pos_y, double* pos_z,
        double* vel_x, double* vel_y, double* vel_z,
        double* mass, u8* is_active,
        i32 count, double dt) = 0;

    virtual bool is_enabled() const { return false; }
};

/// Registry of Phase 13 hooks. Engine holds a pointer to this.
/// All hook pointers are null by default (no-op). Phase 13 sets them.
struct CELESTIAL_API Phase13Hooks {
    AccretionHook*          accretion = nullptr;
    RadiationPressureHook*  radiation_pressure = nullptr;
    RelativisticMassHook*   relativistic_mass = nullptr;
    PrecisionZoneHook*      precision_zones = nullptr;
    EventHorizonHook*       event_horizon = nullptr;

    bool has_any_hook() const {
        return (accretion && accretion->is_enabled()) ||
               (radiation_pressure && radiation_pressure->is_enabled()) ||
               (relativistic_mass && relativistic_mass->is_enabled()) ||
               (precision_zones && precision_zones->is_enabled()) ||
               (event_horizon && event_horizon->is_enabled());
    }
};

} // namespace celestial::hooks
