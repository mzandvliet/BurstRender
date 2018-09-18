using System;
using Unity.Collections;
using Unity.Mathematics;

// https://gist.github.com/firstspring1845/6266769

// [System.Serializable]
// public struct XorshiftBurst {
//     public long _seed0;
//     public long _seed1;
//     public long _seed2;
//     public long _seed3;

//     public XorshiftBurst(long seed) {
//         _seed0 = seed;
//         _seed1 = seed;
//         _seed2 = seed;
//         _seed3 = seed;
//     }

//     public XorshiftBurst(long seed0, long seed1, long seed2, long seed3) {
//         _seed0 = seed0;
//         _seed1 = seed1;
//         _seed2 = seed2;
//         _seed3 = seed3;
//     }

//     public long Next() {
//         long t = _seed0 ^ (_seed0 << 11);
//         _seed0 = _seed1;
//         _seed1 = _seed2;
//         _seed2 = _seed3;
//         _seed3 = (_seed3 ^ (_seed3 >> 19)) ^ (t ^ (t >> 8));
//         return _seed3;
//     }

//     public int NextInt() {
//         return (int)Next();
//     }

//     public int NextInt(int min, int max) {
//         return min + math.abs(NextInt()) / (int.MaxValue / max);
//     }

//     public float NextFloat() {
//         return (float)Next() / long.MaxValue;
//     }

//     public double NextDouble() {
//         return (double)Next() / long.MaxValue;
//     }
// }

[System.Serializable]
public struct XorshiftBurst {
    private int _seed;

    public XorshiftBurst(int seed) {
        _seed = seed;
    }

    public int NextInt(int min, int max) {
        return 0;
    }

    public float NextFloat() {
        return 0f;
    }
}

// public struct XorshiftBurst : IDisposable {
//     public NativeArray<long> _seed;

//     public XorshiftBurst(long seed, Allocator allocator) {
//         _seed = new NativeArray<long>(4, allocator, NativeArrayOptions.UninitializedMemory);
//         for (int i = 0; i < 4; i++) {
//             _seedi] = seed;
//         }
//     }

//     public XorshiftBurst(long seed0, long seed1, long seed2, long seed3, Allocator allocator) {
//         _seed = new NativeArray<long>(4, allocator, NativeArrayOptions.UninitializedMemory);
//         _seed[0] = seed0;
//         _seed[1] = seed1;
//         _seed[2] = seed2;
//         _seed[3] = seed3;
//     }

//     public void Dispose() {
//         _seed.Dispose();
//     }

//     public long Next() {
//         long t = _seed[0] ^ (_seed[0] << 11);
//         _seed[0] = _seed[1];
//         _seed[1] = _seed[2];
//         _seed[2] = _seed[3];
//         _seed[3] = (_seed[3] ^ (_seed[3] >> 19)) ^ (t ^ (t >> 8));
//         return _seed[3];
//     }

//     public int NextInt() {
//         return (int)Next();
//     }

//     public int NextInt(int min, int max) {
//         return min + math.abs(NextInt()) / (int.MaxValue / max);
//     }

//     public float NextFloat() {
//         return (float)Next() / long.MaxValue;
//     }

//     public double NextDouble() {
//         return (double)Next() / long.MaxValue;
//     }

//     public void NextBytes(byte[] b) {
//         int length = b.Length;
//         for (int i = 0; i < length; i++) {
//             long n = Next();
//             for (int j = 0; j < 7; j++) {
//                 if (i < length) {
//                     b[i] = (byte)(n >> (j << 3));
//                     i++;
//                 }
//             }
//         }
//     }
// }

public class Xorshift {
    private long[] seed;

    public Xorshift(long seed) {
        SetSeed(seed);
    }

    public Xorshift(long[] seed) {
        SetSeed(seed);
    }

    public Xorshift SetSeed(long[] seed) {
        if (seed.Length < 4) {
            return SetSeed(seed[0]);
        }
        this.seed = seed;
        return this;
    }

    public Xorshift SetSeed(long seed) {
        this.seed = new long[] { seed, seed, seed, seed };
        return this;
    }

    public long Next() {
        long t = seed[0] ^ (seed[0] << 11);
        seed[0] = seed[1];
        seed[1] = seed[2];
        seed[2] = seed[3];
        seed[3] = (seed[3] ^ (seed[3] >> 19)) ^ (t ^ (t >> 8));
        return seed[3];
    }

    public long NextLong() {
        long l = this.Next();
        if (this.NextBoolean()) {
            l = ~l + 1L;
        }
        return l;
    }

    public int NextInt() {
        return (int)this.Next();
    }
    public float NextFloat() {
        return (float)this.Next() / long.MaxValue;
    }

    public double NextDouble() {
        return (double)this.Next() / long.MaxValue;
    }

    private long temp;
    private char cnt = System.Char.MinValue;

    public bool NextBoolean() {
        if (cnt == 0) {
            temp = this.Next();
            cnt = '?'; // 63
        }
        bool b = (temp | 1L) == 0;
        temp >>= 1;
        cnt -= (char)1;
        return b;
    }
    public void NextBytes(byte[] b) {
        int length = b.Length;
        for (int i = 0; i < length; i++) {
            long n = this.Next();
            for (int j = 0; j < 7; j++) {
                if (i < length) {
                    b[i] = (byte)(n >> (j << 3));
                    i++;
                }
            }
        }
    }
}