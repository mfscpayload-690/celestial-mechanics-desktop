cmake_minimum_required(VERSION 3.25)
project(CelestialEngine LANGUAGES CXX CUDA)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_CUDA_STANDARD 20)
set(CMAKE_CUDA_STANDARD_REQUIRED ON)

# CUDA architecture targets (Turing+, where fp64 is available)
set(CMAKE_CUDA_ARCHITECTURES "75;80;86;89;90")

# Options
option(CELESTIAL_BUILD_TESTS "Build unit tests" ON)
option(CELESTIAL_BUILD_BENCH "Build benchmarks" OFF)
option(CELESTIAL_BUILD_SHARED "Build shared library for C# interop" ON)

# Compiler warnings
if(MSVC)
    add_compile_options(/W4 /permissive- /Zc:__cplusplus)
    add_compile_options($<$<COMPILE_LANGUAGE:CUDA>:--diag-suppress=20012>)
else()
    add_compile_options(-Wall -Wextra -Wpedantic)
endif()

# Include paths
set(CELESTIAL_INCLUDE_DIR ${CMAKE_CURRENT_SOURCE_DIR}/include)

# Collect sources
file(GLOB_RECURSE CPP_SOURCES CONFIGURE_DEPENDS
    ${CMAKE_CURRENT_SOURCE_DIR}/src/*.cpp
)
file(GLOB_RECURSE CU_SOURCES CONFIGURE_DEPENDS
    ${CMAKE_CURRENT_SOURCE_DIR}/src/*.cu
)

# Static library (core engine)
add_library(celestial_engine_static STATIC ${CPP_SOURCES} ${CU_SOURCES})
target_include_directories(celestial_engine_static PUBLIC ${CELESTIAL_INCLUDE_DIR})
set_target_properties(celestial_engine_static PROPERTIES
    CUDA_SEPARABLE_COMPILATION ON
    POSITION_INDEPENDENT_CODE ON
)
target_compile_definitions(celestial_engine_static PRIVATE CELESTIAL_STATIC)

# Shared library (for C# P/Invoke)
if(CELESTIAL_BUILD_SHARED)
    add_library(celestial_engine SHARED ${CPP_SOURCES} ${CU_SOURCES})
    target_include_directories(celestial_engine PUBLIC ${CELESTIAL_INCLUDE_DIR})
    target_compile_definitions(celestial_engine PRIVATE CELESTIAL_EXPORTS)
    set_target_properties(celestial_engine PROPERTIES
        CUDA_SEPARABLE_COMPILATION ON
        OUTPUT_NAME "celestial_engine"
    )
endif()

# Tests
if(CELESTIAL_BUILD_TESTS)
    enable_testing()
    add_subdirectory(tests)
endif()

# Benchmarks
if(CELESTIAL_BUILD_BENCH)
    add_subdirectory(bench)
endif()
