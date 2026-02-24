#include <celestial/memory/gpu_pool.hpp>
#include <celestial/core/error.hpp>
#include <cstring>
#include <algorithm>

#if CELESTIAL_HAS_CUDA
#include <celestial/cuda/cuda_check.hpp>
#endif

namespace celestial::memory {

GpuPool::~GpuPool() {
    if (initialized_) destroy();
}

GpuPool::GpuPool(GpuPool&& other) noexcept
    : d_pos_x(other.d_pos_x), d_pos_y(other.d_pos_y), d_pos_z(other.d_pos_z)
    , d_vel_x(other.d_vel_x), d_vel_y(other.d_vel_y), d_vel_z(other.d_vel_z)
    , d_acc_x(other.d_acc_x), d_acc_y(other.d_acc_y), d_acc_z(other.d_acc_z)
    , d_old_acc_x(other.d_old_acc_x), d_old_acc_y(other.d_old_acc_y), d_old_acc_z(other.d_old_acc_z)
    , d_mass(other.d_mass), d_radius(other.d_radius)
    , d_is_active(other.d_is_active), d_body_type(other.d_body_type)
    , d_softening(other.d_softening), d_is_collidable(other.d_is_collidable)
    , d_morton_codes(other.d_morton_codes), d_sorted_indices(other.d_sorted_indices)
    , d_bbox_min(other.d_bbox_min), d_bbox_max(other.d_bbox_max)
    , capacity_(other.capacity_), count_(other.count_)
    , device_bytes_(other.device_bytes_), initialized_(other.initialized_)
    , d_scratch_(other.d_scratch_), scratch_size_(other.scratch_size_)
    , scratch_offset_(other.scratch_offset_)
{
    other.d_pos_x = nullptr; other.d_pos_y = nullptr; other.d_pos_z = nullptr;
    other.d_vel_x = nullptr; other.d_vel_y = nullptr; other.d_vel_z = nullptr;
    other.d_acc_x = nullptr; other.d_acc_y = nullptr; other.d_acc_z = nullptr;
    other.d_old_acc_x = nullptr; other.d_old_acc_y = nullptr; other.d_old_acc_z = nullptr;
    other.d_mass = nullptr; other.d_radius = nullptr;
    other.d_is_active = nullptr; other.d_body_type = nullptr;
    other.d_softening = nullptr; other.d_is_collidable = nullptr;
    other.d_morton_codes = nullptr; other.d_sorted_indices = nullptr;
    other.d_bbox_min = nullptr; other.d_bbox_max = nullptr;
    other.d_scratch_ = nullptr; other.scratch_size_ = 0; other.scratch_offset_ = 0;
    other.capacity_ = 0; other.count_ = 0;
    other.device_bytes_ = 0; other.initialized_ = false;
}

GpuPool& GpuPool::operator=(GpuPool&& other) noexcept {
    if (this != &other) {
        if (initialized_) destroy();

        d_pos_x = other.d_pos_x; d_pos_y = other.d_pos_y; d_pos_z = other.d_pos_z;
        d_vel_x = other.d_vel_x; d_vel_y = other.d_vel_y; d_vel_z = other.d_vel_z;
        d_acc_x = other.d_acc_x; d_acc_y = other.d_acc_y; d_acc_z = other.d_acc_z;
        d_old_acc_x = other.d_old_acc_x; d_old_acc_y = other.d_old_acc_y; d_old_acc_z = other.d_old_acc_z;
        d_mass = other.d_mass; d_radius = other.d_radius;
        d_is_active = other.d_is_active; d_body_type = other.d_body_type;
        d_softening = other.d_softening; d_is_collidable = other.d_is_collidable;
        d_morton_codes = other.d_morton_codes; d_sorted_indices = other.d_sorted_indices;
        d_bbox_min = other.d_bbox_min; d_bbox_max = other.d_bbox_max;
        d_scratch_ = other.d_scratch_; scratch_size_ = other.scratch_size_;
        scratch_offset_ = other.scratch_offset_;
        capacity_ = other.capacity_; count_ = other.count_;
        device_bytes_ = other.device_bytes_; initialized_ = other.initialized_;

        other.d_pos_x = nullptr; other.d_pos_y = nullptr; other.d_pos_z = nullptr;
        other.d_vel_x = nullptr; other.d_vel_y = nullptr; other.d_vel_z = nullptr;
        other.d_acc_x = nullptr; other.d_acc_y = nullptr; other.d_acc_z = nullptr;
        other.d_old_acc_x = nullptr; other.d_old_acc_y = nullptr; other.d_old_acc_z = nullptr;
        other.d_mass = nullptr; other.d_radius = nullptr;
        other.d_is_active = nullptr; other.d_body_type = nullptr;
        other.d_softening = nullptr; other.d_is_collidable = nullptr;
        other.d_morton_codes = nullptr; other.d_sorted_indices = nullptr;
        other.d_bbox_min = nullptr; other.d_bbox_max = nullptr;
        other.d_scratch_ = nullptr; other.scratch_size_ = 0; other.scratch_offset_ = 0;
        other.capacity_ = 0; other.count_ = 0;
        other.device_bytes_ = 0; other.initialized_ = false;
    }
    return *this;
}

void GpuPool::allocate_arrays(i32 cap) {
#if CELESTIAL_HAS_CUDA
    usize dsize = sizeof(double) * static_cast<usize>(cap);
    usize u8size = sizeof(u8) * static_cast<usize>(cap);
    usize i32size = sizeof(i32) * static_cast<usize>(cap);
    usize u64size = sizeof(u64) * static_cast<usize>(cap);

    // Position (3 arrays)
    CUDA_CHECK(cudaMalloc(&d_pos_x, dsize));
    CUDA_CHECK(cudaMalloc(&d_pos_y, dsize));
    CUDA_CHECK(cudaMalloc(&d_pos_z, dsize));

    // Velocity (3 arrays)
    CUDA_CHECK(cudaMalloc(&d_vel_x, dsize));
    CUDA_CHECK(cudaMalloc(&d_vel_y, dsize));
    CUDA_CHECK(cudaMalloc(&d_vel_z, dsize));

    // Acceleration current (3 arrays)
    CUDA_CHECK(cudaMalloc(&d_acc_x, dsize));
    CUDA_CHECK(cudaMalloc(&d_acc_y, dsize));
    CUDA_CHECK(cudaMalloc(&d_acc_z, dsize));

    // Acceleration old (3 arrays)
    CUDA_CHECK(cudaMalloc(&d_old_acc_x, dsize));
    CUDA_CHECK(cudaMalloc(&d_old_acc_y, dsize));
    CUDA_CHECK(cudaMalloc(&d_old_acc_z, dsize));

    // Scalar fields
    CUDA_CHECK(cudaMalloc(&d_mass, dsize));
    CUDA_CHECK(cudaMalloc(&d_radius, dsize));
    CUDA_CHECK(cudaMalloc(&d_is_active, u8size));
    CUDA_CHECK(cudaMalloc(&d_body_type, i32size));

    // Phase 13: Adaptive softening + collidable flag
    CUDA_CHECK(cudaMalloc(&d_softening, dsize));
    CUDA_CHECK(cudaMalloc(&d_is_collidable, u8size));

    // Morton codes and sorted indices
    CUDA_CHECK(cudaMalloc(&d_morton_codes, u64size));
    CUDA_CHECK(cudaMalloc(&d_sorted_indices, i32size));

    // Bounding box reduction buffers (enough for grid dim)
    i32 max_blocks = (cap + 255) / 256;
    usize bbox_size = sizeof(double) * static_cast<usize>(max_blocks);
    CUDA_CHECK(cudaMalloc(&d_bbox_min, bbox_size * 3)); // min_x, min_y, min_z
    CUDA_CHECK(cudaMalloc(&d_bbox_max, bbox_size * 3)); // max_x, max_y, max_z

    // Zero all arrays
    CUDA_CHECK(cudaMemset(d_pos_x, 0, dsize));
    CUDA_CHECK(cudaMemset(d_pos_y, 0, dsize));
    CUDA_CHECK(cudaMemset(d_pos_z, 0, dsize));
    CUDA_CHECK(cudaMemset(d_vel_x, 0, dsize));
    CUDA_CHECK(cudaMemset(d_vel_y, 0, dsize));
    CUDA_CHECK(cudaMemset(d_vel_z, 0, dsize));
    CUDA_CHECK(cudaMemset(d_acc_x, 0, dsize));
    CUDA_CHECK(cudaMemset(d_acc_y, 0, dsize));
    CUDA_CHECK(cudaMemset(d_acc_z, 0, dsize));
    CUDA_CHECK(cudaMemset(d_old_acc_x, 0, dsize));
    CUDA_CHECK(cudaMemset(d_old_acc_y, 0, dsize));
    CUDA_CHECK(cudaMemset(d_old_acc_z, 0, dsize));
    CUDA_CHECK(cudaMemset(d_mass, 0, dsize));
    CUDA_CHECK(cudaMemset(d_radius, 0, dsize));
    CUDA_CHECK(cudaMemset(d_is_active, 0, u8size));
    CUDA_CHECK(cudaMemset(d_body_type, 0, i32size));
    CUDA_CHECK(cudaMemset(d_softening, 0, dsize));
    CUDA_CHECK(cudaMemset(d_is_collidable, 0, u8size));
    CUDA_CHECK(cudaMemset(d_morton_codes, 0, u64size));
    CUDA_CHECK(cudaMemset(d_sorted_indices, 0, i32size));
    CUDA_CHECK(cudaMemset(d_bbox_min, 0, bbox_size * 3));
    CUDA_CHECK(cudaMemset(d_bbox_max, 0, bbox_size * 3));

    // Track memory usage: 15 double arrays + 2 u8 + 2 i32 + 1 u64 + bbox
    device_bytes_ = static_cast<i64>(dsize) * 15
                  + static_cast<i64>(u8size) * 2
                  + static_cast<i64>(i32size) * 2
                  + static_cast<i64>(u64size)
                  + static_cast<i64>(bbox_size) * 6;

    profile::MemoryTracker::instance().record_device_alloc(device_bytes_);
#else
    (void)cap;
#endif
}

void GpuPool::free_arrays() {
#if CELESTIAL_HAS_CUDA
    auto safe_free = [](auto& ptr) {
        if (ptr) { cudaFree(ptr); ptr = nullptr; }
    };

    safe_free(d_pos_x); safe_free(d_pos_y); safe_free(d_pos_z);
    safe_free(d_vel_x); safe_free(d_vel_y); safe_free(d_vel_z);
    safe_free(d_acc_x); safe_free(d_acc_y); safe_free(d_acc_z);
    safe_free(d_old_acc_x); safe_free(d_old_acc_y); safe_free(d_old_acc_z);
    safe_free(d_mass); safe_free(d_radius);
    safe_free(d_is_active); safe_free(d_body_type);
    safe_free(d_softening); safe_free(d_is_collidable);
    safe_free(d_morton_codes); safe_free(d_sorted_indices);
    safe_free(d_bbox_min); safe_free(d_bbox_max);

    if (device_bytes_ > 0) {
        profile::MemoryTracker::instance().record_device_free(device_bytes_);
        device_bytes_ = 0;
    }
#endif
}

void GpuPool::reallocate_arrays(i32 old_cap, i32 new_cap) {
#if CELESTIAL_HAS_CUDA
    usize old_dsize = sizeof(double) * static_cast<usize>(old_cap);
    usize old_u8size = sizeof(u8) * static_cast<usize>(old_cap);
    usize old_i32size = sizeof(i32) * static_cast<usize>(old_cap);
    usize old_u64size = sizeof(u64) * static_cast<usize>(old_cap);

    usize new_dsize = sizeof(double) * static_cast<usize>(new_cap);
    usize new_u8size = sizeof(u8) * static_cast<usize>(new_cap);
    usize new_i32size = sizeof(i32) * static_cast<usize>(new_cap);
    usize new_u64size = sizeof(u64) * static_cast<usize>(new_cap);

    i32 copy_count = std::min(old_cap, new_cap);
    usize copy_dsize = sizeof(double) * static_cast<usize>(copy_count);
    usize copy_u8size = sizeof(u8) * static_cast<usize>(copy_count);
    usize copy_i32size = sizeof(i32) * static_cast<usize>(copy_count);
    usize copy_u64size = sizeof(u64) * static_cast<usize>(copy_count);

    auto realloc_array = [&](auto*& ptr, usize new_size, usize copy_size) {
        using T = std::remove_pointer_t<std::decay_t<decltype(ptr)>>;
        T* new_ptr = nullptr;
        CUDA_CHECK(cudaMalloc(&new_ptr, new_size));
        CUDA_CHECK(cudaMemset(new_ptr, 0, new_size));
        if (ptr && copy_size > 0) {
            CUDA_CHECK(cudaMemcpy(new_ptr, ptr, copy_size, cudaMemcpyDeviceToDevice));
        }
        if (ptr) cudaFree(ptr);
        ptr = new_ptr;
    };

    // Reallocate all arrays preserving data
    realloc_array(d_pos_x, new_dsize, copy_dsize);
    realloc_array(d_pos_y, new_dsize, copy_dsize);
    realloc_array(d_pos_z, new_dsize, copy_dsize);
    realloc_array(d_vel_x, new_dsize, copy_dsize);
    realloc_array(d_vel_y, new_dsize, copy_dsize);
    realloc_array(d_vel_z, new_dsize, copy_dsize);
    realloc_array(d_acc_x, new_dsize, copy_dsize);
    realloc_array(d_acc_y, new_dsize, copy_dsize);
    realloc_array(d_acc_z, new_dsize, copy_dsize);
    realloc_array(d_old_acc_x, new_dsize, copy_dsize);
    realloc_array(d_old_acc_y, new_dsize, copy_dsize);
    realloc_array(d_old_acc_z, new_dsize, copy_dsize);
    realloc_array(d_mass, new_dsize, copy_dsize);
    realloc_array(d_radius, new_dsize, copy_dsize);
    realloc_array(d_is_active, new_u8size, copy_u8size);
    realloc_array(d_body_type, new_i32size, copy_i32size);
    realloc_array(d_softening, new_dsize, copy_dsize);
    realloc_array(d_is_collidable, new_u8size, copy_u8size);
    realloc_array(d_morton_codes, new_u64size, copy_u64size);
    realloc_array(d_sorted_indices, new_i32size, copy_i32size);

    // Reallocate bbox buffers
    i32 new_max_blocks = (new_cap + 255) / 256;
    usize new_bbox_size = sizeof(double) * static_cast<usize>(new_max_blocks) * 3;
    i32 old_max_blocks = (old_cap + 255) / 256;
    usize old_bbox_copy = sizeof(double) * static_cast<usize>(std::min(old_max_blocks, new_max_blocks)) * 3;
    realloc_array(d_bbox_min, new_bbox_size, old_bbox_copy);
    realloc_array(d_bbox_max, new_bbox_size, old_bbox_copy);

    // Update memory tracking
    i64 old_bytes = device_bytes_;
    device_bytes_ = static_cast<i64>(new_dsize) * 15
                  + static_cast<i64>(new_u8size) * 2
                  + static_cast<i64>(new_i32size) * 2
                  + static_cast<i64>(new_u64size)
                  + static_cast<i64>(new_bbox_size) * 2;

    auto& tracker = profile::MemoryTracker::instance();
    if (old_bytes > 0) tracker.record_device_free(old_bytes);
    tracker.record_device_alloc(device_bytes_);
#else
    (void)old_cap;
    (void)new_cap;
#endif
}

void GpuPool::init(i32 initial_capacity) {
    if (initialized_) {
        throw core::CelestialException(
            core::ErrorCode::AlreadyInitialized, "GpuPool already initialized");
    }
    if (initial_capacity <= 0) {
        initial_capacity = INITIAL_CAPACITY;
    }

    allocate_arrays(initial_capacity);
    capacity_ = initial_capacity;
    count_ = 0;
    initialized_ = true;
}

void GpuPool::init(i32 initial_capacity, usize scratch_bytes) {
    init(initial_capacity);
    if (scratch_bytes > 0) {
        init_scratch(scratch_bytes);
    }
}

void GpuPool::destroy() {
    if (!initialized_) return;

#if CELESTIAL_HAS_CUDA
    cudaDeviceSynchronize();
#endif

    free_arrays();

#if CELESTIAL_HAS_CUDA
    if (d_scratch_) {
        profile::MemoryTracker::instance().record_device_free(static_cast<i64>(scratch_size_));
        cudaFree(d_scratch_);
        d_scratch_ = nullptr;
    }
#endif
    scratch_size_ = 0;
    scratch_offset_ = 0;

    capacity_ = 0;
    count_ = 0;
    initialized_ = false;
}

void GpuPool::init_scratch(usize bytes) {
#if CELESTIAL_HAS_CUDA
    if (d_scratch_) {
        profile::MemoryTracker::instance().record_device_free(static_cast<i64>(scratch_size_));
        cudaFree(d_scratch_);
        d_scratch_ = nullptr;
    }
    if (bytes > 0) {
        CUDA_CHECK(cudaMalloc(&d_scratch_, bytes));
        CUDA_CHECK(cudaMemset(d_scratch_, 0, bytes));
        scratch_size_ = bytes;
        scratch_offset_ = 0;
        device_bytes_ += static_cast<i64>(bytes);
        profile::MemoryTracker::instance().record_device_alloc(static_cast<i64>(bytes));
    }
#else
    (void)bytes;
#endif
}

void* GpuPool::scratch_alloc(usize bytes, usize alignment) {
    if (bytes == 0 || !d_scratch_) return nullptr;

    // Align offset up to requested alignment
    usize aligned_offset = (scratch_offset_ + alignment - 1) & ~(alignment - 1);
    if (aligned_offset + bytes > scratch_size_) {
        return nullptr; // Insufficient scratch space
    }

    void* ptr = static_cast<char*>(d_scratch_) + aligned_offset;
    scratch_offset_ = aligned_offset + bytes;
    return ptr;
}

void GpuPool::expand(i32 required_capacity) {
    if (required_capacity <= capacity_) return;
    if (required_capacity > MAX_CAPACITY) {
        throw core::CelestialException(
            core::ErrorCode::InvalidArgument, "Requested capacity exceeds MAX_CAPACITY");
    }

    // Round up to next EXPANSION_BLOCK boundary
    i32 new_cap = capacity_;
    while (new_cap < required_capacity) {
        new_cap += EXPANSION_BLOCK;
    }
    new_cap = std::min(new_cap, MAX_CAPACITY);

    i32 old_cap = capacity_;
    reallocate_arrays(old_cap, new_cap);
    capacity_ = new_cap;
}

void GpuPool::set_count(i32 n) {
    if (n < 0) {
        throw core::CelestialException(
            core::ErrorCode::InvalidArgument, "Count must be non-negative");
    }
    if (n > capacity_) {
        expand(n);
    }
    count_ = n;
}

#if CELESTIAL_HAS_CUDA

void GpuPool::upload_all(
    const double* hpx, const double* hpy, const double* hpz,
    const double* hvx, const double* hvy, const double* hvz,
    const double* hax, const double* hay, const double* haz,
    const double* hoax, const double* hoay, const double* hoaz,
    const double* hm, const double* hr,
    const u8* h_active, i32 n, cudaStream_t stream)
{
    if (n > capacity_) expand(n);
    count_ = n;
    usize dsize = sizeof(double) * static_cast<usize>(n);
    usize u8size = sizeof(u8) * static_cast<usize>(n);

    CUDA_CHECK(cudaMemcpyAsync(d_pos_x, hpx, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_pos_y, hpy, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_pos_z, hpz, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_vel_x, hvx, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_vel_y, hvy, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_vel_z, hvz, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_acc_x, hax, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_acc_y, hay, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_acc_z, haz, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_old_acc_x, hoax, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_old_acc_y, hoay, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_old_acc_z, hoaz, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_mass, hm, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_radius, hr, dsize, cudaMemcpyHostToDevice, stream));
    CUDA_CHECK(cudaMemcpyAsync(d_is_active, h_active, u8size, cudaMemcpyHostToDevice, stream));
}

void GpuPool::download_state(
    double* hpx, double* hpy, double* hpz,
    double* hvx, double* hvy, double* hvz,
    double* hax, double* hay, double* haz,
    i32 n, cudaStream_t stream)
{
    usize dsize = sizeof(double) * static_cast<usize>(n);
    CUDA_CHECK(cudaMemcpyAsync(hpx, d_pos_x, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hpy, d_pos_y, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hpz, d_pos_z, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hvx, d_vel_x, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hvy, d_vel_y, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hvz, d_vel_z, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hax, d_acc_x, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hay, d_acc_y, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(haz, d_acc_z, dsize, cudaMemcpyDeviceToHost, stream));
}

void GpuPool::download_old_acc(
    double* hoax, double* hoay, double* hoaz,
    i32 n, cudaStream_t stream)
{
    usize dsize = sizeof(double) * static_cast<usize>(n);
    CUDA_CHECK(cudaMemcpyAsync(hoax, d_old_acc_x, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hoay, d_old_acc_y, dsize, cudaMemcpyDeviceToHost, stream));
    CUDA_CHECK(cudaMemcpyAsync(hoaz, d_old_acc_z, dsize, cudaMemcpyDeviceToHost, stream));
}

void GpuPool::zero_accelerations(i32 n, cudaStream_t stream) {
    usize dsize = sizeof(double) * static_cast<usize>(n);
    CUDA_CHECK(cudaMemsetAsync(d_acc_x, 0, dsize, stream));
    CUDA_CHECK(cudaMemsetAsync(d_acc_y, 0, dsize, stream));
    CUDA_CHECK(cudaMemsetAsync(d_acc_z, 0, dsize, stream));
}

#endif // CELESTIAL_HAS_CUDA

} // namespace celestial::memory
