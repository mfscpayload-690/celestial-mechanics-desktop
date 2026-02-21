#include <celestial/physics/particle_system.hpp>
#include <celestial/core/error.hpp>
#include <cstring>
#include <cstdlib>

#if CELESTIAL_HAS_CUDA
#include <cuda_runtime.h>
#include <celestial/cuda/cuda_check.hpp>
#endif

namespace celestial::physics {

void* ParticleSystem::alloc_array(usize bytes) {
#if CELESTIAL_HAS_CUDA
    void* ptr = nullptr;
    cudaError_t err = cudaMallocHost(&ptr, bytes);
    if (err == cudaSuccess) {
        using_pinned_ = true;
        return ptr;
    }
    // Fallback to aligned alloc if CUDA pinned fails
#endif

#if defined(_WIN32)
    void* ptr2 = _aligned_malloc(bytes, CELESTIAL_CACHE_LINE);
#else
    void* ptr2 = std::aligned_alloc(CELESTIAL_CACHE_LINE, bytes);
#endif
    if (!ptr2) {
        throw core::CelestialException(
            core::ErrorCode::OutOfMemory, "ParticleSystem allocation failed");
    }
    return ptr2;
}

void ParticleSystem::free_array(void* ptr) {
    if (!ptr) return;

#if CELESTIAL_HAS_CUDA
    if (using_pinned_) {
        cudaFreeHost(ptr);
        return;
    }
#endif

#if defined(_WIN32)
    _aligned_free(ptr);
#else
    std::free(ptr);
#endif
}

void ParticleSystem::allocate(i32 cap) {
    if (cap <= 0) {
        throw core::CelestialException(
            core::ErrorCode::InvalidArgument, "Capacity must be positive");
    }
    if (capacity > 0) {
        free();
    }

    capacity = cap;
    count = 0;

    usize dsize = sizeof(double) * static_cast<usize>(cap);
    usize u8size = sizeof(u8) * static_cast<usize>(cap);
    usize i32size = sizeof(i32) * static_cast<usize>(cap);

    pos_x     = static_cast<double*>(alloc_array(dsize));
    pos_y     = static_cast<double*>(alloc_array(dsize));
    pos_z     = static_cast<double*>(alloc_array(dsize));

    vel_x     = static_cast<double*>(alloc_array(dsize));
    vel_y     = static_cast<double*>(alloc_array(dsize));
    vel_z     = static_cast<double*>(alloc_array(dsize));

    acc_x     = static_cast<double*>(alloc_array(dsize));
    acc_y     = static_cast<double*>(alloc_array(dsize));
    acc_z     = static_cast<double*>(alloc_array(dsize));

    old_acc_x = static_cast<double*>(alloc_array(dsize));
    old_acc_y = static_cast<double*>(alloc_array(dsize));
    old_acc_z = static_cast<double*>(alloc_array(dsize));

    mass           = static_cast<double*>(alloc_array(dsize));
    radius         = static_cast<double*>(alloc_array(dsize));
    density        = static_cast<double*>(alloc_array(dsize));
    is_active      = static_cast<u8*>(alloc_array(u8size));
    is_collidable  = static_cast<u8*>(alloc_array(u8size));
    body_type_index = static_cast<i32*>(alloc_array(i32size));

    clear();
}

void ParticleSystem::free() {
    free_array(pos_x);     pos_x = nullptr;
    free_array(pos_y);     pos_y = nullptr;
    free_array(pos_z);     pos_z = nullptr;
    free_array(vel_x);     vel_x = nullptr;
    free_array(vel_y);     vel_y = nullptr;
    free_array(vel_z);     vel_z = nullptr;
    free_array(acc_x);     acc_x = nullptr;
    free_array(acc_y);     acc_y = nullptr;
    free_array(acc_z);     acc_z = nullptr;
    free_array(old_acc_x); old_acc_x = nullptr;
    free_array(old_acc_y); old_acc_y = nullptr;
    free_array(old_acc_z); old_acc_z = nullptr;
    free_array(mass);           mass = nullptr;
    free_array(radius);         radius = nullptr;
    free_array(density);        density = nullptr;
    free_array(is_active);      is_active = nullptr;
    free_array(is_collidable);  is_collidable = nullptr;
    free_array(body_type_index); body_type_index = nullptr;

    count = 0;
    capacity = 0;
    using_pinned_ = false;
}

void ParticleSystem::set_count(i32 n) {
    if (n < 0 || n > capacity) {
        throw core::CelestialException(
            core::ErrorCode::InvalidArgument, "Count exceeds capacity");
    }
    count = n;
}

void ParticleSystem::zero_accelerations() {
    usize bytes = sizeof(double) * static_cast<usize>(count);
    std::memset(acc_x, 0, bytes);
    std::memset(acc_y, 0, bytes);
    std::memset(acc_z, 0, bytes);
}

void ParticleSystem::rotate_accelerations() {
    usize bytes = sizeof(double) * static_cast<usize>(count);
    std::memcpy(old_acc_x, acc_x, bytes);
    std::memcpy(old_acc_y, acc_y, bytes);
    std::memcpy(old_acc_z, acc_z, bytes);
}

void ParticleSystem::clear() {
    usize dsize = sizeof(double) * static_cast<usize>(capacity);
    usize u8size = sizeof(u8) * static_cast<usize>(capacity);
    usize i32size = sizeof(i32) * static_cast<usize>(capacity);

    std::memset(pos_x, 0, dsize);     std::memset(pos_y, 0, dsize);     std::memset(pos_z, 0, dsize);
    std::memset(vel_x, 0, dsize);     std::memset(vel_y, 0, dsize);     std::memset(vel_z, 0, dsize);
    std::memset(acc_x, 0, dsize);     std::memset(acc_y, 0, dsize);     std::memset(acc_z, 0, dsize);
    std::memset(old_acc_x, 0, dsize); std::memset(old_acc_y, 0, dsize); std::memset(old_acc_z, 0, dsize);
    std::memset(mass, 0, dsize);      std::memset(radius, 0, dsize);    std::memset(density, 0, dsize);
    std::memset(is_active, 0, u8size); std::memset(is_collidable, 0, u8size);
    std::memset(body_type_index, 0, i32size);
}

ParticleSystem::ParticleSystem(ParticleSystem&& other) noexcept
    : pos_x(other.pos_x), pos_y(other.pos_y), pos_z(other.pos_z)
    , vel_x(other.vel_x), vel_y(other.vel_y), vel_z(other.vel_z)
    , acc_x(other.acc_x), acc_y(other.acc_y), acc_z(other.acc_z)
    , old_acc_x(other.old_acc_x), old_acc_y(other.old_acc_y), old_acc_z(other.old_acc_z)
    , mass(other.mass), radius(other.radius), density(other.density)
    , is_active(other.is_active), is_collidable(other.is_collidable)
    , body_type_index(other.body_type_index)
    , count(other.count), capacity(other.capacity)
    , using_pinned_(other.using_pinned_)
{
    other.pos_x = nullptr; other.pos_y = nullptr; other.pos_z = nullptr;
    other.vel_x = nullptr; other.vel_y = nullptr; other.vel_z = nullptr;
    other.acc_x = nullptr; other.acc_y = nullptr; other.acc_z = nullptr;
    other.old_acc_x = nullptr; other.old_acc_y = nullptr; other.old_acc_z = nullptr;
    other.mass = nullptr; other.radius = nullptr; other.density = nullptr;
    other.is_active = nullptr; other.is_collidable = nullptr; other.body_type_index = nullptr;
    other.count = 0; other.capacity = 0;
}

ParticleSystem& ParticleSystem::operator=(ParticleSystem&& other) noexcept {
    if (this != &other) {
        free();
        pos_x = other.pos_x; pos_y = other.pos_y; pos_z = other.pos_z;
        vel_x = other.vel_x; vel_y = other.vel_y; vel_z = other.vel_z;
        acc_x = other.acc_x; acc_y = other.acc_y; acc_z = other.acc_z;
        old_acc_x = other.old_acc_x; old_acc_y = other.old_acc_y; old_acc_z = other.old_acc_z;
        mass = other.mass; radius = other.radius; density = other.density;
        is_active = other.is_active; is_collidable = other.is_collidable;
        body_type_index = other.body_type_index;
        count = other.count; capacity = other.capacity;
        using_pinned_ = other.using_pinned_;

        other.pos_x = nullptr; other.pos_y = nullptr; other.pos_z = nullptr;
        other.vel_x = nullptr; other.vel_y = nullptr; other.vel_z = nullptr;
        other.acc_x = nullptr; other.acc_y = nullptr; other.acc_z = nullptr;
        other.old_acc_x = nullptr; other.old_acc_y = nullptr; other.old_acc_z = nullptr;
        other.mass = nullptr; other.radius = nullptr; other.density = nullptr;
        other.is_active = nullptr; other.is_collidable = nullptr; other.body_type_index = nullptr;
        other.count = 0; other.capacity = 0;
    }
    return *this;
}

} // namespace celestial::physics
