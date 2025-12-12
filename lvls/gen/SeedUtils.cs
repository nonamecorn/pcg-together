using System;

namespace PCGTogether.lvls.gen;

/// Utilities for deterministic seed derivation and per-thread RNG instances.
internal static class SeedUtils {
    private const int DefaultSeed = unchecked((int)0x9E3779B9u);

    /// Returns a non-zero seed to keep the RNG state valid.
    public static int NormalizeSeed(int seed) {
        return seed == 0 ? DefaultSeed : seed;
    }

    /// Derives a new deterministic seed from a base seed and a salt.
    public static int DeriveSeed(int baseSeed, int salt) {
        var x = (uint)NormalizeSeed(baseSeed);
        var y = (uint)salt;
        var z = x ^ (y + 0x9E3779B9u + (x << 6) + (x >> 2));
        z = (z ^ 0x85EBCA6Bu) * 0x27D4EB2Du;
        z ^= z >> 15;
        return NormalizeSeed((int)z);
    }

    /// Scrambles a seed into a 64-bit state for the xorshift* generator.
    public static ulong ScrambleState(int seed) {
        var state = (ulong)(uint)NormalizeSeed(seed) ^ 0x9E3779B97F4A7C15UL;
        state ^= state >> 30;
        state *= 0xBF58476D1CE4E5B9UL;
        state ^= state >> 27;
        state *= 0x94D049BB133111EBUL;
        state ^= state >> 31;
        return state == 0 ? 0xA4093822299F31D0UL : state;
    }
}

/// Deterministic RNG using xorshift*; safe to use independently on multiple threads.
internal struct DeterministicRng {
    private ulong _state;

    public DeterministicRng(int seed) {
        _state = SeedUtils.ScrambleState(seed);
    }

    /// Advances RNG state and returns a 64-bit sample.
    /// <returns>Unsigned 64-bit pseudo-random value.</returns>
    private ulong NextUInt64() {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return _state * 0x2545F4914F6CDD1DUL;
    }

    public float NextFloat() {
        const float scale = 1f / (1UL << 24); // 24 bits of precision for float
        var value = NextUInt64() >> 40;
        return (float)(value * scale);
    }

    public int NextInt(int minInclusive, int maxInclusive) {
        if (maxInclusive < minInclusive) {
            throw new ArgumentException("maxInclusive must be >= minInclusive");
        }
        var range = (ulong)(maxInclusive - minInclusive + 1);
        var sample = NextUInt64();
        return (int)(minInclusive + (long)(sample % range));
    }
}

/// Seed chain that keeps all generation stages deterministic off a single base seed.
public readonly struct VoronoiSeedChain {
    private const int PoissonSalt = unchecked((int)0xA1B2C3D4u);
    private const int TraversalSalt = 0x00C0FFEE;

    /// Root seed for the world.
    public int BaseSeed { get; }
    /// Derived Poisson seed.
    public int PoissonSeed { get; }
    /// Derived traversal seed.
    public int TraversalSeed { get; }

    public VoronoiSeedChain(int baseSeed, int? poissonSeedOverride = null, int? traversalSeedOverride = null) {
        BaseSeed = SeedUtils.NormalizeSeed(baseSeed);
        PoissonSeed = SeedUtils.NormalizeSeed(poissonSeedOverride ?? SeedUtils.DeriveSeed(BaseSeed, PoissonSalt));
        TraversalSeed = SeedUtils.NormalizeSeed(traversalSeedOverride ?? SeedUtils.DeriveSeed(BaseSeed, TraversalSalt));
    }

    public VoronoiSeedChain WithTraversalSeed(int traversalSeed) {
        return new VoronoiSeedChain(BaseSeed, PoissonSeed, traversalSeed);
    }
}
