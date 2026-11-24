using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using AxoClient.DataModel.Instances;
using AxoClient.Instances.Contract;

namespace AxoClient.Instances
{
    /// <summary>
    /// Liest und schreibt Minecrafts NBT (level.dat, servers.dat). Mit Obergrenzen, damit eine manipulierte Welt
    /// den Launcher nicht mit riesigen Arrays lahmlegt.
    /// </summary>
    public class NbtReader : INbtReader
    {
        private const byte End = 0, Byte = 1, Short = 2, Int = 3, Long = 4, Float = 5, Double = 6,
            ByteArray = 7, String = 8, List = 9, Compound = 10, IntArray = 11, LongArray = 12;

        private const int MaxArray = 16 * 1024 * 1024;
        private const int MaxDepth = 512;

        public NbtCompound Read(Stream input)
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
            {
                throw new InvalidDataException("Keine gültige NBT-Datei.");
            }

            readString(reader);
            return (NbtCompound)readPayload(reader, Compound, 0);
        }

        public NbtCompound ReadFile(string path)
        {
            using var file = File.OpenRead(path);
            return Read(file);
        }

        /// <summary>servers.dat ist unkomprimiert, level.dat GZip. Atomar schreiben.</summary>
        public void WriteFile(string path, NbtCompound root, bool gzip = false)
        {
            var tmp = path + ".tmp";
            using (var file = File.Create(tmp))
            {
                using Stream stream = gzip ? new GZipStream(file, CompressionLevel.Optimal) : file;
                using var writer = new BinaryWriter(stream);
                writer.Write(Compound);
                writeString(writer, "");
                writePayload(writer, root);
            }
            File.Move(tmp, path, overwrite: true);
        }

        private object readPayload(BinaryReader r, byte type, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new InvalidDataException("NBT zu tief verschachtelt.");
            }

            switch (type)
            {
                case Byte: return r.ReadSByte();
                case Short: return BinaryPrimitives.ReadInt16BigEndian(r.ReadBytes(2));
                case Int: return readInt(r);
                case Long: return BinaryPrimitives.ReadInt64BigEndian(r.ReadBytes(8));
                case Float: return BinaryPrimitives.ReadSingleBigEndian(r.ReadBytes(4));
                case Double: return BinaryPrimitives.ReadDoubleBigEndian(r.ReadBytes(8));
                case ByteArray: return r.ReadBytes(readLength(r));
                case String: return readString(r);
                case List:
                    {
                        var list = new NbtList(r.ReadByte());
                        var count = readLength(r);
                        for (var i = 0; i < count; i++)
                        {
                            list.Add(readPayload(r, list.ElementType, depth + 1));
                        }

                        return list;
                    }
                case Compound:
                    {
                        var compound = new NbtCompound();
                        byte tag;
                        while ((tag = r.ReadByte()) != End)
                        {
                            compound[readString(r)] = readPayload(r, tag, depth + 1);
                        }

                        return compound;
                    }
                case IntArray:
                    {
                        var count = readLength(r);
                        var values = new int[count];
                        for (var i = 0; i < count; i++)
                        {
                            values[i] = readInt(r);
                        }

                        return values;
                    }
                case LongArray:
                    {
                        var count = readLength(r);
                        var values = new long[count];
                        for (var i = 0; i < count; i++)
                        {
                            values[i] = BinaryPrimitives.ReadInt64BigEndian(r.ReadBytes(8));
                        }

                        return values;
                    }
                default:
                    throw new InvalidDataException($"Unbekannter NBT-Typ {type}.");
            }
        }

        private int readInt(BinaryReader r) => BinaryPrimitives.ReadInt32BigEndian(r.ReadBytes(4));

        private int readLength(BinaryReader r)
        {
            var length = readInt(r);
            if (length is < 0 or > MaxArray)
            {
                throw new InvalidDataException("Ungültige NBT-Länge.");
            }

            return length;
        }

        private string readString(BinaryReader r)
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(r.ReadBytes(2));
            return Encoding.UTF8.GetString(r.ReadBytes(length));
        }

        private byte typeOf(object value) => value switch
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

        private void writePayload(BinaryWriter w, object value)
        {
            Span<byte> buffer = stackalloc byte[8];
            switch (value)
            {
                case bool b: w.Write((byte)(b ? 1 : 0)); break;
                case sbyte b: w.Write(b); break;
                case short s: BinaryPrimitives.WriteInt16BigEndian(buffer, s); w.Write(buffer[..2]); break;
                case int i: writeInt(w, i); break;
                case long l: BinaryPrimitives.WriteInt64BigEndian(buffer, l); w.Write(buffer[..8]); break;
                case float f: BinaryPrimitives.WriteSingleBigEndian(buffer, f); w.Write(buffer[..4]); break;
                case double d: BinaryPrimitives.WriteDoubleBigEndian(buffer, d); w.Write(buffer[..8]); break;
                case byte[] bytes: writeInt(w, bytes.Length); w.Write(bytes); break;
                case string s: writeString(w, s); break;
                case NbtList list:
                    w.Write(list.Count == 0 ? list.ElementType : typeOf(list[0]));
                    writeInt(w, list.Count);
                    foreach (var item in list)
                    {
                        writePayload(w, item);
                    }

                    break;
                case NbtCompound compound:
                    foreach (var (key, item) in compound)
                    {
                        w.Write(typeOf(item));
                        writeString(w, key);
                        writePayload(w, item);
                    }
                    w.Write(End);
                    break;
                case int[] ints:
                    writeInt(w, ints.Length);
                    foreach (var i in ints)
                    {
                        writeInt(w, i);
                    }

                    break;
                case long[] longs:
                    writeInt(w, longs.Length);
                    foreach (var l in longs)
                    {
                        BinaryPrimitives.WriteInt64BigEndian(buffer, l);
                        w.Write(buffer[..8]);
                    }
                    break;
            }
        }

        private void writeInt(BinaryWriter w, int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            w.Write(buffer);
        }

        private void writeString(BinaryWriter w, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            if (bytes.Length > ushort.MaxValue)
            {
                throw new InvalidDataException("NBT-Text zu lang.");
            }

            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)bytes.Length);
            w.Write(buffer);
            w.Write(bytes);
        }
    }
}
