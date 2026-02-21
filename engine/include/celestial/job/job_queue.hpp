#pragma once

#include <atomic>
#include <celestial/job/job.hpp>
#include <celestial/core/types.hpp>

namespace celestial::job {

/// Lock-free bounded ring buffer for one priority level.
/// Uses compare_exchange_weak for concurrent push/pop.
class alignas(CELESTIAL_CACHE_LINE) PriorityRing {
public:
    static constexpr usize CAPACITY = 4096;
    static constexpr usize MASK = CAPACITY - 1;

    bool try_push(Job* job);
    Job* try_pop();
    bool empty() const;

private:
    alignas(CELESTIAL_CACHE_LINE) std::atomic<usize> head_{0};
    alignas(CELESTIAL_CACHE_LINE) std::atomic<usize> tail_{0};
    Job* buffer_[CAPACITY]{};
};

/// Multi-priority job queue. Drains highest priority first.
class JobQueue {
public:
    /// Push a job to its priority-level queue.
    void push(Job* job);

    /// Pop the highest-priority available job. Returns nullptr if all empty.
    Job* pop();

    /// Check if all queues are empty.
    bool empty() const;

private:
    PriorityRing rings_[NUM_PRIORITY_LEVELS];
};

} // namespace celestial::job
