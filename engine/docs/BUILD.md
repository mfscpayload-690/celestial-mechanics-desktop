# Build Guide

## Prerequisites

| Requirement | Minimum Version | Notes |
|---|---|---|
| CMake | 3.25+ | Required for CUDA language support |
| C++ Compiler | C++20 support | MSVC 2022, GCC 12+, Clang 15+ |
| CUDA Toolkit | 11.5+ | Required. fp64 support needed |
| GPU | Turing+ (SM 75+) | GTX 1650+, RTX 2060+, or newer |
| Google Test | 1.14.0 | Auto-fetched by CMake (FetchContent) |

## Target CUDA Architectures

The build targets SM 75 through SM 90:

```cmake
set(CMAKE_CUDA_ARCHITECTURES "75;80;86;89;90")
```

| SM | Generation | Example GPUs |
|----|------------|-------------|
| 75 | Turing | GTX 1650, RTX 2060/2070/2080 |
| 80 | Ampere | A100, RTX 3090 |
| 86 | Ampere | RTX 3060/3070/3080 |
| 89 | Ada Lovelace | RTX 4070/4080/4090 |
| 90 | Hopper | H100 |

## Build Configuration

### CMake Options

| Option | Default | Description |
|--------|---------|-------------|
| `CELESTIAL_BUILD_TESTS` | ON | Build unit test executable |
| `CELESTIAL_BUILD_BENCH` | OFF | Build benchmark executable |
| `CELESTIAL_BUILD_SHARED` | ON | Build shared library for C# P/Invoke |

### Build Targets

| Target | Type | Output | Purpose |
|--------|------|--------|---------|
| `celestial_engine_static` | Static lib | `libcelestial_engine_static.a` | Link into tests and benchmarks |
| `celestial_engine` | Shared lib | `celestial_engine.dll` / `.so` | P/Invoke from .NET |
| `celestial_tests` | Executable | `celestial_tests.exe` | Google Test runner |

### Compile Definitions

| Define | Applied To | Purpose |
|--------|-----------|---------|
| `CELESTIAL_STATIC` | Static lib | Marks internal linkage |
| `CELESTIAL_EXPORTS` | Shared lib | Enables `__declspec(dllexport)` |
| `CELESTIAL_HAS_CUDA` | Both | Enables CUDA code paths (auto-set by CMake CUDA language) |

## Build Commands

### Windows (MSVC)

```bash
# Configure
cmake -B build -G "Visual Studio 17 2022" -A x64 engine

# Build (Release)
cmake --build build --config Release

# Build (Debug)
cmake --build build --config Debug

# Run tests
cd build && ctest --output-on-failure -C Release
```

### Linux (GCC/Clang)

```bash
# Configure
cmake -B build -DCMAKE_BUILD_TYPE=Release engine

# Build
cmake --build build -j$(nproc)

# Run tests
cd build && ctest --output-on-failure
```

## Compiler Warnings

### MSVC
```
/W4 /permissive- /Zc:__cplusplus
```
Plus CUDA diagnostic `--diag-suppress=20012` (unnamed type used in `extern "C"` function).

### GCC/Clang
```
-Wall -Wextra -Wpedantic
```

## Project Structure

```
engine/
├── CMakeLists.txt              Root build file
├── include/celestial/          Public headers (44 files)
├── src/                        Implementation (36 files: 20 .cpp + 16 .cu)
├── tests/
│   ├── CMakeLists.txt          Test build config (FetchContent for gtest)
│   └── *.cpp                   11 test files
├── bench/                      Benchmarks (optional, requires CELESTIAL_BUILD_BENCH)
└── docs/                       Engineering documentation
```

## CUDA Configuration

### Separable Compilation

Both static and shared libraries are built with `CUDA_SEPARABLE_COMPILATION ON`.
This is required because CUDA device code calls across translation units (e.g.,
`gpu_tree.cu` calls device functions defined in `gpu_reduction.cu`).

### CUDA Standard

```cmake
set(CMAKE_CUDA_STANDARD 20)
set(CMAKE_CUDA_STANDARD_REQUIRED ON)
```

## Test Framework

Tests use Google Test 1.14.0, fetched automatically via CMake FetchContent.
On Windows, `gtest_force_shared_crt` is enabled to prevent CRT mismatch warnings.

All test sources in `engine/tests/*.cpp` are compiled into a single `celestial_tests`
executable. Test discovery uses `gtest_discover_tests()` for CTest integration.

## Output Files

After build, the key output files are:

| File | Location | Purpose |
|------|----------|---------|
| `celestial_engine.dll` | `build/Release/` | Shared library for C# P/Invoke |
| `celestial_tests.exe` | `build/tests/Release/` | Test runner |
| `celestial_engine_static.lib` | `build/Release/` | Static library for C++ consumers |
