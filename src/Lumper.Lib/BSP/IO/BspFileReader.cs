namespace Lumper.Lib.BSP.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lumper.Lib.BSP.Lumps;
using Lumper.Lib.BSP.Lumps.BspLumps;
using Newtonsoft.Json;

public class BspFileReader(BspFile file, Stream input) : LumpReader(input)
{
    [JsonIgnore]
    private readonly BspFile _bsp = file;

    [JsonProperty]
    public IReadOnlyDictionary<BspLumpType, LumpHeader> Headers => Lumps.ToDictionary(
        x => x.Item1 is Lump<BspLumpType> lump
            ? lump.Type
            : BspLumpType.Unknown,
        x => x.Item2);

    public void Load()
    {
        Lumps.Clear();
        _bsp.Lumps.Clear();
        ReadHeader();
        LoadAll();
        ResolveTexNames();
        ResolveTexData();
    }

    protected override void ReadHeader()
    {
        if (BaseStream.Position != 0)
            BaseStream.Seek(0, SeekOrigin.Begin);

        var ident = ReadBytes(4);

        if (Encoding.Default.GetString(ident) != "VBSP")
            throw new InvalidDataException("File doesn't look like a VBSP");

        _bsp.Version = ReadInt32();
        Console.WriteLine($"BSP version: {_bsp.Version}");

        for (var i = 0; i < BspFile.HeaderLumps; i++)
        {
            var type = (BspLumpType)i;

            Lump<BspLumpType> lump = type switch
            {
                BspLumpType.Entities => new EntityLump(_bsp),
                BspLumpType.Texinfo => new TexInfoLump(_bsp),
                BspLumpType.Texdata => new TexDataLump(_bsp),
                BspLumpType.TexdataStringTable => new TexDataStringTableLump(_bsp),
                BspLumpType.TexdataStringData => new TexDataStringDataLump(_bsp),
                BspLumpType.Pakfile => new PakFileLump(_bsp),
                BspLumpType.GameLump => new GameLump(_bsp),
                _ => new UnmanagedLump<BspLumpType>(_bsp)
            };
            LumpHeader lumpHeader = new();

            lump.Type = type;
            lumpHeader.Offset = ReadInt32();
            var length = ReadInt32();
            lump.Version = ReadInt32();
            var fourCc = ReadInt32();
            if (fourCc == 0)
            {
                lumpHeader.CompressedLength = -1;
                lumpHeader.UncompressedLength = length;
            }
            else
            {
                lumpHeader.CompressedLength = length;
                lumpHeader.UncompressedLength = fourCc;
            }

            Console.WriteLine($"Lump {type}({(int)type})"
                              + $"\t\tOffset: {lumpHeader.Offset}"
                              + $"\t\tLength: {length}"
                              + $"\t\tVersion: {lump.Version}"
                              + $"\t\tFourCc: {fourCc}");

            _bsp.Lumps.Add(type, lump);
            Lumps.Add(new Tuple<Lump, LumpHeader>(lump, lumpHeader));
        }

        _bsp.Revision = ReadInt32();

        UpdateGameLumpLength();

        SortLumps();

        if (CheckOverlapping())
            throw new InvalidDataException("Some lumps are overlapping. Check logging for details.");
    }

    //finding the real gamelump length by looking at the next lump
    private void UpdateGameLumpLength()
    {
        Lump gameLump = null;
        LumpHeader gameLumpHeader = null;
        foreach (Tuple<Lump, LumpHeader>? l in Lumps.OrderBy(x => x.Item2.Offset))
        {
            Lump lump = l.Item1;
            LumpHeader header = l.Item2;
            if (lump is GameLump)
            {
                gameLump = lump;
                gameLumpHeader = header;
                if (gameLumpHeader.Length == 0 && gameLumpHeader.Offset == 0)
                {
                    Console.WriteLine("GameLump length and offset 0 .. won't set new length");
                    break;
                }
            }
            else if (gameLump is not null && header.Offset != 0 && header.Offset != gameLumpHeader.Offset)
            {
                gameLumpHeader.UncompressedLength = header.Offset - gameLumpHeader.Offset;
                Console.WriteLine($"Changed gamelump length to {gameLumpHeader.Length}");
                break;
            }
        }
    }

    //sort by offset so the output file looks more like the input
    private void SortLumps()
    {
        Dictionary<BspLumpType, Lump<BspLumpType>> newLumps = [];
        foreach (Tuple<Lump, LumpHeader>? l in Lumps.OrderBy(x => x.Item2.Offset))
        {
            KeyValuePair<BspLumpType, Lump<BspLumpType>> temp = _bsp.Lumps.First(x => x.Value == l.Item1);
            newLumps.Add(temp.Key, temp.Value);
            _bsp.Lumps.Remove(temp.Key);
        }

        if (_bsp.Lumps.Count != 0)
            throw new InvalidDataException("SortLumps error: BSP lumps and reader headers didn't match!");
        _bsp.Lumps = newLumps;
    }

    //for testing
    private bool CheckOverlapping()
    {
        var ret = false;
        Lump<BspLumpType> prevLump = null;
        LumpHeader prevHeader = null;
        var first = true;
        foreach (Tuple<Lump, LumpHeader>? l in Lumps.OrderBy(x => x.Item2.Offset))
        {
            var lump = (Lump<BspLumpType>)l.Item1;
            LumpHeader header = l.Item2;
            if (first)
            {
                first = false;
                prevLump = lump;
                prevHeader = header;
            }
            else if (header.Length > 0)
            {
                var prevEnd = prevHeader.Offset + prevHeader.Length;
                if (header.Offset < prevEnd)
                {
                    Console.WriteLine($"Lumps {prevLump.Type} and {lump.Type} overlapping");
                    if (prevLump.Type == BspLumpType.GameLump)
                        Console.WriteLine("but the previous lump was GAME_LUMP and the length is a lie");
                    else
                        ret = true;
                }
                else if (header.Offset > prevEnd)
                {
                    Console.WriteLine($"Space between lumps {prevLump.Type} {prevEnd} <-- {header.Offset - prevEnd} --> {header.Offset} {lump.Type}");
                }

                if (header.Offset + header.Length >= prevEnd)
                {
                    prevLump = lump;
                    prevHeader = header;
                }
            }
        }
        return ret;
    }

    private void ResolveTexNames()
    {
        TexDataLump texDataLump = _bsp.GetLump<TexDataLump>();
        foreach (Struct.TexData texture in texDataLump.Data)
        {
            var name = new StringBuilder();
            TexDataStringTableLump texDataStringTableLump = _bsp.GetLump<TexDataStringTableLump>();
            var stringTableOffset = texDataStringTableLump.Data[texture.StringTablePointer];
            TexDataStringDataLump texDataStringDataLump = _bsp.GetLump<TexDataStringDataLump>();

            var end = Array.FindIndex(texDataStringDataLump.Data, stringTableOffset, x => x == 0);
            if (end < 0)
            {
                end = texDataStringDataLump.Data.Length;
                Console.WriteLine("WARING: didn't find null at the end of texture string");
            }
            texture.TexName = end > 0
                ? TexDataStringDataLump.TextureNameEncoding.GetString(
                    texDataStringDataLump.Data,
                    stringTableOffset,
                    end - stringTableOffset)
                : "";
        }
    }

    private void ResolveTexData()
    {
        TexInfoLump texInfoLump = _bsp.GetLump<TexInfoLump>();
        foreach (Struct.TexInfo texInfo in texInfoLump.Data)
        {
            TexDataLump texDataLump = _bsp.GetLump<TexDataLump>();
            texInfo.TexData = texDataLump.Data[texInfo.TexDataPointer];
        }
    }
}