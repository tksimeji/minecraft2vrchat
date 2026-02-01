#nullable enable

using System.Collections.Generic;
using System.Linq;
using M2V.Editor.Minecraft;
using M2V.Editor.Minecraft.Model;
using M2V.Editor.Minecraft.World;

namespace M2V.Editor.Bakery.Meshing
{
    public sealed class BlockModelResolver
    {
        private readonly AssetReader _reader;
        private readonly Dictionary<ResourceLocation, BlockModelDefinition> _blockStateCache = new();
        private readonly HashSet<ResourceLocation> _missingBlockStates = new();
        private readonly Dictionary<ResourceLocation, BlockModel> _modelCache = new();
        private readonly Dictionary<ResourceLocation, BlockModel> _resolvedModelCache = new();

        public BlockModelResolver(AssetReader reader)
        {
            _reader = reader;
        }

        public BlockModelIndex BuildBlockVariants(IReadOnlyList<BlockState> blockStates)
        {
            var variantsById = new List<List<Variant>>(blockStates.Count) { new() };

            for (var i = 1; i < blockStates.Count; i++)
            {
                var state = blockStates[i];
                if (state == null || state.Name.IsEmpty || state.IsAir)
                {
                    variantsById.Add(new List<Variant>());
                    continue;
                }

                var definition = LoadBlockStateDefinition(state.Name);
                var variants = definition?.GetResolvedVariants(state.Properties);
                if (variants == null || variants.Count == 0)
                {
                    variantsById.Add(new List<Variant> { CreateFallbackVariant(state.Name) });
                }
                else
                {
                    variantsById.Add(variants);
                }
            }

            return new BlockModelIndex(variantsById);
        }

        public List<bool> BuildFullCubeFlags(BlockModelIndex blockModelIndex)
        {
            var flags = new List<bool>(blockModelIndex.Count);
            for (var i = 0; i < blockModelIndex.Count; i++)
            {
                var variants = blockModelIndex[i];
                var isFull = !(variants.Count == 0 || variants
                    .Select(variant => ResolveBlockModel(variant?.Model ?? ResourceLocation.Empty))
                    .Any(model => model == null || !model.IsFullCube()));

                flags.Add(isFull);
            }

            return flags;
        }

        public HashSet<ResourceLocation> CollectTexturePaths(BlockModelIndex blockModelIndex)
        {
            var paths = new HashSet<ResourceLocation>();
            foreach (var variants in blockModelIndex.Variants)
            {
                if (variants == null || variants.Count == 0)
                {
                    continue;
                }

                foreach (var variant in variants)
                {
                    var model = ResolveBlockModel(variant?.Model ?? ResourceLocation.Empty);
                    if (model == null)
                    {
                        continue;
                    }

                    foreach (var texture in model.GetResolvedTextures())
                    {
                        if (!texture.IsEmpty)
                        {
                            paths.Add(texture);
                        }
                    }
                }
            }

            return paths;
        }

        public BlockModel? ResolveBlockModel(ResourceLocation modelLocation)
        {
            if (modelLocation.IsEmpty)
            {
                return null;
            }

            if (_resolvedModelCache.TryGetValue(modelLocation, out var cached))
            {
                return cached;
            }

            var visiting = new HashSet<ResourceLocation>();
            var resolved =
                ModelLoader.ResolveModelWithParents(_reader, modelLocation, _modelCache, _resolvedModelCache, visiting);
            if (resolved != null)
            {
                _resolvedModelCache[modelLocation] = resolved;
            }

            return resolved;
        }

        private BlockModelDefinition? LoadBlockStateDefinition(ResourceLocation blockName)
        {
            if (blockName.IsEmpty)
            {
                return null;
            }

            if (_blockStateCache.TryGetValue(blockName, out var cached))
            {
                return cached;
            }

            if (_missingBlockStates.Contains(blockName))
            {
                return null;
            }

            if (!ModelLoader.TryLoadBlockState(_reader, blockName, out BlockModelDefinition definition))
            {
                _missingBlockStates.Add(blockName);
                return null;
            }

            _blockStateCache[blockName] = definition;
            return definition;
        }

        private static Variant CreateFallbackVariant(ResourceLocation blockName)
        {
            var modelLocation = blockName.IsEmpty
                ? ResourceLocation.Empty
                : ResourceLocation.Of(blockName.Namespace, $"block/{blockName.Path}");
            return new Variant { Model = modelLocation };
        }
    }
}