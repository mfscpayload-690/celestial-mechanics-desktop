#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>
#include <atomic>

namespace celestial::sim {

/// Deterministic simulation mode configuration and enforcement.
/// When enabled, ensures bit-exact reproducibility across runs:
/// - Fixed floating-point rounding mode
/// - Stable kernel dispatch order
/// - Seeded deterministic RNG
/// - Disabled async reordering
class CELESTIAL_API DeterministicMode {
public:
    /// Enable or disable deterministic mode.
    void set_enabled(bool enabled);
    bool is_enabled() const { return enabled_; }

    /// Set the RNG seed for deterministic random operations.
    void set_seed(u64 seed) { seed_ = seed; }
    u64 seed() const { return seed_; }

    /// Get current step number (for deterministic sequencing).
    u64 step_number() const { return step_number_; }

    /// Increment step counter (call once per physics step).
    void advance_step() { step_number_++; }

    /// Reset step counter (e.g., on simulation restart).
    void reset_step() { step_number_ = 0; }

    /// Generate a deterministic u64 from seed + step + channel.
    /// Uses SplitMix64 for high-quality deterministic hashing.
    u64 deterministic_hash(u64 channel) const;

    /// Generate a deterministic u32 in [0, UINT32_MAX].
    u32 deterministic_u32(u64 channel) const;

    /// Generate a deterministic double in [0, 1).
    double deterministic_double(u64 channel) const;

    /// Force CUDA synchronization after each kernel (for determinism).
    /// Only active when deterministic mode is enabled.
    bool force_sync() const { return enabled_ && force_sync_; }
    void set_force_sync(bool sync) { force_sync_ = sync; }

private:
    bool enabled_ = false;
    bool force_sync_ = true;
    u64 seed_ = 42;
    u64 step_number_ = 0;
};

} // namespace celestial::sim
