#include <celestial/physics/octree_pool.hpp>
#include <celestial/core/error.hpp>
#include <cstring>
#include <cstdlib>
#include <algorithm>

namespace celestial::physics {

OctreePool::OctreePool(i32 initial_capacity) {
    ensure_capacity(initial_capacity);
}

OctreePool::~OctreePool() {
    std::free(nodes_);
    nodes_ = nullptr;
    count_ = 0;
    capacity_ = 0;
}

OctreePool::OctreePool(OctreePool&& other) noexcept
    : nodes_(other.nodes_), count_(other.count_), capacity_(other.capacity_)
{
    other.nodes_ = nullptr;
    other.count_ = 0;
    other.capacity_ = 0;
}

OctreePool& OctreePool::operator=(OctreePool&& other) noexcept {
    if (this != &other) {
        std::free(nodes_);
        nodes_ = other.nodes_;
        count_ = other.count_;
        capacity_ = other.capacity_;
        other.nodes_ = nullptr;
        other.count_ = 0;
        other.capacity_ = 0;
    }
    return *this;
}

void OctreePool::grow(i32 new_cap) {
    OctreeNode* new_nodes = static_cast<OctreeNode*>(
        std::realloc(nodes_, sizeof(OctreeNode) * static_cast<usize>(new_cap)));
    if (!new_nodes) {
        throw core::CelestialException(
            core::ErrorCode::OutOfMemory, "OctreePool grow failed");
    }
    nodes_ = new_nodes;
    capacity_ = new_cap;
}

void OctreePool::ensure_capacity(i32 cap) {
    if (cap <= capacity_) return;
    i32 new_cap = std::max(cap, capacity_ * 2);
    if (new_cap < 64) new_cap = 64;
    grow(new_cap);
}

i32 OctreePool::allocate(double cx, double cy, double cz, double half_size) {
    if (count_ >= capacity_) {
        ensure_capacity(count_ + 1);
    }
    i32 idx = count_++;
    nodes_[idx].init(cx, cy, cz, half_size);
    return idx;
}

void OctreePool::reset() {
    count_ = 0;
}

} // namespace celestial::physics
