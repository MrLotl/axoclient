using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace AxoClient.Core.Instances;

public sealed class NbtCompound : Dictionary<string, object>
{
    public T? Get<T>(string key) => TryGetValue(key, out var v) && v is T t ? t : default;
}

public sealed class NbtList(byte elementType) : List<object>
{
    public byte ElementType { get; set; } = elementType;
}

/// <summary>
/// Liest und schreibt Minecrafts NBT (level.dat, servers.dat). Mit Obergrenzen, damit eine manipulierte Welt
/// den Launcher nicht mit riesigen Arrays lahmlegt.
/// </summary>
public static class Nbt
{
    private const byte End = 0, Byte = 1, Short = 2, Int = 3, Long = 4, Float = 5, Double = 6,
        ByteArray = 7, String = 8, List = 9, Compound = 10, IntArray = 11, LongArray = 12;

    private const int MaxArray = 16 * 1024 * 1024;
    private const int MaxDepth = 512;

    public static NbtCompound Read(Stream input)
    {
        var buffered = new BufferedStream(input);
        var b1 = buffered.ReadByte();
        var b2 = buffered.ReadByte();
        var gzip = b1 == 0x1F && b2 == 0x8B;
        Stream stream;
        if (buffered.CanSeek)
        {
            buffered.Position = 0;
            stream = gzip ? new GZipStream(buffered, CompressionMode.Decompress) : buffered;
        }
        else
        {
            var all = new MemoryStream();
            all.WriteByte((byte)b1);
            all.WriteByte((byte)b2);
            buffered.CopyTo(all);
            all.Position = 0;
            stream = gzip ? new GZipStream(all, CompressionMode.Decompress) : all;
        }

        using var reader = new BinaryReader(stream);
        if (reader.ReadByte() != Compound)
            throw new InvalidDataException("Keine gültige NBT-Datei.");
        ReadString(reader);
        return (NbtCompound)ReadPayload(reader, Compound, 0);
    }

    public static NbtCompound ReadFile(string path)
    {
        using var file = File.OpenRead(path);
        return Read(file);
    }

    /// <summary>servers.dat ist unkomprimiert, level.dat GZip. Atomar schreiben.</summary>
    public static void WriteFile(string path, NbtCompound root, bool gzip = false)
    {
        var tmp = path + ".tmp";
        using (var file = File.Create(tmp))
        {
            using Stream stream = gzip ? new GZipStream(file, CompressionLevel.Optimal) : file;
            using var writer = new BinaryWriter(stream);
            writer.Write(Compound);
            WriteString(writer, "");
            WritePayload(writer, root);
        }
        File.Move(tmp, path, overwrite: true);
    }

    private static object ReadPayload(BinaryReader r, byte type, int depth)
    {
        if (depth > MaxDepth)
            throw new InvalidDataException("NBT zu tief verschachtelt.");
        switch (type)
        {
            case Byte: return r.ReadSByte();
            case Short: return BinaryPrimitives.ReadInt16BigEndian(r.ReadBytes(2));
            case Int: return ReadInt(r);
            case Long: return BinaryPrimitives.ReadInt64BigEndian(r.ReadBytes(8));
            case Float: return BinaryPrimitives.ReadSingleBigEndian(r.ReadBytes(4));
            case Double: return BinaryPrimitives.ReadDoubleBigEndian(r.ReadBytes(8));
            case ByteArray: return r.ReadBytes(ReadLength(r));
            case String: return ReadString(r);
            case List:
            {
                var list = new NbtList(r.ReadByte());
                var count = ReadLength(r);
                for (var i = 0; i < count; i++)
                    list.Add(ReadPayload(r, list.ElementType, depth + 1));
                return list;
            }
            case Compound:
            {
                var compound = new NbtCompound();
                byte tag;
                while ((tag = r.ReadByte()) != End)
                    compound[ReadString(r)] = ReadPayload(r, tag, depth + 1);
                return compound;
            }
            case IntArray:
            {
                var count = ReadLength(r);
                var values = new int[count];
                for (var i = 0; i < count; i++)
                    values[i] = ReadInt(r);
                return values;
            }
            case LongArray:
            {
                var count = ReadLength(r);
                var values = new long[count];
                for (var i = 0; i < count; i++)
                    values[i] = BinaryPrimitives.ReadInt64BigEndian(r.ReadBytes(8));
                return values;
            }
            default:
                throw new InvalidDataException($"Unbekannter NBT-Typ {type}.");
        }
    }

    private static int ReadInt(BinaryReader r) => BinaryPrimitives.ReadInt32BigEndian(r.ReadBytes(4));

    private static int ReadLength(BinaryReader r)
    {
        var length = ReadInt(r);
        if (length is < 0 or > MaxArray)
            throw new InvalidDataException("Ungültige NBT-Länge.");
        return length;
    }

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
                foreach (var i in ints)
                    WriteInt(w, i);
                break;
            case long[] longs:
                WriteInt(w, longs.Length);
                foreach (var l in longs)
                {
                    BinaryPrimitives.WriteInt64BigEndian(buffer, l);
                    w.Write(buffer[..8]);
                }
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
        if (bytes.Length > ushort.MaxValue)
            throw new InvalidDataException("NBT-Text zu lang.");
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)bytes.Length);
        w.Write(buffer);
        w.Write(bytes);
    }
}
