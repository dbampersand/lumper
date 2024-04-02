namespace Lumper.Lib.BSP.Lumps.GameLumps;
using System.Drawing;
using System.IO;
using Lumper.Lib.BSP.Struct;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

public enum StaticPropVersion
{
    Unknown = 0,
    V4 = 40,
    V5 = 50,
    V6 = 60,
    V7s = 70,
    V7 = 71,
    V8 = 80,
    V9 = 90,
    V10 = 100,
    V11 = 110,
    V12 = 120,
    V13 = 130
}
public class StaticPropLump(BspFile parent) : FixedLump<GameLumpType, StaticProp>(parent)
{
    [JsonConverter(typeof(StringEnumConverter))]
    public StaticPropVersion ActualVersion { get; set; }
    public override int StructureSize => ActualVersion switch
    {
        StaticPropVersion.V4 => 56,
        StaticPropVersion.V5 => 60,
        StaticPropVersion.V6 => 64,
        StaticPropVersion.V7 => 68,
        StaticPropVersion.V7s => 72,
        StaticPropVersion.V8 => 68,
        StaticPropVersion.V9 => 72,
        StaticPropVersion.V10 => 76,
        StaticPropVersion.V11 => 80,
        StaticPropVersion.V12 => 80,
        StaticPropVersion.V13 => 88,
        _ => 1
    };

    public void SetVersion(int version) => ActualVersion = version switch
    {
        4 => StaticPropVersion.V4,
        5 => StaticPropVersion.V5,
        6 => StaticPropVersion.V6,
        7 => StaticPropVersion.V7,
        8 => StaticPropVersion.V8,
        9 => StaticPropVersion.V9,
        10 => StaticPropVersion.V10,
        11 => StaticPropVersion.V11,
        12 => StaticPropVersion.V12,
        13 => StaticPropVersion.V13,
        _ => StaticPropVersion.Unknown
    };

    public int GetVersion() => ActualVersion switch
    {
        StaticPropVersion.V4 => 4,
        StaticPropVersion.V5 => 5,
        StaticPropVersion.V6 => 6,
        StaticPropVersion.V7 => 7,
        StaticPropVersion.V7s => 10, //good luck
        StaticPropVersion.V8 => 8,
        StaticPropVersion.V9 => 9,
        StaticPropVersion.V10 => 10,
        StaticPropVersion.V11 => 11,
        StaticPropVersion.V12 => 12,
        _ => 0
    };
    protected override void ReadItem(BinaryReader reader)
    {
        var startpos = reader.BaseStream.Position;
        StaticProp prop = new()
        {
            // v4
            Origin = new Vector()
            {
                X = System.BitConverter.ToSingle(reader.ReadBytes(4)),
                Y = System.BitConverter.ToSingle(reader.ReadBytes(4)),
                Z = System.BitConverter.ToSingle(reader.ReadBytes(4)),
            },
            Angle = new Angle()
            {
                Pitch = System.BitConverter.ToSingle(reader.ReadBytes(4)),
                Yaw = System.BitConverter.ToSingle(reader.ReadBytes(4)),
                Roll = System.BitConverter.ToSingle(reader.ReadBytes(4)),
            },

            // v4
            PropType = reader.ReadUInt16(),
            FirstLeaf = reader.ReadUInt16(),
            LeafCount = reader.ReadUInt16(),
            Solid = reader.ReadByte(),
            // every version except v7*
            //if (Version != StaticPropVersion.V7s)
            Flags = reader.ReadByte(),
            // v4 still
            Skin = reader.ReadInt32(),
            FadeMinDist = System.BitConverter.ToSingle(reader.ReadBytes(4)),
            FadeMaxDist = System.BitConverter.ToSingle(reader.ReadBytes(4)),
            LightingOrigin = new Vector()
            {
                X = System.BitConverter.ToSingle(reader.ReadBytes(4)),
                Y = System.BitConverter.ToSingle(reader.ReadBytes(4)),
                Z = System.BitConverter.ToSingle(reader.ReadBytes(4)),
            }
        };
        // since v5
        if (ActualVersion >= StaticPropVersion.V5)
            prop.ForcedFadeScale = System.BitConverter.ToSingle(reader.ReadBytes(4));
        // v6, v7, and v7* only
        switch (ActualVersion)
        {
            case StaticPropVersion.V6:
            case StaticPropVersion.V7:
            case StaticPropVersion.V7s:
                prop.MinDXLevel = reader.ReadUInt16();
                prop.MaxDXLevel = reader.ReadUInt16();
                break;
            case StaticPropVersion.Unknown:
                break;
            case StaticPropVersion.V4:
                break;
            case StaticPropVersion.V5:
                break;
            case StaticPropVersion.V8:
                break;
            case StaticPropVersion.V9:
                break;
            case StaticPropVersion.V10:
                break;
            case StaticPropVersion.V11:
                break;
            case StaticPropVersion.V12:
                break;
            default:
                break;
        }
        // v7* only
        if (ActualVersion == StaticPropVersion.V7s)
        {
            prop.FlagsV7s = reader.ReadUInt32();
            prop.LightmapResX = reader.ReadUInt16();
            prop.LightmapResY = reader.ReadUInt16();
        }
        // since v8
        if (ActualVersion >= StaticPropVersion.V8)
        {
            prop.MinCPULevel = reader.ReadByte();
            prop.MaxCPULevel = reader.ReadByte();
            prop.MinGPULevel = reader.ReadByte();
            prop.MaxGPULevel = reader.ReadByte();
        }
        // since v7
        if (ActualVersion >= StaticPropVersion.V7)
        {
            var r = reader.ReadByte();
            var g = reader.ReadByte();
            var b = reader.ReadByte();
            var a = reader.ReadByte();
            prop.DiffuseModulation = Color.FromArgb(a, r, g, b);
        }
        // v9 and v10 only
        // and v11?
        if (ActualVersion >= StaticPropVersion.V9)
            prop.DisableX360 = reader.ReadInt32() > 0;
        // since v10
        if (ActualVersion >= StaticPropVersion.V10)
            prop.FlagsEx = reader.ReadUInt32();
        // since v11
        if (ActualVersion >= StaticPropVersion.V11)
        {
            var x = reader.ReadSingle();
            prop.UniformScale = ActualVersion < StaticPropVersion.V13
                ? new Vector3 { X = x, Y = x, Z = x }
                : new Vector3 { X = x, Y = reader.ReadSingle(), Z = reader.ReadSingle() };
        }
        Data.Add(prop);
        if (reader.BaseStream.Position - startpos != StructureSize)
            throw new InvalidDataException($"StaticProp structuresize doesn't match reader position after read ({reader.BaseStream.Position - startpos} != {StructureSize})");
    }

    protected override void WriteItem(BinaryWriter writer, int index)
    {
        var startPos = writer.BaseStream.Position;
        StaticProp prop = Data[index];
        writer.Write(prop.Origin.X);
        writer.Write(prop.Origin.Y);
        writer.Write(prop.Origin.Z);
        writer.Write(prop.Angle.Pitch);
        writer.Write(prop.Angle.Yaw);
        writer.Write(prop.Angle.Roll);

        // v4
        writer.Write(prop.PropType);
        writer.Write(prop.FirstLeaf);
        writer.Write(prop.LeafCount);
        writer.Write(prop.Solid);
        // every version except v7*
        //if (Version != StaticPropVersion.V7s)
        writer.Write(prop.Flags);
        // v4 still
        writer.Write(prop.Skin);
        writer.Write(prop.FadeMinDist);
        writer.Write(prop.FadeMaxDist);
        writer.Write(prop.LightingOrigin.X);
        writer.Write(prop.LightingOrigin.Y);
        writer.Write(prop.LightingOrigin.Z);
        // since v5
        if (ActualVersion >= StaticPropVersion.V5)
            writer.Write(prop.ForcedFadeScale);
        // v6, v7, and v7* only
        if (ActualVersion is StaticPropVersion.V6 or
            StaticPropVersion.V7 or
            StaticPropVersion.V7s)
        {
            writer.Write(prop.MinDXLevel);
            writer.Write(prop.MaxDXLevel);
        }
        // v7* only
        if (ActualVersion == StaticPropVersion.V7s)
        {
            writer.Write(prop.FlagsV7s);
            writer.Write(prop.LightmapResX);
            writer.Write(prop.LightmapResY);
        }
        // since v8
        if (ActualVersion >= StaticPropVersion.V8)
        {
            writer.Write(prop.MinCPULevel);
            writer.Write(prop.MaxCPULevel);
            writer.Write(prop.MinGPULevel);
            writer.Write(prop.MaxGPULevel);
        }
        // since v7
        if (ActualVersion >= StaticPropVersion.V7)
        {
            writer.Write(prop.DiffuseModulation.R);
            writer.Write(prop.DiffuseModulation.G);
            writer.Write(prop.DiffuseModulation.B);
            writer.Write(prop.DiffuseModulation.A);
        }
        // v9 and v10 only
        //and 10?
        if (ActualVersion >= StaticPropVersion.V9)
        {
            writer.Write(prop.DisableX360);
            writer.Write(new byte[3]);
        }
        // since v10
        if (ActualVersion >= StaticPropVersion.V10)
            writer.Write(prop.FlagsEx);
        // since v11
        if (ActualVersion >= StaticPropVersion.V11)
            writer.Write(prop.UniformScale);
        if (ActualVersion >= StaticPropVersion.V13)
        {
            writer.Write(prop.UniformScale.X);
            writer.Write(prop.UniformScale.Y);
            writer.Write(prop.UniformScale.Z);
        }

        if (writer.BaseStream.Position - startPos != StructureSize)
            throw new InvalidDataException($"StaticProp structuresize doesn't match writer position after write ({writer.BaseStream.Position - startPos} != {StructureSize})");
    }
}
