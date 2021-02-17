using System.Collections.Generic;
using System.IO;

namespace MomBspTools.Lib.BSP.Lump
{
    public class TexDataLump : AbstractLump
    {
        public override int DataSize => 32;

        public struct TexData
        {
            public float[] Reflectivity { get; set; }
            public int TexName { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int ViewWidth { get; set; }
            public int ViewHeight { get; set; }
        }

        public List<TexData> Data { get; set; } = new();

        public override void Read(BinaryReader r)
        {
            var item = new TexData
            {
                Reflectivity = new float[3] {r.ReadSingle(), r.ReadSingle(), r.ReadSingle()},
                TexName = r.ReadInt32(),
                Width = r.ReadInt32(),
                Height = r.ReadInt32(),
                ViewWidth = r.ReadInt32(),
                ViewHeight = r.ReadInt32()
            };
            Data.Add(item);
        }

        public TexDataLump(BspFile parent) : base(parent)
        {
        }
    }
}