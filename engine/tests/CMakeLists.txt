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
