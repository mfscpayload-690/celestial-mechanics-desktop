#include <celestial/profile/memory_tracker.hpp>

namespace celestial::profile {

MemoryTracker& MemoryTracker::instance() {
    static MemoryTracker tracker;
    return tracker;
}

} // namespace celestial::profile
