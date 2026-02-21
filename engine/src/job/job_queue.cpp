#include <celestial/job/job_queue.hpp>

namespace celestial::job {

// --------------------------------------------------------------------------
// PriorityRing
// --------------------------------------------------------------------------

bool PriorityRing::try_push(Job* job) {
    usize t = tail_.load(std::memory_order_relaxed);
    usize next = (t + 1) & MASK;
    usize h = head_.load(std::memory_order_acquire);

    if (next == h) {
        return false; // Full
    }

    buffer_[t] = job;
    tail_.store(next, std::memory_order_release);
    return true;
}

Job* PriorityRing::try_pop() {
    usize h = head_.load(std::memory_order_relaxed);
    usize t = tail_.load(std::memory_order_acquire);

    if (h == t) {
        return nullptr; // Empty
    }

    Job* job = buffer_[h];
    usize next = (h + 1) & MASK;
    head_.store(next, std::memory_order_release);
    return job;
}

bool PriorityRing::empty() const {
    return head_.load(std::memory_order_acquire) == tail_.load(std::memory_order_acquire);
}

// --------------------------------------------------------------------------
// JobQueue
// --------------------------------------------------------------------------

void JobQueue::push(Job* job) {
    int priority = static_cast<int>(job->priority);
    if (priority < 0) priority = 0;
    if (priority >= NUM_PRIORITY_LEVELS) priority = NUM_PRIORITY_LEVELS - 1;

    // Spin until push succeeds (queue full is unlikely with 4096 capacity)
    while (!rings_[priority].try_push(job)) {
        // Could yield here, but queue full is exceptional
    }
}

Job* JobQueue::pop() {
    // Drain highest priority first
    for (int p = 0; p < NUM_PRIORITY_LEVELS; p++) {
        Job* job = rings_[p].try_pop();
        if (job) return job;
    }
    return nullptr;
}

bool JobQueue::empty() const {
    for (int p = 0; p < NUM_PRIORITY_LEVELS; p++) {
        if (!rings_[p].empty()) return false;
    }
    return true;
}

} // namespace celestial::job
