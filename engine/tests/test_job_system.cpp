#include <gtest/gtest.h>
#include <celestial/job/job_system.hpp>
#include <atomic>
#include <thread>

using namespace celestial::job;

class JobSystemTest : public ::testing::Test {
protected:
    void SetUp() override {
        auto& js = JobSystem::instance();
        if (!js.is_initialized()) {
            js.init(4); // 4 worker threads
        }
    }
};

TEST_F(JobSystemTest, CreateAndRunJob) {
    auto& js = JobSystem::instance();
    std::atomic<int> counter{0};

    auto* job = js.create_job(
        [](Job*, const void* data) {
            auto* c = static_cast<const std::atomic<int>*>(data);
            const_cast<std::atomic<int>*>(c)->fetch_add(1);
        },
        &counter);

    js.submit(job);
    js.wait(job);

    EXPECT_EQ(counter.load(), 1);
}

TEST_F(JobSystemTest, MultipleJobs) {
    auto& js = JobSystem::instance();
    std::atomic<int> counter{0};

    constexpr int NUM_JOBS = 100;
    Job* jobs[NUM_JOBS];

    for (int i = 0; i < NUM_JOBS; i++) {
        jobs[i] = js.create_job(
            [](Job*, const void* data) {
                auto* c = static_cast<const std::atomic<int>*>(data);
                const_cast<std::atomic<int>*>(c)->fetch_add(1);
            },
            &counter);
    }

    for (int i = 0; i < NUM_JOBS; i++) {
        js.submit(jobs[i]);
    }

    // Wait for all
    for (int i = 0; i < NUM_JOBS; i++) {
        js.wait(jobs[i]);
    }

    EXPECT_EQ(counter.load(), NUM_JOBS);
}

TEST_F(JobSystemTest, ParentChildDependency) {
    auto& js = JobSystem::instance();
    std::atomic<int> counter{0};

    auto* parent = js.create_job(
        [](Job*, const void* data) {
            auto* c = static_cast<const std::atomic<int>*>(data);
            const_cast<std::atomic<int>*>(c)->fetch_add(1);
        },
        &counter);

    constexpr int NUM_CHILDREN = 10;
    for (int i = 0; i < NUM_CHILDREN; i++) {
        auto* child = js.create_child_job(parent,
            [](Job*, const void* data) {
                auto* c = static_cast<const std::atomic<int>*>(data);
                const_cast<std::atomic<int>*>(c)->fetch_add(1);
            },
            &counter);
        js.submit(child);
    }

    js.submit(parent);
    js.wait(parent);

    // Parent + all children should have executed
    EXPECT_EQ(counter.load(), NUM_CHILDREN + 1);
}

TEST_F(JobSystemTest, IsComplete) {
    auto& js = JobSystem::instance();

    auto* job = js.create_job(
        [](Job*, const void*) {
            // Quick job
        });

    EXPECT_FALSE(JobSystem::is_complete(job)); // Not yet submitted/run
    js.submit(job);
    js.wait(job);
    EXPECT_TRUE(JobSystem::is_complete(job));
}
