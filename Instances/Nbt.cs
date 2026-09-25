using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace AxoClient.Instances;

public class NbtCompound : Dictionary<string, object>
{
    public T? Get<T>(string key) => TryGetValue(key, out var v) && v is T t ? t : default;
}

public class NbtList(byte elementType) : List<object>
{
    public byte ElementType { get; set; } = elementType;
}

public static class Nbt
{
    private const byte End = 0, Byte = 1, Short = 2, Int = 3, Long = 4, Float = 5, Double = 6,
        ByteArray = 7, String = 8, List = 9, Compound = 10, IntArray = 11, LongArray = 12;

    public static NbtCompound ReadFile(string path)
    {
        using var file = File.OpenRead(path);
        var gzip = file.ReadByte() == 0x1F && file.ReadByte() == 0x8B;
        file.Position = 0;
        using Stream stream = gzip ? new GZipStream(file, CompressionMode.Decompress) : file;
        using var reader = new BinaryReader(new BufferedStream(stream));
        if (reader.ReadByte() != Compound)
            throw new InvalidDataException("Keine gültige NBT-Datei.");
        ReadString(reader);
        return (NbtCompound)ReadPayload(reader, Compound);
    }

    public static void WriteFile(string path, NbtCompound root)
    {
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        writer.Write(Compound);
        WriteString(writer, "");
        WritePayload(writer, root);
    }

    private static object ReadPayload(BinaryReader r, byte type)
    {
        switch (type)
        {
            case Byte: return r.ReadSByte();
            case Short: return BinaryPrimitives.ReadInt16BigEndian(r.ReadBytes(2));
            case Int: return BinaryPrimitives.ReadInt32BigEndian(r.ReadBytes(4));
            case Long: return BinaryPrimitives.ReadInt64BigEndian(r.ReadBytes(8));
            case Float: return BinaryPrimitives.ReadSingleBigEndian(r.ReadBytes(4));
            case Double: return BinaryPrimitives.ReadDoubleBigEndian(r.ReadBytes(8));
            case ByteArray: return r.ReadBytes(ReadInt(r));
            case String: return ReadString(r);
            case List:
            {
                var list = new NbtList(r.ReadByte());
                var count = ReadInt(r);
                for (var i = 0; i < count; i++)
                    list.Add(ReadPayload(r, list.ElementType));
                return list;
            }
            case Compound:
            {
                var compound = new NbtCompound();
                byte tag;
                while ((tag = r.ReadByte()) != End)
                    compound[ReadString(r)] = ReadPayload(r, tag);
                return compound;
            }
            case IntArray:
                return Enumerable.Range(0, ReadInt(r)).Select(_ => ReadInt(r)).ToArray();
            case LongArray:
            {
                var count = ReadInt(r);
                return Enumerable.Range(0, count).Select(_ => BinaryPrimitives.ReadInt64BigEndian(r.ReadBytes(8))).ToArray();
            }
            default:
                throw new InvalidDataException($"Unbekannter NBT-Typ {type}.");
        }
    }

    private static int ReadInt(BinaryReader r) => BinaryPrimitives.ReadInt32BigEndian(r.ReadBytes(4));

    private static string ReadString(BinaryReader r)
    {
        var length = BinaryPrimitives.ReadUInt16BigEndian(r.ReadBytes(2));
        return Encoding.UTF8.GetString(r.ReadBytes(length));
    }

    private static byte TypeOf(object value) => value switch
    {
        sbyte or bool => Byte,
        short => Short,
        int => Int,
        long => Long,
        float => Float,
        double => Double,
        byte[] => ByteArray,
        string => String,
        NbtList => List,
        NbtCompound => Compound,
        int[] => IntArray,
        long[] => LongArray,
        _ => throw new InvalidDataException($"Typ {value.GetType()} kann nicht als NBT gespeichert werden.")
    };

    private static void WritePayload(BinaryWriter w, object value)
    {
        Span<byte> buffer = stackalloc byte[8];
        switch (value)
        {
            case bool b: w.Write((byte)(b ? 1 : 0)); break;
            case sbyte b: w.Write(b); break;
            case short s: BinaryPrimitives.WriteInt16BigEndian(buffer, s); w.Write(buffer[..2]); break;
            case int i: WriteInt(w, i); break;
            case long l: BinaryPrimitives.WriteInt64BigEndian(buffer, l); w.Write(buffer[..8]); break;
            case float f: BinaryPrimitives.WriteSingleBigEndian(buffer, f); w.Write(buffer[..4]); break;
            case double d: BinaryPrimitives.WriteDoubleBigEndian(buffer, d); w.Write(buffer[..8]); break;
            case byte[] bytes: WriteInt(w, bytes.Length); w.Write(bytes); break;
            case string s: WriteString(w, s); break;
            case NbtList list:
                w.Write(list.Count == 0 ? list.ElementType : TypeOf(list[0]));
                WriteInt(w, list.Count);
                foreach (var item in list)
                    WritePayload(w, item);
                break;
            case NbtCompound compound:
                foreach (var (key, item) in compound)
                {
                    w.Write(TypeOf(item));
                    WriteString(w, key);
                    WritePayload(w, item);
                }
                w.Write(End);
                break;
            case int[] ints:
                WriteInt(w, ints.Length);
                foreach (var i in ints) WriteInt(w, i);
                break;
            case long[] longs:
                WriteInt(w, longs.Length);
                foreach (var l in longs) { BinaryPrimitives.WriteInt64BigEndian(buffer, l); w.Write(buffer[..8]); }
                break;
        }
    }

    private static void WriteInt(BinaryWriter w, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        w.Write(buffer);
    }

    private static void WriteString(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)bytes.Length);
        w.Write(buffer);
        w.Write(bytes);
    }
}
