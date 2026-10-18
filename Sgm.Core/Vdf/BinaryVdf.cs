using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Sgm.Core.Vdf;

/// <summary>
/// Minimal dependency-free binary-VDF codec used by Steam's <c>shortcuts.vdf</c>.
/// Supports: 0x00 nested dict, 0x01 UTF-8 string, 0x02 int32 (LE), 0x08 end-of-object.
/// </summary>
public static class BinaryVdf
{
    private const byte TypeDict = 0x00;
    private const byte TypeString = 0x01;
    private const byte TypeInt = 0x02;
    private const byte TypeEnd = 0x08;

    public static Dictionary<string, object> Loads(byte[] data)
    {
        var pos = 0;
        return ReadObject(data, ref pos, topLevel: true);
    }

    public static byte[] Dumps(Dictionary<string, object> obj)
    {
        using var ms = new MemoryStream();
        WriteObject(ms, obj);
        return ms.ToArray();
    }

    private static Dictionary<string, object> ReadObject(byte[] buf, ref int pos, bool topLevel)
    {
        var result = new Dictionary<string, object>();
        while (pos < buf.Length)
        {
            var type = buf[pos++];
            if (type == TypeEnd)
                return result;

            var key = ReadCString(buf, ref pos);
            switch (type)
            {
                case TypeDict:
                    result[key] = ReadObject(buf, ref pos, topLevel: false);
                    break;
                case TypeString:
                    result[key] = ReadCString(buf, ref pos);
                    break;
                case TypeInt:
                    if (pos + 4 > buf.Length)
                        throw new InvalidDataException("Truncated int32 in binary VDF");
                    result[key] = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(pos, 4));
                    pos += 4;
                    break;
                default:
                    throw new InvalidDataException($"Unsupported binary VDF type 0x{type:x2} for key '{key}'");
            }
        }

        if (!topLevel)
            throw new InvalidDataException("Unterminated object in binary VDF (missing 0x08)");
        return result;
    }

    private static string ReadCString(byte[] buf, ref int pos)
    {
        var end = Array.IndexOf(buf, (byte)0, pos);
        if (end < 0)
            throw new InvalidDataException("Unterminated string in binary VDF");
        var s = Encoding.UTF8.GetString(buf, pos, end - pos);
        pos = end + 1;
        return s;
    }

    private static void WriteObject(Stream stream, Dictionary<string, object> obj)
    {
        var intBuf = new byte[4];
        foreach (var (key, value) in obj)
        {
            switch (value)
            {
                case Dictionary<string, object> nested:
                    stream.WriteByte(TypeDict);
                    WriteCString(stream, key);
                    WriteObject(stream, nested);
                    break;
                case int i:
                    stream.WriteByte(TypeInt);
                    WriteCString(stream, key);
                    BinaryPrimitives.WriteInt32LittleEndian(intBuf, i);
                    stream.Write(intBuf);
                    break;
                default:
                    stream.WriteByte(TypeString);
                    WriteCString(stream, key);
                    WriteCString(stream, value?.ToString() ?? "");
                    break;
            }
        }
        stream.WriteByte(TypeEnd);
    }

    private static void WriteCString(Stream stream, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        stream.Write(bytes);
        stream.WriteByte(0);
    }
}
