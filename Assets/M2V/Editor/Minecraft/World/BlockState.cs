#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using fNbt;

namespace M2V.Editor.Minecraft.World
{
    public sealed record BlockState
    {
        private static readonly ResourceLocation AirKey = ResourceLocation.Of("air");
        private static readonly ResourceLocation CaveAirKey = ResourceLocation.Of("cave_air");
        private static readonly ResourceLocation VoidAirKey = ResourceLocation.Of("void_air");
        
        public static readonly BlockState Air = new(AirKey, new Dictionary<string, NbtTag>(StringComparer.Ordinal));

        public static BlockState From(NbtCompound nbt)
        {
            var name = (nbt["Name"] as NbtString)?.Value ?? string.Empty;
            var properties = new Dictionary<string, NbtTag>(StringComparer.Ordinal);

            if (nbt["Properties"] is not NbtCompound propertiesTag)
            {
                return new BlockState(ResourceLocation.Parse(name), properties);
            }

            foreach (var tag in propertiesTag.Tags)
            {
                if (tag.Name != null)
                {
                    properties[tag.Name] = tag;
                }
            }

            return new BlockState(ResourceLocation.Parse(name), properties);
        }
        
        internal static string FormatNbtTag(NbtTag tag)
        {
            return tag switch
            {
                NbtString str => str.Value,
                NbtByte b => b.Value == 0 ? "false" : "true",
                NbtShort s => s.Value.ToString(CultureInfo.InvariantCulture),
                NbtInt i => i.Value.ToString(CultureInfo.InvariantCulture),
                NbtLong l => l.Value.ToString(CultureInfo.InvariantCulture),
                NbtFloat f => f.Value.ToString(CultureInfo.InvariantCulture),
                NbtDouble d => d.Value.ToString(CultureInfo.InvariantCulture),
                _ => tag.ToString()
            };
        }

        public ResourceLocation Name { get; }
        public IReadOnlyDictionary<string, NbtTag> Properties { get; }
        
        private readonly Lazy<string> _key;

        private BlockState(ResourceLocation name, Dictionary<string, NbtTag> properties)
        {
            Name = name;
            Properties = new ReadOnlyDictionary<string, NbtTag>(properties);
            _key = new Lazy<string>(ComputeUniqueKey);
        }
        
        public bool IsAir => Name == AirKey || Name == CaveAirKey || Name == VoidAirKey;
        
        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(_key.Value);
        }

        public bool Equals(BlockState? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return other is not null && string.Equals(_key.Value, other._key.Value, StringComparison.Ordinal);
        }

        private string ComputeUniqueKey()
        {
            if (Properties.Count == 0) return Name.ToString();

            var keys = new List<string>(Properties.Keys);
            keys.Sort(StringComparer.Ordinal);
            
            var parts = new List<string>(keys.Count);
            parts.AddRange(keys.Select(key => $"{key}={FormatNbtTag(Properties[key])}"));
            
            return $"{Name}|{string.Join(";", parts)}";

        }
    }
}