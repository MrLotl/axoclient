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
            var firstByte = buffered.ReadByte();
            var secondByte = buffered.ReadByte();
            var gzip = firstByte == 0x1F && secondByte == 0x8B;
            Stream stream;
            if (buffered.CanSeek)
            {
                buffered.Position = 0;
                stream = gzip ? new GZipStream(buffered, CompressionMode.Decompress) : buffered;
            }
            else
            {
                var all = new MemoryStream();
                all.WriteByte((byte)firstByte);
                all.WriteByte((byte)secondByte);
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
            var temporary = path + ".tmp";
            using (var file = File.Create(temporary))
            {
                using Stream stream = gzip ? new GZipStream(file, CompressionLevel.Optimal) : file;
                using var writer = new BinaryWriter(stream);
                writer.Write(Compound);
                writeString(writer, "");
                writePayload(writer, root);
            }
            File.Move(temporary, path, overwrite: true);
        }

        private object readPayload(BinaryReader reader, byte type, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new InvalidDataException("NBT zu tief verschachtelt.");
            }

            switch (type)
            {
                case Byte: return reader.ReadSByte();
                case Short: return BinaryPrimitives.ReadInt16BigEndian(reader.ReadBytes(2));
                case Int: return readInt(reader);
                case Long: return BinaryPrimitives.ReadInt64BigEndian(reader.ReadBytes(8));
                case Float: return BinaryPrimitives.ReadSingleBigEndian(reader.ReadBytes(4));
                case Double: return BinaryPrimitives.ReadDoubleBigEndian(reader.ReadBytes(8));
                case ByteArray: return reader.ReadBytes(readLength(reader));
                case String: return readString(reader);
                case List:
                    {
                        var list = new NbtList(reader.ReadByte());
                        var count = readLength(reader);
                        for (var index = 0; index < count; index++)
                        {
                            list.Add(readPayload(reader, list.ElementType, depth + 1));
                        }

                        return list;
                    }
                case Compound:
                    {
                        var compound = new NbtCompound();
                        byte tag;
                        while ((tag = reader.ReadByte()) != End)
                        {
                            compound[readString(reader)] = readPayload(reader, tag, depth + 1);
                        }

                        return compound;
                    }
                case IntArray:
                    {
                        var count = readLength(reader);
                        var values = new int[count];
                        for (var index = 0; index < count; index++)
                        {
                            values[index] = readInt(reader);
                        }

                        return values;
                    }
                case LongArray:
                    {
                        var count = readLength(reader);
                        var values = new long[count];
                        for (var index = 0; index < count; index++)
                        {
                            values[index] = BinaryPrimitives.ReadInt64BigEndian(reader.ReadBytes(8));
                        }

                        return values;
                    }
                default:
                    throw new InvalidDataException($"Unbekannter NBT-Typ {type}.");
            }
        }

        private int readInt(BinaryReader reader) => BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));

        private int readLength(BinaryReader reader)
        {
            var length = readInt(reader);
            if (length is < 0 or > MaxArray)
            {
                throw new InvalidDataException("Ungültige NBT-Länge.");
            }

            return length;
        }

        private string readString(BinaryReader reader)
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
            return Encoding.UTF8.GetString(reader.ReadBytes(length));
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

        private void writePayload(BinaryWriter binaryWriter, object value)
        {
            Span<byte> buffer = stackalloc byte[8];
            switch (value)
            {
                case bool flag: binaryWriter.Write((byte)(flag ? 1 : 0)); break;
                case sbyte byteValue: binaryWriter.Write(byteValue); break;
                case short shortValue: BinaryPrimitives.WriteInt16BigEndian(buffer, shortValue); binaryWriter.Write(buffer[..2]); break;
                case int intValue: writeInt(binaryWriter, intValue); break;
                case long longValue: BinaryPrimitives.WriteInt64BigEndian(buffer, longValue); binaryWriter.Write(buffer[..8]); break;
                case float floatValue: BinaryPrimitives.WriteSingleBigEndian(buffer, floatValue); binaryWriter.Write(buffer[..4]); break;
                case double doubleValue: BinaryPrimitives.WriteDoubleBigEndian(buffer, doubleValue); binaryWriter.Write(buffer[..8]); break;
                case byte[] bytes: writeInt(binaryWriter, bytes.Length); binaryWriter.Write(bytes); break;
                case string text: writeString(binaryWriter, text); break;
                case NbtList list:
                    binaryWriter.Write(list.Count == 0 ? list.ElementType : typeOf(list[0]));
                    writeInt(binaryWriter, list.Count);
                    foreach (var item in list)
                    {
                        writePayload(binaryWriter, item);
                    }

                    break;
                case NbtCompound compound:
                    foreach (var (key, item) in compound)
                    {
                        binaryWriter.Write(typeOf(item));
                        writeString(binaryWriter, key);
                        writePayload(binaryWriter, item);
                    }
                    binaryWriter.Write(End);
                    break;
                case int[] ints:
                    writeInt(binaryWriter, ints.Length);
                    foreach (var intValue in ints)
                    {
                        writeInt(binaryWriter, intValue);
                    }

                    break;
                case long[] longs:
                    writeInt(binaryWriter, longs.Length);
                    foreach (var longValue in longs)
                    {
                        BinaryPrimitives.WriteInt64BigEndian(buffer, longValue);
                        binaryWriter.Write(buffer[..8]);
                    }
                    break;
            }
        }

        private void writeInt(BinaryWriter binaryWriter, int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            binaryWriter.Write(buffer);
        }

        private void writeString(BinaryWriter binaryWriter, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length > ushort.MaxValue)
            {
                throw new InvalidDataException("NBT-Text zu lang.");
            }

            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)bytes.Length);
            binaryWriter.Write(buffer);
            binaryWriter.Write(bytes);
        }
    }
}
