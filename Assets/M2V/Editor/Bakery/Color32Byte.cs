#nullable enable

namespace M2V.Editor.Bakery.Meshing
{
    public readonly struct Color32Byte
    {
        public static Color32Byte FromInt(int value)
        {
            var r = (byte)((value >> 16) & 0xFF);
            var g = (byte)((value >> 8) & 0xFF);
            var b = (byte)(value & 0xFF);
            return new Color32Byte(r, g, b, 0xFF);
        }
        
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;
        public readonly byte A;

        public Color32Byte(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }
}