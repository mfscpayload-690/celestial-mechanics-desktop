#pragma once

#include <atomic>
#include <cstdint>
#include <celestial/job/job_types.hpp>
#include <celestial/core/platform.hpp>

namespace celestial::job {

/// A single unit of work. Pool-allocated, never heap-allocated individually.
/// Padded to fit in 2 cache lines (128 bytes) for false-sharing prevention.
struct CELESTIAL_ALIGNED(CELESTIAL_CACHE_LINE) Job {
    /// Function pointer type: void(Job* self, const void* user_data).
    using WorkFn = void(*)(Job*, const void*);

    WorkFn work = nullptr;                       ///< The actual work function
    const void* data = nullptr;                  ///< Opaque user data pointer
    Job* parent = nullptr;                       ///< Parent job (for dependency tracking)
    std::atomic<int32_t> unfinished_jobs{0};     ///< Includes self (1 = no children)
    JobType type = JobType::Physics;
    JobPriority priority = JobPriority::Normal;
    uint8_t _padding[64 - sizeof(WorkFn) - sizeof(const void*) - sizeof(Job*)
                        - sizeof(std::atomic<int32_t>) - sizeof(JobType) - sizeof(JobPriority)]{};
};

static_assert(sizeof(Job) <= 128, "Job must fit in 2 cache lines");

} // namespace celestial::job
