include(FetchContent)

FetchContent_Declare(
    googletest
    GIT_REPOSITORY https://github.com/google/googletest.git
    GIT_TAG        v1.14.0
)

# Prevent overriding the parent project's compiler/linker settings on Windows
set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
FetchContent_MakeAvailable(googletest)

# Collect test sources
file(GLOB TEST_SOURCES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/*.cpp)

add_executable(celestial_tests ${TEST_SOURCES})
target_link_libraries(celestial_tests PRIVATE
    celestial_engine_static
    GTest::gtest_main
)

include(GoogleTest)
gtest_discover_tests(celestial_tests)

# ── Phase 21: Test Category Labels ────────────────────────────────────────
# Usage: ctest -L core | ctest -L physics | ctest -L integration | ctest -L validation
# Or:    ctest -L "core|physics" (multiple labels)
#
# Since gtest_discover_tests names tests at CTest time, we use per-category
# executables that can be built/run independently.

# Category: core (math primitives, timestep, job system)
add_executable(test_core
    test_vec3d.cpp
    test_timestep.cpp
    test_job_system.cpp
)
target_link_libraries(test_core PRIVATE celestial_engine_static GTest::gtest_main)
gtest_discover_tests(test_core TEST_PREFIX "core/" PROPERTIES LABELS "core")

# Category: physics (gravity, octree)
add_executable(test_physics
    test_gravity.cpp
    test_octree.cpp
)
target_link_libraries(test_physics PRIVATE celestial_engine_static GTest::gtest_main)
gtest_discover_tests(test_physics TEST_PREFIX "physics/" PROPERTIES LABELS "physics")

# Category: integration (engine + phase tests)
add_executable(test_integration
    test_engine.cpp
    test_phase13.cpp
    test_phase14_15.cpp
    test_phase16_17.cpp
    test_phase18_19.cpp
)
target_link_libraries(test_integration PRIVATE celestial_engine_static GTest::gtest_main)
gtest_discover_tests(test_integration TEST_PREFIX "integration/" PROPERTIES LABELS "integration")

# Category: validation (scientific certification tests)
add_executable(test_validation_suite
    test_validation.cpp
)
target_link_libraries(test_validation_suite PRIVATE celestial_engine_static GTest::gtest_main)
gtest_discover_tests(test_validation_suite TEST_PREFIX "validation/" PROPERTIES LABELS "validation")

