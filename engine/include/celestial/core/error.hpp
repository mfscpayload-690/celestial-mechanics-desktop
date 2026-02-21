#pragma once

#include <stdexcept>
#include <string>
#include <cstdint>

namespace celestial::core {

enum class ErrorCode : std::int32_t {
    Success = 0,
    CudaError = -1,
    OutOfMemory = -2,
    InvalidArgument = -3,
    PoolExhausted = -4,
    DeviceNotFound = -5,
    NotInitialized = -6,
    AlreadyInitialized = -7,
};

/// Convert error code to human-readable string.
const char* error_string(ErrorCode code) noexcept;

/// Exception type carrying an ErrorCode plus optional context.
class CelestialException : public std::runtime_error {
public:
    CelestialException(ErrorCode code, const char* message,
                       const char* file = nullptr, int line = 0);

    ErrorCode code() const noexcept { return code_; }
    const char* file() const noexcept { return file_; }
    int line() const noexcept { return line_; }

private:
    ErrorCode code_;
    const char* file_;
    int line_;
};

} // namespace celestial::core
