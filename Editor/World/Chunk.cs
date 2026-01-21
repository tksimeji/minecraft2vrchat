#nullable enable
using System.Collections.Generic;
using fNbt;
using M2V.Editor.World.Block;

namespace M2V.Editor.World
{
    public sealed class Chunk
    {
        private static List<Section> ReadSections(NbtCompound chunkNbt)
        {
            var sections = new List<Section>();

            if (chunkNbt["sections"] is not NbtList sectionsTag || sectionsTag.Count == 0)
            {
                return sections;
            }

            foreach (var sectionTag in sectionsTag)
            {
                if (sectionTag is NbtCompound compound)
                {
                    sections.Add(new Section(compound));
                }
            }

            return sections;
        }

        private readonly List<Section> _sections;

        public Chunk(NbtCompound chunkNbt)
        {
            _sections = ReadSections(chunkNbt);
        }

        public IReadOnlyList<Section> Sections => _sections;

        public sealed class Section
        {
            private static (List<BlockState>?, long[]?) ReadBlockStateData(NbtCompound sectionNbt)
            {
                if (sectionNbt["block_states"] is not NbtCompound blockStates)
                {
                    return (null, null);
                }

                var paletteTag = blockStates["palette"] as NbtList;
                if (paletteTag == null || paletteTag.Count == 0)
                {
                    return (null, null);
                }

                var palette = ReadBlockStatePalette(paletteTag);
                if (palette.Count == 0)
                {
                    return (null, null);
                }

                return (palette, (blockStates["data"] as NbtLongArray)?.Value);
            }

            private static List<BlockState> ReadBlockStatePalette(NbtList paletteTag)
            {
                var list = new List<BlockState>(paletteTag.Count);
                foreach (var entry in paletteTag)
                {
                    if (entry is NbtCompound compound)
                    {
                        list.Add(BlockState.FromNbt(compound));
                    }
                }

                return list;
            }

            public int Y { get; }
            private readonly List<BlockState>? _blockStatePalette;
            private readonly long[]? _blockStateData;

            internal Section(NbtCompound sectionNbt)
            {
                Y = sectionNbt["Y"] is NbtByte yTag ? unchecked((sbyte)yTag.Value) : 0;
                (_blockStatePalette, _blockStateData) = ReadBlockStateData(sectionNbt);
            }

            public IReadOnlyList<BlockState>? BlockStatePalette => _blockStatePalette;

            public long[]? BlockStateData => _blockStateData;
        }
    }
}
