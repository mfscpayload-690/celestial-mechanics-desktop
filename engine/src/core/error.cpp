#include <celestial/core/error.hpp>
#include <string>

namespace celestial::core {

const char* error_string(ErrorCode code) noexcept {
    switch (code) {
        case ErrorCode::Success:            return "Success";
        case ErrorCode::CudaError:          return "CUDA error";
        case ErrorCode::OutOfMemory:        return "Out of memory";
        case ErrorCode::InvalidArgument:    return "Invalid argument";
        case ErrorCode::PoolExhausted:      return "Pool exhausted";
        case ErrorCode::DeviceNotFound:     return "CUDA device not found";
        case ErrorCode::NotInitialized:     return "Engine not initialized";
        case ErrorCode::AlreadyInitialized: return "Engine already initialized";
        default:                            return "Unknown error";
    }
}

CelestialException::CelestialException(ErrorCode code, const char* message,
                                       const char* file, int line)
    : std::runtime_error(
          std::string(error_string(code)) + ": " + message +
          (file ? " [" + std::string(file) + ":" + std::to_string(line) + "]" : ""))
    , code_(code)
    , file_(file)
    , line_(line)
{
}

} // namespace celestial::core
