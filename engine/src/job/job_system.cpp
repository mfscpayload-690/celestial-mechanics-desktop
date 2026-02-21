#include <celestial/job/job_system.hpp>
#include <celestial/core/error.hpp>
#include <cstring>

namespace celestial::job {

JobSystem& JobSystem::instance() {
    static JobSystem sys;
    return sys;
}

JobSystem::~JobSystem() {
    if (initialized_) {
        shutdown();
    }
}

void JobSystem::init(int num_workers) {
    if (initialized_.exchange(true)) {
        throw core::CelestialException(
            core::ErrorCode::AlreadyInitialized, "JobSystem already initialized");
    }

    if (num_workers < 0) {
        int hw = static_cast<int>(std::thread::hardware_concurrency());
        num_workers = (hw > 1) ? hw - 1 : 1;
    }
    if (num_workers < 1) num_workers = 1;

    pool_index_.store(0, std::memory_order_relaxed);

    workers_.reserve(static_cast<usize>(num_workers));
    for (int i = 0; i < num_workers; i++) {
        workers_.emplace_back([this](std::stop_token st) { worker_loop(st); });
    }
}

void JobSystem::shutdown() {
    if (!initialized_) return;

    // Request all workers to stop — jthread handles stop_token
    for (auto& w : workers_) {
        w.request_stop();
    }

    // Wake all workers so they can check stop_token
    for (usize i = 0; i < workers_.size() + 1; i++) {
        semaphore_.release();
    }

    // Join all workers
    workers_.clear();
    initialized_ = false;
}

Job* JobSystem::allocate_job() {
    usize idx = pool_index_.fetch_add(1, std::memory_order_relaxed) & JOB_POOL_MASK;
    Job* job = &job_pool_[idx];
    // Zero-init the job (except padding)
    job->work = nullptr;
    job->data = nullptr;
    job->parent = nullptr;
    job->unfinished_jobs.store(0, std::memory_order_relaxed);
    job->type = JobType::Physics;
    job->priority = JobPriority::Normal;
    return job;
}

Job* JobSystem::create_job(Job::WorkFn fn, const void* data, JobPriority priority) {
    Job* job = allocate_job();
    job->work = fn;
    job->data = data;
    job->priority = priority;
    job->unfinished_jobs.store(1, std::memory_order_release); // 1 = self
    return job;
}

Job* JobSystem::create_child_job(Job* parent, Job::WorkFn fn, const void* data,
                                  JobPriority priority) {
    parent->unfinished_jobs.fetch_add(1, std::memory_order_relaxed);
    Job* job = allocate_job();
    job->work = fn;
    job->data = data;
    job->parent = parent;
    job->priority = priority;
    job->unfinished_jobs.store(1, std::memory_order_release);
    return job;
}

void JobSystem::submit(Job* job) {
    queue_.push(job);
    semaphore_.release(); // Wake a worker
}

void JobSystem::execute_job(Job* job) {
    if (job->work) {
        job->work(job, job->data);
    }

    // Decrement unfinished count
    int32_t prev = job->unfinished_jobs.fetch_sub(1, std::memory_order_acq_rel);
    if (prev == 1 && job->parent) {
        // All children (and self) complete — notify parent
        int32_t parent_prev = job->parent->unfinished_jobs.fetch_sub(1, std::memory_order_acq_rel);
        (void)parent_prev;
    }
}

bool JobSystem::is_complete(const Job* job) {
    return job->unfinished_jobs.load(std::memory_order_acquire) <= 0;
}

void JobSystem::wait(const Job* job) {
    while (!is_complete(job)) {
        // While waiting, help by executing other queued jobs
        Job* next = queue_.pop();
        if (next) {
            execute_job(next);
        } else {
            // Brief yield to avoid busy-spinning when no work available
            std::this_thread::yield();
        }
    }
}

void JobSystem::worker_loop(std::stop_token stoken) {
    while (!stoken.stop_requested()) {
        Job* job = queue_.pop();
        if (job) {
            execute_job(job);
        } else {
            // Wait for work
            semaphore_.acquire();
        }
    }
}

} // namespace celestial::job
