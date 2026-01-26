using System.Collections.Generic;
using M2V.Editor.Model;

namespace M2V.Editor.Meshing
{
    internal sealed class ModelRepository : IModelRepository
    {
        private readonly ModelResolver _resolver;

        internal ModelRepository(IAssetReader assets)
        {
            _resolver = new ModelResolver(assets);
        }

        public List<List<ModelPlacement>> BuildBlockModels(List<BlockStateKey> states)
        {
            return _resolver.BuildBlockModels(states);
        }

        public List<bool> BuildFullCubeFlags(List<List<ModelPlacement>> modelCache)
        {
            return _resolver.BuildFullCubeFlags(modelCache);
        }

        public HashSet<string> CollectTexturePaths(List<List<ModelPlacement>> modelCache)
        {
            var set = _resolver.CollectTexturePaths(modelCache);
            if (set.Count == 0)
            {
                set.Add("minecraft:block/dirt");
            }
            return set;
        }
    }
}
