using System.Collections.Generic;

namespace M2V.Editor.Model
{
    internal sealed class BlockStateKey
    {
        internal static readonly BlockStateKey Empty =
            new BlockStateKey("minecraft:air", new Dictionary<string, string>(), "minecraft:air");

        internal string Name { get; }

        internal string NameWithoutNamespace
        {
            get
            {
                if (string.IsNullOrEmpty(Name))
                {
                    return string.Empty;
                }

                var index = Name.IndexOf(':');
                return index >= 0 ? Name.Substring(index + 1) : Name;
            }
        }

        internal Dictionary<string, string> Properties { get; }
        internal string Key { get; }

        internal BlockStateKey(string name, Dictionary<string, string> properties, string key)
        {
            Name = name;
            Properties = properties;
            Key = key;
        }
    }
}