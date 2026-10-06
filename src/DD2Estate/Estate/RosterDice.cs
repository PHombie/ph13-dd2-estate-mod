using System.Collections.Generic;

namespace DD2Estate.Estate
{
    /// <summary>
    /// A small repeatable random source (xorshift32) for rolls that must come out the same after a reload:
    /// the weekly stage coach is rolled from the estate's saved seed and the week number.
    /// </summary>
    internal sealed class RosterDice
    {
        private uint _state;

        public RosterDice(int seed, int stream)
        {
            unchecked
            {
                _state = (uint)seed ^ ((uint)stream * 0x9E3779B9u) ^ 0x85EBCA6Bu;
            }
            if (_state == 0) _state = 0x6C078965u;
            // Neighbouring weeks differ in a few bits only; a few rounds pull their sequences apart.
            for (var i = 0; i < 8; i++) Next();
        }

        private uint Next()
        {
            unchecked
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
            }
            return _state;
        }

        /// <summary>0 .. max-1.</summary>
        public int Below(int max) => max <= 1 ? 0 : (int)(Next() % (uint)max);

        public void Shuffle<T>(IList<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = Below(i + 1);
                var swap = list[i];
                list[i] = list[j];
                list[j] = swap;
            }
        }
    }
}
