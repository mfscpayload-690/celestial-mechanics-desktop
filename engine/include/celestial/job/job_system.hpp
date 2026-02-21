#pragma once

#include <atomic>
#include <thread>
#include <vector>
#include <semaphore>
#include <celestial/job/job.hpp>
#include <celestial/job/job_queue.hpp>
#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>

namespace celestial::job {

/// Thread pool + job scheduler.
/// Workers drain the queue, execute jobs, and decrement parent counters.
class CELESTIAL_API JobSystem {
public:
    /// Get the singleton instance.
    static JobSystem& instance();

    /// Initialize workers. num_workers = -1 means hardware_concurrency - 1.
    void init(int num_workers = -1);

    /// Shutdown all worker threads.
    void shutdown();

    bool is_initialized() const { return initialized_; }
    int num_workers() const { return static_cast<int>(workers_.size()); }

    /// Allocate a job from the pool. Sets unfinished_jobs = 1.
    Job* create_job(Job::WorkFn fn, const void* data = nullptr,
                    JobPriority priority = JobPriority::Normal);

    /// Allocate a child job. Increments parent's unfinished_jobs.
    Job* create_child_job(Job* parent, Job::WorkFn fn, const void* data = nullptr,
                          JobPriority priority = JobPriority::Normal);

    /// Submit a job for execution.
    void submit(Job* job);

    /// Wait for a job to complete. While waiting, help by executing other jobs.
    void wait(const Job* job);

    /// Check if a job is complete (unfinished_jobs == 0).
    static bool is_complete(const Job* job);

private:
    JobSystem() = default;
    ~JobSystem();

    JobSystem(const JobSystem&) = delete;
    JobSystem& operator=(const JobSystem&) = delete;

    void worker_loop(std::stop_token stoken);
    void execute_job(Job* job);
    Job* allocate_job();

    // Job pool: pre-allocated ring buffer
    static constexpr usize JOB_POOL_SIZE = 16384;
    static constexpr usize JOB_POOL_MASK = JOB_POOL_SIZE - 1;
    Job job_pool_[JOB_POOL_SIZE]{};
    std::atomic<usize> pool_index_{0};

    JobQueue queue_;
    std::vector<std::jthread> workers_;
    std::counting_semaphore<> semaphore_{0};
    std::atomic<bool> initialized_{false};
};

} // namespace celestial::job
