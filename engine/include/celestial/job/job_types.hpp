#pragma once

#include <cstdint>

namespace celestial::job {

enum class JobType : uint8_t {
    Physics   = 0,
    TreeBuild = 1,
    Rendering = 2,
    IO        = 3
};

enum class JobPriority : uint8_t {
    Critical = 0,
    High     = 1,
    Normal   = 2,
    Low      = 3
};

inline constexpr int NUM_PRIORITY_LEVELS = 4;

} // namespace celestial::job
