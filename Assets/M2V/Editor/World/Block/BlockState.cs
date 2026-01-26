#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using fNbt;

namespace M2V.Editor.World.Block
{
    public sealed class BlockState : IReadOnlyDictionary<string, NbtTag>
    {
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

        public int Count => _properties.Count;

        public IEnumerable<string> Keys => _properties.Keys;

        public IEnumerable<NbtTag> Values => _properties.Values;

        public NbtTag this[string key] => _properties[key];

        public bool ContainsKey(string key)
        {
            return _properties.ContainsKey(key);
        }

        public bool TryGetValue(string key, out NbtTag value)
        {
            return _properties.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<string, NbtTag>> GetEnumerator()
        {
            return _properties.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _properties.GetEnumerator();
        }
    }
}
