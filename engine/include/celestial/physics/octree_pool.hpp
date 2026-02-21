#pragma once

#include <celestial/physics/octree_node.hpp>
#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>

namespace celestial::physics {

/// Pool allocator for OctreeNode. Flat array, zero per-frame allocation.
/// Matches C# OctreePool pattern.
class CELESTIAL_API OctreePool {
public:
    OctreePool() = default;
    explicit OctreePool(i32 initial_capacity);
    ~OctreePool();

    // Move-only
    OctreePool(OctreePool&& other) noexcept;
    OctreePool& operator=(OctreePool&& other) noexcept;
    OctreePool(const OctreePool&) = delete;
    OctreePool& operator=(const OctreePool&) = delete;

    /// Allocate a node, initialize with given center/half-size.
    /// Returns index into the nodes array.
    i32 allocate(double cx, double cy, double cz, double half_size);

    /// Reset the pool for reuse (no deallocation, just resets count).
    void reset();

    /// Ensure the pool can hold at least 'cap' nodes.
    void ensure_capacity(i32 cap);

    OctreeNode* nodes() { return nodes_; }
    const OctreeNode* nodes() const { return nodes_; }

    OctreeNode& operator[](i32 index) { return nodes_[index]; }
    const OctreeNode& operator[](i32 index) const { return nodes_[index]; }

    i32 count() const { return count_; }
    i32 capacity() const { return capacity_; }

private:
    void grow(i32 new_cap);

    OctreeNode* nodes_ = nullptr;
    i32 count_ = 0;
    i32 capacity_ = 0;
};

} // namespace celestial::physics
