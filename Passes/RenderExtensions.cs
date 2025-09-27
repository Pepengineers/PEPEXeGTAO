using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace PEPEngineers.Passes
{
    public static class RenderExtensions
    {
        public static TextureDesc CreateTextureDesc(string name, RenderTextureDescriptor input)
        {
            return new TextureDesc(input.width, input.height)
            {
                format = input.graphicsFormat,
                dimension = input.dimension,
                slices = input.volumeDepth,
                name = name,
                useMipMap = input.useMipMap,
                enableRandomWrite = input.enableRandomWrite
            };
        }

        public static int AlignUp(int value, int alignment)
        {
            if (alignment == 0) return value;
            return (value + alignment - 1) & -alignment;
        }

        public static int2 AlignUp(int2 value, int2 alignment)
        {
            return math.select(value, (value + alignment - 1) & -alignment, alignment != 0);
        }

        public static int3 AlignUp(int3 value, int3 alignment)
        {
            return math.select(value, (value + alignment - 1) & -alignment, alignment != 0);
        }
    }
}