#nullable enable

using System.Collections.Generic;
using M2V.Editor.Minecraft;
using M2V.Editor.Minecraft.Biome;

namespace M2V.Editor.Bakery.Meshing
{
    public sealed class BiomeIndex
    {
        private readonly Dictionary<ResourceLocation, int> _indexByKey = new();
        private readonly List<Biome> _biomes = new();
        private readonly int _plainsIndex;

        public BiomeIndex(AssetReader assetReader)
        {
            foreach (var entry in BiomeLoader.LoadAll(assetReader))
            {
                AddOrUpdate(entry.Id, entry.Biome);
            }

            if (_indexByKey.TryGetValue(Biome.Plains, out _plainsIndex))
            {
                return;
            }

            var fallback = new Biome { Temperature = 0.8f, Downfall = 0.4f };
            _plainsIndex = AddOrUpdate(Biome.Plains, fallback);
        }

        public int PlainsIndex => _plainsIndex;

        public int GetIndex(ResourceLocation biomeKey) =>
            biomeKey.IsEmpty ? _plainsIndex : _indexByKey.GetValueOrDefault(biomeKey, _plainsIndex);

        public Biome GetBiomeByIndex(int index)
        {
            if (index >= 0 && index < _biomes.Count)
            {
                return _biomes[index];
            }

            return _biomes[_plainsIndex];
        }

        private int AddOrUpdate(ResourceLocation id, Biome biome)
        {
            if (_indexByKey.TryGetValue(id, out var index))
            {
                _biomes[index] = biome;
                return index;
            }

            index = _biomes.Count;
            _biomes.Add(biome);
            _indexByKey[id] = index;
            return index;
        }
    }
}