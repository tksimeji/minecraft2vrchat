#nullable enable

using System.Collections.Generic;
using M2V.Editor.Minecraft.Model;

namespace M2V.Editor.Bakery.Meshing
{
    public sealed class BlockModelIndex
    {
        private readonly List<List<Variant>> _variantsById;

        public BlockModelIndex(List<List<Variant>> variantsById)
        {
            _variantsById = variantsById;
        }

        public int Count => _variantsById.Count;

        public List<Variant> this[int id] => _variantsById[id];

        public IReadOnlyList<List<Variant>> Variants => _variantsById;
    }
}