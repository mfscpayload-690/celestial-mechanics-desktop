#pragma once

// --------------------------------------------------------------------------
// OS detection
// --------------------------------------------------------------------------
#if defined(_WIN32) || defined(_WIN64)
    #define CELESTIAL_PLATFORM_WINDOWS 1
#elif defined(__linux__)
    #define CELESTIAL_PLATFORM_LINUX 1
#elif defined(__APPLE__)
    #define CELESTIAL_PLATFORM_MACOS 1
#endif

// --------------------------------------------------------------------------
// DLL export/import macros
// --------------------------------------------------------------------------
#if defined(CELESTIAL_STATIC)
    #define CELESTIAL_API
#elif defined(CELESTIAL_PLATFORM_WINDOWS)
    #ifdef CELESTIAL_EXPORTS
        #define CELESTIAL_API __declspec(dllexport)
    #else
        #define CELESTIAL_API __declspec(dllimport)
    #endif
#else
    #define CELESTIAL_API __attribute__((visibility("default")))
#endif

// --------------------------------------------------------------------------
// CUDA host/device annotations
// --------------------------------------------------------------------------
#ifdef __CUDACC__
    #define CELESTIAL_HOST_DEVICE __host__ __device__
    #define CELESTIAL_DEVICE     __device__
    #define CELESTIAL_GLOBAL     __global__
    #define CELESTIAL_HAS_CUDA   1
#else
    #define CELESTIAL_HOST_DEVICE
    #define CELESTIAL_DEVICE
    #define CELESTIAL_GLOBAL
    #define CELESTIAL_HAS_CUDA   0
#endif

// --------------------------------------------------------------------------
// Alignment helpers
// --------------------------------------------------------------------------
#define CELESTIAL_CACHE_LINE 64
#define CELESTIAL_ALIGNED(n) alignas(n)

// --------------------------------------------------------------------------
// Force inline
// --------------------------------------------------------------------------
#if defined(_MSC_VER)
    #define CELESTIAL_FORCE_INLINE __forceinline
#elif defined(__GNUC__) || defined(__clang__)
    #define CELESTIAL_FORCE_INLINE __attribute__((always_inline)) inline
#else
    #define CELESTIAL_FORCE_INLINE inline
#endif
