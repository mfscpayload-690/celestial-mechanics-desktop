#include <celestial/sim/async_pipeline.hpp>
#include <celestial/cuda/cuda_check.hpp>

// Forward declarations for kernel launch functions
namespace celestial::cuda {
    void launch_gravity_kernel(
        double* d_pos_x, double* d_pos_y, double* d_pos_z,
        double* d_mass, uint8_t* d_is_active,
        double* d_acc_x, double* d_acc_y, double* d_acc_z,
        int32_t n, double softening, cudaStream_t stream);

    void launch_kick_drift(
        double* d_pos_x, double* d_pos_y, double* d_pos_z,
        double* d_vel_x, double* d_vel_y, double* d_vel_z,
        double* d_old_acc_x, double* d_old_acc_y, double* d_old_acc_z,
        uint8_t* d_is_active, int32_t n, double dt, cudaStream_t stream);

    void launch_kick_rotate(
        double* d_vel_x, double* d_vel_y, double* d_vel_z,
        double* d_acc_x, double* d_acc_y, double* d_acc_z,
        double* d_old_acc_x, double* d_old_acc_y, double* d_old_acc_z,
        uint8_t* d_is_active, int32_t n, double dt, cudaStream_t stream);

    void launch_pn_correction(
        double* d_pos_x, double* d_pos_y, double* d_pos_z,
        double* d_vel_x, double* d_vel_y, double* d_vel_z,
        double* d_mass, uint8_t* d_is_active,
        double* d_acc_x, double* d_acc_y, double* d_acc_z,
        int32_t n, double softening,
        double max_velocity_fraction_c,
        double schwarz_warning_factor,
        cudaStream_t stream);
}

namespace celestial::sim {

void AsyncPipeline::init(i32 capacity) {
    if (initialized_) return;

    for (int i = 0; i < 2; i++) {
        device_buf_[i].allocate(capacity);
        CUDA_CHECK(cudaStreamCreateWithFlags(&streams_[i], cudaStreamNonBlocking));
        CUDA_CHECK(cudaEventCreateWithFlags(&compute_done_[i], cudaEventDisableTiming));
    }

    current_ = 0;
    first_frame_ = true;
    initialized_ = true;
}

void AsyncPipeline::destroy() {
    if (!initialized_) return;

    // Sync before destroying
    cudaDeviceSynchronize();

    for (int i = 0; i < 2; i++) {
        device_buf_[i].free();
        if (streams_[i]) { cudaStreamDestroy(streams_[i]); streams_[i] = nullptr; }
        if (compute_done_[i]) { cudaEventDestroy(compute_done_[i]); compute_done_[i] = nullptr; }
    }

    initialized_ = false;
}

void AsyncPipeline::submit_step(
    const celestial::physics::ParticleSystem& host,
    double softening, double dt, bool enable_pn)
{
    auto& db = device_buf_[current_];
    cudaStream_t stream = streams_[current_];
    i32 n = host.count;

    // 1. Upload all state from host to device
    db.upload_all(
        host.pos_x, host.pos_y, host.pos_z,
        host.vel_x, host.vel_y, host.vel_z,
        host.acc_x, host.acc_y, host.acc_z,
        host.old_acc_x, host.old_acc_y, host.old_acc_z,
        host.mass, host.radius, host.is_active,
        n, stream);

    // 2. Phase 1+2: Half-kick + Drift
    cuda::launch_kick_drift(
        db.d_pos_x, db.d_pos_y, db.d_pos_z,
        db.d_vel_x, db.d_vel_y, db.d_vel_z,
        db.d_old_acc_x, db.d_old_acc_y, db.d_old_acc_z,
        db.d_is_active, n, dt, stream);

    // 3. Phase 3: Compute new accelerations (gravity)
    cuda::launch_gravity_kernel(
        db.d_pos_x, db.d_pos_y, db.d_pos_z,
        db.d_mass, db.d_is_active,
        db.d_acc_x, db.d_acc_y, db.d_acc_z,
        n, softening, stream);

    // 3b. Optional: Post-Newtonian corrections
    if (enable_pn) {
        cuda::launch_pn_correction(
            db.d_pos_x, db.d_pos_y, db.d_pos_z,
            db.d_vel_x, db.d_vel_y, db.d_vel_z,
            db.d_mass, db.d_is_active,
            db.d_acc_x, db.d_acc_y, db.d_acc_z,
            n, softening, 0.3, 3.0, stream);
    }

    // 4. Phase 4+5: Second half-kick + Rotate
    cuda::launch_kick_rotate(
        db.d_vel_x, db.d_vel_y, db.d_vel_z,
        db.d_acc_x, db.d_acc_y, db.d_acc_z,
        db.d_old_acc_x, db.d_old_acc_y, db.d_old_acc_z,
        db.d_is_active, n, dt, stream);

    // 5. Download results back to host
    db.download_state(
        const_cast<double*>(host.pos_x), const_cast<double*>(host.pos_y), const_cast<double*>(host.pos_z),
        const_cast<double*>(host.vel_x), const_cast<double*>(host.vel_y), const_cast<double*>(host.vel_z),
        const_cast<double*>(host.acc_x), const_cast<double*>(host.acc_y), const_cast<double*>(host.acc_z),
        n, stream);

    // 6. Record completion event
    CUDA_CHECK(cudaEventRecord(compute_done_[current_], stream));
}

void AsyncPipeline::retrieve_results(celestial::physics::ParticleSystem& host) {
    if (first_frame_) {
        first_frame_ = false;
        return;
    }

    // Wait for the previous frame's compute to finish
    int prev = 1 - current_;
    CUDA_CHECK(cudaEventSynchronize(compute_done_[prev]));
}

} // namespace celestial::sim
