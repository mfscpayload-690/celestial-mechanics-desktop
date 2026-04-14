using System.Numerics;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CelestialMechanics.Renderer;

public enum EmissionTier
{
    Planet,
    Star,
    Supernova,
    Kilonova,
    BigBang,
}

public class RenderSettings : INotifyPropertyChanged
{
    private bool _enableBloom = true;
    private float _bloomIntensity = 1.2f;
    private float _bloomThreshold = 1.0f;
    private float _bloomRadius = 5.0f;

    private bool _enableParticles = true;
    private int _maxParticles = 5000;
    private float _particleEmissionScale = 1.0f;
    private bool _enableTrails = true;

    private bool _enableWaves = true;
    private float _waveEmissionScale = 1.0f;

    private bool _enableHdr = true;
    private bool _enableReflections = true;
    private bool _enableGlowScaling = true;
    private bool _enableExplosions = true;
    private bool _enableBigBangMode;

    private float _glowDistanceScale = 18.0f;
    private float _reflectionScale = 0.012f;
    private int _maxStarLights = 6;

    private float _exposure = 1.0f;
    private float _fogDensity = 0.02f;
    private Vector3 _fogColor = new(0.02f, 0.02f, 0.05f);

    private float _starEmissionMultiplier = 1.0f;
    private float _waveEmissionMultiplier = 0.6f;
    private float _particleEmissionMultiplier = 0.4f;
    private float _nebulaEmissionMultiplier = 0.2f;

    private float _maxExplosionRadius = 28.0f;
    private int _maxReflectionSamples = 8;
    private int _maxExplosionParticles = 3200;

    private bool _debugOnlyParticles;
    private bool _debugOnlyWaves;

    private bool _showGrid = true;
    private bool _showAxes = true;
    private bool _showOrbits = true;
    private bool _showGravitationalWaves = true;
    private bool _showVelocityVectors;
    private bool _showForceVectors;
    private bool _showBoundingBoxes;
    private bool _showInspector = true;
    private bool _showStatistics = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Bloom
    public bool EnableBloom { get => _enableBloom; set => SetField(ref _enableBloom, value); }
    public float BloomIntensity { get => _bloomIntensity; set => SetField(ref _bloomIntensity, value); }
    public float BloomThreshold { get => _bloomThreshold; set => SetField(ref _bloomThreshold, value); }
    public float BloomRadius { get => _bloomRadius; set => SetField(ref _bloomRadius, value); }

    // Particles
    public bool EnableParticles { get => _enableParticles; set => SetField(ref _enableParticles, value); }
    public int MaxParticles { get => _maxParticles; set => SetField(ref _maxParticles, value); }
    public float ParticleEmissionScale { get => _particleEmissionScale; set => SetField(ref _particleEmissionScale, value); }
    public bool EnableTrails { get => _enableTrails; set => SetField(ref _enableTrails, value); }

    // Gravitational Waves
    public bool EnableWaves { get => _enableWaves; set => SetField(ref _enableWaves, value); }
    public float WaveEmissionScale { get => _waveEmissionScale; set => SetField(ref _waveEmissionScale, value); }

    // Cinematic toggles
    public bool EnableHdr { get => _enableHdr; set => SetField(ref _enableHdr, value); }
    public bool EnableReflections { get => _enableReflections; set => SetField(ref _enableReflections, value); }
    public bool EnableGlowScaling { get => _enableGlowScaling; set => SetField(ref _enableGlowScaling, value); }
    public bool EnableExplosions { get => _enableExplosions; set => SetField(ref _enableExplosions, value); }
    public bool EnableBigBangMode { get => _enableBigBangMode; set => SetField(ref _enableBigBangMode, value); }

    // Distanced-based visual scaling
    public float GlowDistanceScale { get => _glowDistanceScale; set => SetField(ref _glowDistanceScale, value); }
    public float ReflectionScale { get => _reflectionScale; set => SetField(ref _reflectionScale, value); }

    // Lighting
    public int MaxStarLights { get => _maxStarLights; set => SetField(ref _maxStarLights, value); }

    // Fog & Exposure
    public float Exposure { get => _exposure; set => SetField(ref _exposure, value); }
    public float FogDensity { get => _fogDensity; set => SetField(ref _fogDensity, value); }
    public Vector3 FogColor { get => _fogColor; set => SetField(ref _fogColor, value); }

    // Emission Hierarchy
    public float StarEmissionMultiplier { get => _starEmissionMultiplier; set => SetField(ref _starEmissionMultiplier, value); }
    public float WaveEmissionMultiplier { get => _waveEmissionMultiplier; set => SetField(ref _waveEmissionMultiplier, value); }
    public float ParticleEmissionMultiplier { get => _particleEmissionMultiplier; set => SetField(ref _particleEmissionMultiplier, value); }
    public float NebulaEmissionMultiplier { get => _nebulaEmissionMultiplier; set => SetField(ref _nebulaEmissionMultiplier, value); }

    // Performance safety caps
    public float MaxExplosionRadius { get => _maxExplosionRadius; set => SetField(ref _maxExplosionRadius, value); }
    public int MaxReflectionSamples { get => _maxReflectionSamples; set => SetField(ref _maxReflectionSamples, value); }
    public int MaxExplosionParticles { get => _maxExplosionParticles; set => SetField(ref _maxExplosionParticles, value); }

    // Debug
    public bool DebugOnlyParticles { get => _debugOnlyParticles; set => SetField(ref _debugOnlyParticles, value); }
    public bool DebugOnlyWaves { get => _debugOnlyWaves; set => SetField(ref _debugOnlyWaves, value); }

    // Scene/View toggles for menu bindings
    public bool ShowGrid { get => _showGrid; set => SetField(ref _showGrid, value); }
    public bool ShowAxes { get => _showAxes; set => SetField(ref _showAxes, value); }
    public bool ShowOrbits { get => _showOrbits; set => SetField(ref _showOrbits, value); }
    public bool ShowTrails { get => _enableTrails; set => SetField(ref _enableTrails, value); }
    public bool ShowGravitationalWaves { get => _showGravitationalWaves; set => SetField(ref _showGravitationalWaves, value); }
    public bool ShowVelocityVectors { get => _showVelocityVectors; set => SetField(ref _showVelocityVectors, value); }
    public bool ShowForceVectors { get => _showForceVectors; set => SetField(ref _showForceVectors, value); }
    public bool ShowBoundingBoxes { get => _showBoundingBoxes; set => SetField(ref _showBoundingBoxes, value); }
    public bool ShowInspector { get => _showInspector; set => SetField(ref _showInspector, value); }
    public bool ShowStatistics { get => _showStatistics; set => SetField(ref _showStatistics, value); }

    public static float GetTierMultiplier(EmissionTier tier) => tier switch
    {
        EmissionTier.Planet => 1.0f,
        EmissionTier.Star => 50.0f,
        EmissionTier.Supernova => 500.0f,
        EmissionTier.Kilonova => 800.0f,
        EmissionTier.BigBang => 2000.0f,
        _ => 1.0f,
    };
}
