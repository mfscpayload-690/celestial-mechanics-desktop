#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>
#include <atomic>

namespace celestial::profile {

/// Simple memory allocation tracker.
class CELESTIAL_API MemoryTracker {
public:
    static MemoryTracker& instance();

    void record_host_alloc(i64 bytes) {
        host_allocated_ += bytes;
        i64 current = host_allocated_.load();
        i64 peak = peak_host_.load();
        while (current > peak && !peak_host_.compare_exchange_weak(peak, current)) {}
    }

    void record_host_free(i64 bytes) { host_allocated_ -= bytes; }

    void record_device_alloc(i64 bytes) {
        device_allocated_ += bytes;
        i64 current = device_allocated_.load();
        i64 peak = peak_device_.load();
        while (current > peak && !peak_device_.compare_exchange_weak(peak, current)) {}
    }

    void record_device_free(i64 bytes) { device_allocated_ -= bytes; }

    void record_pinned_alloc(i64 bytes) { pinned_allocated_ += bytes; }
    void record_pinned_free(i64 bytes) { pinned_allocated_ -= bytes; }

    i64 host_allocated() const { return host_allocated_.load(); }
    i64 device_allocated() const { return device_allocated_.load(); }
    i64 pinned_allocated() const { return pinned_allocated_.load(); }
    i64 peak_host() const { return peak_host_.load(); }
    i64 peak_device() const { return peak_device_.load(); }

    void reset() {
        host_allocated_ = 0; device_allocated_ = 0; pinned_allocated_ = 0;
        peak_host_ = 0; peak_device_ = 0;
    }

private:
    MemoryTracker() = default;

    std::atomic<i64> host_allocated_{0};
    std::atomic<i64> device_allocated_{0};
    std::atomic<i64> pinned_allocated_{0};
    std::atomic<i64> peak_host_{0};
    std::atomic<i64> peak_device_{0};
};

} // namespace celestial::profile
