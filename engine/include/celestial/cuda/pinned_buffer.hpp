#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>
#include <celestial/cuda/cuda_check.hpp>
#include <utility>

#if CELESTIAL_HAS_CUDA
#include <cuda_runtime.h>
#endif

namespace celestial::cuda {

/// RAII wrapper for CUDA pinned (page-locked) host memory.
/// Pinned memory enables async GPU transfers via cudaMemcpyAsync.
template <typename T>
class PinnedBuffer {
public:
    PinnedBuffer() = default;

    explicit PinnedBuffer(i32 count) : count_(count) {
        if (count_ > 0) {
#if CELESTIAL_HAS_CUDA
            CUDA_CHECK(cudaMallocHost(&data_, sizeof(T) * count_));
#else
            data_ = static_cast<T*>(std::aligned_alloc(CELESTIAL_CACHE_LINE, sizeof(T) * count_));
            if (!data_) throw core::CelestialException(core::ErrorCode::OutOfMemory, "PinnedBuffer allocation failed");
#endif
        }
    }

    ~PinnedBuffer() { free(); }

    // Move-only
    PinnedBuffer(PinnedBuffer&& other) noexcept
        : data_(other.data_), count_(other.count_) {
        other.data_ = nullptr;
        other.count_ = 0;
    }

    PinnedBuffer& operator=(PinnedBuffer&& other) noexcept {
        if (this != &other) {
            free();
            data_ = other.data_;
            count_ = other.count_;
            other.data_ = nullptr;
            other.count_ = 0;
        }
        return *this;
    }

    PinnedBuffer(const PinnedBuffer&) = delete;
    PinnedBuffer& operator=(const PinnedBuffer&) = delete;

    T* data() { return data_; }
    const T* data() const { return data_; }
    i32 count() const { return count_; }
    bool empty() const { return count_ == 0 || data_ == nullptr; }

    T& operator[](i32 index) { return data_[index]; }
    const T& operator[](i32 index) const { return data_[index]; }

private:
    void free() {
        if (data_) {
#if CELESTIAL_HAS_CUDA
            cudaFreeHost(data_);
#else
            std::free(data_);
#endif
            data_ = nullptr;
            count_ = 0;
        }
    }

    T* data_ = nullptr;
    i32 count_ = 0;
};

} // namespace celestial::cuda
