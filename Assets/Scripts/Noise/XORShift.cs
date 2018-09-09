using System;
using Unity.Collections;
using Unity.Mathematics;

// https://gist.github.com/firstspring1845/6266769


public struct XorshiftBurst : IDisposable {
    private NativeArray<long> seed;

    public XorshiftBurst(long seed) {
        this.seed = new NativeArray<long>(4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        for (int i = 0; i < 4; i++) {
            this.seed[i] = seed;
        }
    }

    public XorshiftBurst(long[] seed) {
        this.seed = new NativeArray<long>(4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        if (seed.Length < 4) {
            for (int i = 0; i < 4; i++) {
                this.seed[i] = seed[0];
            }
        } else {
            for (int i = 0; i < 4; i++) {
                this.seed[i] = seed[i];
            }
        }
    }

    public void Dispose() {
        this.seed.Dispose();
    }

    public long Next() {
        long t = seed[0] ^ (seed[0] << 11);
        seed[0] = seed[1];
        seed[1] = seed[2];
        seed[2] = seed[3];
        seed[3] = (seed[3] ^ (seed[3] >> 19)) ^ (t ^ (t >> 8));
        return seed[3];
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