using System.IO;

namespace Lumper.Lib.BSP.Lumps.BspLumps
{
    public class TexDataStringTableLump : FixedLump<BspLumpType, int>
    {
        public override int StructureSize => 4;

        protected override void ReadItem(BinaryReader reader)
        {
            Data.Add(reader.ReadInt32());
        }
        protected override void WriteItem(BinaryWriter writer, int index)
        {
            writer.Write(Data[index]);
        }

        public TexDataStringTableLump(BspFile parent) : base(parent)
        {
        }
    }
}