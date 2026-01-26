#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using fNbt;

namespace M2V.Editor.World.Block
{
    public sealed record BlockState
    {
        public static readonly BlockState Air =
            new BlockState("minecraft:air", new Dictionary<string, NbtTag>(StringComparer.Ordinal));

        internal static BlockState FromNbt(NbtCompound nbt)
        {
            var name = (nbt["Name"] as NbtString)?.Value ?? string.Empty;
            var properties = ReadProperties(nbt["Properties"] as NbtCompound);
            return new BlockState(name, properties);
        }

        private static Dictionary<string, NbtTag> ReadProperties(NbtCompound? props)
        {
            var properties = new Dictionary<string, NbtTag>(StringComparer.Ordinal);
            if (props == null)
            {
                return properties;
            }

            foreach (var tag in props.Tags)
            {
                properties[tag.Name] = tag;
            }

            return properties;
        }

        public string Name { get; }
        private readonly Dictionary<string, NbtTag> _properties;

        public IReadOnlyDictionary<string, NbtTag> Properties => _properties;

        private BlockState(string name, Dictionary<string, NbtTag> properties)
        {
            Name = name;
            _properties = properties;
        }

        private string _key;

        public string Key => _key ??= BuildKey(Name, _properties);

        public bool Equals(BlockState other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is null)
            {
                return false;
            }

            return string.Equals(Key, other.Key, StringComparison.Ordinal);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Key);
        }

        private static string BuildKey(string name, IReadOnlyDictionary<string, NbtTag> properties)
        {
            if (properties == null || properties.Count == 0)
            {
                return name ?? string.Empty;
            }

            var keys = new List<string>(properties.Keys);
            keys.Sort(StringComparer.Ordinal);
            var parts = new List<string>(keys.Count);
            foreach (var key in keys)
            {
                parts.Add($"{key}={FormatNbtValue(properties[key])}");
            }

            return $"{name}|{string.Join(";", parts)}";
        }

        private static string FormatNbtValue(NbtTag tag)
        {
            if (tag == null)
            {
                return string.Empty;
            }

            return tag switch
            {
                NbtString str => str.Value ?? string.Empty,
                NbtByte b => b.Value == 0 ? "false" :
                    b.Value == 1 ? "true" : b.Value.ToString(CultureInfo.InvariantCulture),
                NbtShort s => s.Value.ToString(CultureInfo.InvariantCulture),
                NbtInt i => i.Value.ToString(CultureInfo.InvariantCulture),
                NbtLong l => l.Value.ToString(CultureInfo.InvariantCulture),
                NbtFloat f => f.Value.ToString(CultureInfo.InvariantCulture),
                NbtDouble d => d.Value.ToString(CultureInfo.InvariantCulture),
                _ => tag.ToString()
            };
        }
    }
}
