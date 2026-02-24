#pragma once

#include <celestial/core/types.hpp>
#include <celestial/core/platform.hpp>

namespace celestial::sim {

/// Yoshida 4th-order symmetric integrator coefficients.
/// Forest-Ruth (1990) decomposition: 3 leapfrog sub-stages per full step.
///
/// The composition uses w1, w0, w1 as sub-step dt multipliers where:
///   w1 = 1/(2 - 2^(1/3))
///   w0 = -2^(1/3)/(2 - 2^(1/3))
///   2*w1 + w0 = 1  (total dt preserved)
///
/// Each full Yoshida step executes 3 KDK leapfrog sub-steps with dt_sub = w[i] * dt.
/// This yields 4th-order accuracy in position and velocity while remaining symplectic.
struct YoshidaCoefficients {
    static constexpr double CBRT2 = 1.2599210498948732;  // cbrt(2.0)
    static constexpr double W1 =  1.0 / (2.0 - CBRT2);  //  1.3512071919596578
    static constexpr double W0 = -CBRT2 / (2.0 - CBRT2); // -1.7024143839193153

    /// Sub-step dt multipliers (3 sub-steps).
    static constexpr double SUB_DT[3] = { W1, W0, W1 };

    /// Number of sub-steps per full Yoshida step.
    static constexpr int NUM_STAGES = 3;
};

} // namespace celestial::sim
