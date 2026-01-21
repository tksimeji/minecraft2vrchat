using System.Collections.Generic;
using System.IO.Compression;
using M2V.Editor.Model;

namespace M2V.Editor.MeshGeneration
{
    internal sealed class ModelRepository : IModelRepository
    {
        private readonly ModelResolver _resolver;

        internal ModelRepository(ZipArchive zip)
        {
            _resolver = new ModelResolver(zip);
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
            return _resolver.CollectTexturePaths(modelCache);
        }
    }
}