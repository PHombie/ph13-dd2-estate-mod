using System.Collections.Generic;

namespace DD2Estate.Core
{
    /// <summary>
    /// xorshift64*: the same sequence on every runtime (Mono in the game, .NET in the tests), which System.Random
    /// does not promise. The whole state is one number, so it goes into the save file as is.
    /// </summary>
    public sealed class Rng
    {
        private ulong _state;

        public Rng(long seed)
        {
            // splitmix64 step so that neighbouring seeds (1, 2, 3...) start far apart
            unchecked
            {
                var z = (ulong)seed + 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                _state = z ^ (z >> 31);
            }
            if (_state == 0) _state = 0x9E3779B97F4A7C15UL;
        }

        private Rng() { }

        public static Rng FromState(ulong state) => new Rng { _state = state == 0 ? 0x9E3779B97F4A7C15UL : state };

        public ulong State => _state;

        public ulong NextU64()
        {
            unchecked
            {
                _state ^= _state >> 12;
                _state ^= _state << 25;
                _state ^= _state >> 27;
                return _state * 0x2545F4914F6CDD1DUL;
            }
        }

        /// <summary>0 .. n-1.</summary>
        public int Next(int n) => n <= 1 ? 0 : (int)((NextU64() >> 33) % (ulong)n);

        /// <summary>min .. max, both included.</summary>
        public int Range(int min, int max) => max <= min ? min : min + Next(max - min + 1);

        /// <summary>0 &lt;= x &lt; 1.</summary>
        public double NextDouble() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);

        public bool Chance(double probability) => NextDouble() < probability;

        /// <summary>Index picked by weight; -1 when nothing has a positive weight.</summary>
        public int PickWeighted(IList<double> weights)
        {
            double total = 0;
            for (var i = 0; i < weights.Count; i++)
                if (weights[i] > 0) total += weights[i];
            if (total <= 0) return -1;
            var roll = NextDouble() * total;
            var last = -1;
            for (var i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0) continue;
                last = i;
                roll -= weights[i];
                if (roll < 0) return i;
            }
            return last;
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = Next(i + 1);
                var t = list[i];
                list[i] = list[j];
                list[j] = t;
            }
        }
    }
}
