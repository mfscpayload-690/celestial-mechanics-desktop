#include <celestial/sim/config_serializer.hpp>
#include <sstream>
#include <cstring>
#include <cstdlib>
#include <cctype>

namespace celestial::sim {

// ── Serialization ───────────────────────────────────────────────────────

static void write_key(std::ostringstream& os, const char* key) {
    os << "\"" << key << "\":";
}

static void write_double(std::ostringstream& os, const char* key, double val, bool comma = true) {
    write_key(os, key);
    os << val;
    if (comma) os << ",";
}

static void write_int(std::ostringstream& os, const char* key, int64_t val, bool comma = true) {
    write_key(os, key);
    os << val;
    if (comma) os << ",";
}

static void write_bool(std::ostringstream& os, const char* key, bool val, bool comma = true) {
    write_key(os, key);
    os << (val ? "true" : "false");
    if (comma) os << ",";
}

std::string ConfigSerializer::to_json(const SimulationConfig& c) {
    std::ostringstream os;
    os.precision(17);  // Full double precision
    os << std::scientific;

    os << "{";

    // Top-level scalars
    write_double(os, "dt", c.dt);
    write_double(os, "softening", c.softening);
    write_double(os, "theta", c.theta);
    write_bool(os, "enable_pn", c.enable_pn);
    write_bool(os, "enable_collisions", c.enable_collisions);
    write_bool(os, "deterministic", c.deterministic);
    write_int(os, "deterministic_seed", static_cast<int64_t>(c.deterministic_seed));
    write_int(os, "max_particles", c.max_particles);
    write_int(os, "max_steps_per_frame", c.max_steps_per_frame);
    write_int(os, "gpu_memory_pool_size", static_cast<int64_t>(c.gpu_memory_pool_size));
    write_double(os, "max_velocity_fraction_c", c.max_velocity_fraction_c);
    write_double(os, "schwarz_warning_factor", c.schwarz_warning_factor);
    write_int(os, "integrator", static_cast<int64_t>(c.integrator));
    write_int(os, "compute_mode", static_cast<int64_t>(c.compute_mode));
    write_bool(os, "enable_diagnostics", c.enable_diagnostics);
    write_bool(os, "enable_gpu_validation", c.enable_gpu_validation);
    write_double(os, "gpu_validation_tolerance", c.gpu_validation_tolerance);

    // Adaptive timestep sub-object
    os << "\"adaptive_dt\":{";
    write_bool(os, "enabled", c.adaptive_dt.enabled);
    write_double(os, "eta", c.adaptive_dt.eta);
    write_double(os, "dt_min", c.adaptive_dt.dt_min);
    write_double(os, "dt_max", c.adaptive_dt.dt_max);
    write_double(os, "initial_dt", c.adaptive_dt.initial_dt, false);
    os << "},";

    // Softening config sub-object
    os << "\"softening_config\":{";
    write_int(os, "mode", static_cast<int64_t>(c.softening_config.mode));
    write_double(os, "global_softening", c.softening_config.global_softening);
    write_double(os, "adaptive_scale", c.softening_config.adaptive_scale);
    write_double(os, "adaptive_min", c.softening_config.adaptive_min, false);
    os << "},";

    // Collision config sub-object
    os << "\"collision_config\":{";
    write_int(os, "mode", static_cast<int64_t>(c.collision_config.mode));
    write_double(os, "restitution", c.collision_config.restitution);
    write_int(os, "max_merges_per_frame", c.collision_config.max_merges_per_frame);
    write_int(os, "max_merges_per_body", c.collision_config.max_merges_per_body);
    write_bool(os, "density_preserving_merge", c.collision_config.density_preserving_merge, false);
    os << "},";

    // Density config sub-object
    os << "\"density_config\":{";
    write_double(os, "default_density", c.density_config.default_density);
    write_double(os, "min_radius", c.density_config.min_radius, false);
    os << "}";

    os << "}";
    return os.str();
}

// ── Deserialization (minimal hand-rolled JSON parser) ───────────────────

namespace {

struct Parser {
    const char* p;
    const char* end;

    void skip_ws() {
        while (p < end && std::isspace(static_cast<unsigned char>(*p))) ++p;
    }

    bool expect(char ch) {
        skip_ws();
        if (p < end && *p == ch) { ++p; return true; }
        return false;
    }

    bool parse_string(std::string& out) {
        skip_ws();
        if (p >= end || *p != '"') return false;
        ++p;
        out.clear();
        while (p < end && *p != '"') {
            if (*p == '\\' && p + 1 < end) {
                ++p;
                out += *p++;
            } else {
                out += *p++;
            }
        }
        if (p >= end) return false;
        ++p; // skip closing quote
        return true;
    }

    bool parse_double(double& out) {
        skip_ws();
        char* end_ptr = nullptr;
        out = std::strtod(p, &end_ptr);
        if (end_ptr == p) return false;
        p = end_ptr;
        return true;
    }

    bool parse_int64(int64_t& out) {
        skip_ws();
        char* end_ptr = nullptr;
        out = std::strtoll(p, &end_ptr, 10);
        if (end_ptr == p) return false;
        p = end_ptr;
        return true;
    }

    bool parse_bool(bool& out) {
        skip_ws();
        if (end - p >= 4 && std::strncmp(p, "true", 4) == 0) {
            out = true; p += 4; return true;
        }
        if (end - p >= 5 && std::strncmp(p, "false", 5) == 0) {
            out = false; p += 5; return true;
        }
        return false;
    }

    // Skip any JSON value (string, number, bool, null, object, array)
    bool skip_value() {
        skip_ws();
        if (p >= end) return false;
        if (*p == '"') { std::string tmp; return parse_string(tmp); }
        if (*p == '{') return skip_object();
        if (*p == '[') return skip_array();
        if (*p == 't' || *p == 'f') { bool tmp; return parse_bool(tmp); }
        if (*p == 'n' && end - p >= 4 && std::strncmp(p, "null", 4) == 0) { p += 4; return true; }
        // number
        double tmp; return parse_double(tmp);
    }

    bool skip_object() {
        if (!expect('{')) return false;
        skip_ws();
        if (p < end && *p == '}') { ++p; return true; }
        while (true) {
            std::string key;
            if (!parse_string(key)) return false;
            if (!expect(':')) return false;
            if (!skip_value()) return false;
            skip_ws();
            if (p < end && *p == ',') { ++p; continue; }
            break;
        }
        return expect('}');
    }

    bool skip_array() {
        if (!expect('[')) return false;
        skip_ws();
        if (p < end && *p == ']') { ++p; return true; }
        while (true) {
            if (!skip_value()) return false;
            skip_ws();
            if (p < end && *p == ',') { ++p; continue; }
            break;
        }
        return expect(']');
    }
};

// Helper to parse a flat set of key-value pairs from an object
using FieldHandler = bool(*)(Parser& parser, void* ctx, const std::string& key);

bool parse_object(Parser& parser, FieldHandler handler, void* ctx) {
    if (!parser.expect('{')) return false;
    parser.skip_ws();
    if (parser.p < parser.end && *parser.p == '}') { ++parser.p; return true; }

    while (true) {
        std::string key;
        if (!parser.parse_string(key)) return false;
        if (!parser.expect(':')) return false;
        if (!handler(parser, ctx, key)) return false;
        parser.skip_ws();
        if (parser.p < parser.end && *parser.p == ',') { ++parser.p; continue; }
        break;
    }
    return parser.expect('}');
}

bool handle_adaptive_dt(Parser& parser, void* ctx, const std::string& key) {
    auto* cfg = static_cast<AdaptiveTimestepConfig*>(ctx);
    if (key == "enabled") return parser.parse_bool(cfg->enabled);
    if (key == "eta") return parser.parse_double(cfg->eta);
    if (key == "dt_min") return parser.parse_double(cfg->dt_min);
    if (key == "dt_max") return parser.parse_double(cfg->dt_max);
    if (key == "initial_dt") return parser.parse_double(cfg->initial_dt);
    return parser.skip_value();
}

bool handle_softening(Parser& parser, void* ctx, const std::string& key) {
    auto* cfg = static_cast<celestial::physics::SofteningConfig*>(ctx);
    if (key == "mode") { int64_t v; if (!parser.parse_int64(v)) return false; cfg->mode = static_cast<celestial::physics::SofteningMode>(v); return true; }
    if (key == "global_softening") return parser.parse_double(cfg->global_softening);
    if (key == "adaptive_scale") return parser.parse_double(cfg->adaptive_scale);
    if (key == "adaptive_min") return parser.parse_double(cfg->adaptive_min);
    return parser.skip_value();
}

bool handle_collision(Parser& parser, void* ctx, const std::string& key) {
    auto* cfg = static_cast<celestial::physics::CollisionResolverConfig*>(ctx);
    if (key == "mode") { int64_t v; if (!parser.parse_int64(v)) return false; cfg->mode = static_cast<celestial::physics::CollisionMode>(v); return true; }
    if (key == "restitution") return parser.parse_double(cfg->restitution);
    if (key == "max_merges_per_frame") { int64_t v; if (!parser.parse_int64(v)) return false; cfg->max_merges_per_frame = static_cast<celestial::core::i32>(v); return true; }
    if (key == "max_merges_per_body") { int64_t v; if (!parser.parse_int64(v)) return false; cfg->max_merges_per_body = static_cast<celestial::core::i32>(v); return true; }
    if (key == "density_preserving_merge") return parser.parse_bool(cfg->density_preserving_merge);
    return parser.skip_value();
}

bool handle_density(Parser& parser, void* ctx, const std::string& key) {
    auto* cfg = static_cast<celestial::physics::DensityConfig*>(ctx);
    if (key == "default_density") return parser.parse_double(cfg->default_density);
    if (key == "min_radius") return parser.parse_double(cfg->min_radius);
    return parser.skip_value();
}

bool handle_root(Parser& parser, void* ctx, const std::string& key) {
    auto* c = static_cast<SimulationConfig*>(ctx);

    if (key == "dt") return parser.parse_double(c->dt);
    if (key == "softening") return parser.parse_double(c->softening);
    if (key == "theta") return parser.parse_double(c->theta);
    if (key == "enable_pn") return parser.parse_bool(c->enable_pn);
    if (key == "enable_collisions") return parser.parse_bool(c->enable_collisions);
    if (key == "deterministic") return parser.parse_bool(c->deterministic);
    if (key == "deterministic_seed") {
        int64_t v; if (!parser.parse_int64(v)) return false;
        c->deterministic_seed = static_cast<celestial::core::u64>(v); return true;
    }
    if (key == "max_particles") {
        int64_t v; if (!parser.parse_int64(v)) return false;
        c->max_particles = static_cast<celestial::core::i32>(v); return true;
    }
    if (key == "max_steps_per_frame") {
        int64_t v; if (!parser.parse_int64(v)) return false;
        c->max_steps_per_frame = static_cast<int>(v); return true;
    }
    if (key == "gpu_memory_pool_size") {
        int64_t v; if (!parser.parse_int64(v)) return false;
        c->gpu_memory_pool_size = static_cast<celestial::core::usize>(v); return true;
    }
    if (key == "max_velocity_fraction_c") return parser.parse_double(c->max_velocity_fraction_c);
    if (key == "schwarz_warning_factor") return parser.parse_double(c->schwarz_warning_factor);
    if (key == "integrator") {
        int64_t v; if (!parser.parse_int64(v)) return false;
        c->integrator = static_cast<IntegratorType>(v); return true;
    }
    if (key == "compute_mode") {
        int64_t v; if (!parser.parse_int64(v)) return false;
        c->compute_mode = static_cast<SimulationConfig::ComputeMode>(v); return true;
    }
    if (key == "enable_diagnostics") return parser.parse_bool(c->enable_diagnostics);
    if (key == "enable_gpu_validation") return parser.parse_bool(c->enable_gpu_validation);
    if (key == "gpu_validation_tolerance") return parser.parse_double(c->gpu_validation_tolerance);

    // Sub-objects
    if (key == "adaptive_dt") return parse_object(parser, handle_adaptive_dt, &c->adaptive_dt);
    if (key == "softening_config") return parse_object(parser, handle_softening, &c->softening_config);
    if (key == "collision_config") return parse_object(parser, handle_collision, &c->collision_config);
    if (key == "density_config") return parse_object(parser, handle_density, &c->density_config);

    return parser.skip_value();
}

} // anonymous namespace

bool ConfigSerializer::from_json(const std::string& json, SimulationConfig& config) {
    Parser parser{json.c_str(), json.c_str() + json.size()};
    return parse_object(parser, handle_root, &config);
}

} // namespace celestial::sim
