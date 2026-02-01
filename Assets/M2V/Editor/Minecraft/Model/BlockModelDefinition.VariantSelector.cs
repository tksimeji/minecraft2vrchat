#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using fNbt;
using M2V.Editor.Minecraft.World;

namespace M2V.Editor.Minecraft.Model
{
    internal static class VariantSelector
    {
        public static bool Matches(string key, IReadOnlyDictionary<string, NbtTag> properties, out int score)
        {
            score = 0;
            if (string.IsNullOrEmpty(key))
            {
                return true;
            }

            var parts = key.Split(',');
            foreach (var part in parts)
            {
                var kv = part.Split('=');
                if (kv.Length != 2)
                {
                    continue;
                }

                if (!properties.TryGetValue(kv[0], out var value))
                {
                    return false;
                }

                if (!MatchesSelector(BlockState.FormatNbtTag(value), kv[1]))
                {
                    return false;
                }

                score++;
            }

            return true;
        }

        private static bool MatchesSelector(string value, string selector)
        {
            var options = selector.Split('|');
            return options.Any(option => string.Equals(value, option, StringComparison.Ordinal));
        }
    }
}