#pragma once

#include <celestial/sim/simulation_config.hpp>
#include <string>

namespace celestial::sim {

/// Lightweight JSON serializer/deserializer for SimulationConfig.
/// No external JSON library — hand-rolled for the ~25 scalar fields.
/// Produces minified JSON output suitable for C# interop.
class ConfigSerializer {
public:
    /// Serialize a SimulationConfig to a JSON string.
    static std::string to_json(const SimulationConfig& config);

    /// Deserialize a JSON string into a SimulationConfig.
    /// Unknown keys are silently ignored. Missing keys retain defaults.
    /// Returns true on success, false on parse error.
    static bool from_json(const std::string& json, SimulationConfig& config);
};

} // namespace celestial::sim
