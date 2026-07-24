using System.Buffers.Binary;
using System.Text;

namespace MooRCON.Core;

/// <summary>Тип пакета Source RCON.</summary>
public static class RconPacketType
{
    public const int ResponseValue = 0;
    public const int AuthResponse = 2;
    public const int ExecCommand = 2;
    public const int Auth = 3;
}

/// <summary>Разобранный пакет: заголовок + тело.</summary>
public readonly record struct RconPacket(int Size, int Id, int Type, string Body);

/// <summary>
/// Кодирование/декодирование пакетов Source RCON.
///
/// Формат: int32 size | int32 id | int32 type | тело | 0x00 | 0x00,
/// где size — длина всего, что идёт после него самого.
/// </summary>
public static class RconProtocol
{
    /// <summary>Порог, после которого ответ считается разрезанным на пакеты (спека Source — 4096 байт).</summary>
    public const int SplitThreshold = 4000;

    /// <summary>Защита от мусора в потоке: пакет заведомо больше этого быть не может.</summary>
    private const int MaxPacketSize = 64 * 1024;

    public static byte[] Encode(int id, int type, string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var buffer = new byte[12 + bodyBytes.Length + 2];

        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0), bodyBytes.Length + 10);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), id);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8), type);
        bodyBytes.CopyTo(buffer, 12);
        // Последние два байта уже нулевые: терминатор тела и терминатор пустой строки.
        return buffer;
    }

    public static async Task<RconPacket> ReadAsync(Stream stream, CancellationToken token)
    {
        var header = await ReadExactAsync(stream, 4, token);
        int size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size < 8 || size > MaxPacketSize)
            throw new IOException($"Некорректный размер пакета RCON: {size}.");

        var payload = await ReadExactAsync(stream, size, token);
        int id = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0));
        int type = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4));

        // Тело — всё между заголовком и двумя нулевыми терминаторами; часть серверов
        // шлёт только один, поэтому длину не считаем, а обрезаем нули с конца.
        int bodyLen = size - 8;
        while (bodyLen > 0 && payload[8 + bodyLen - 1] == 0) bodyLen--;

        return new RconPacket(size, id, type, Encoding.UTF8.GetString(payload, 8, bodyLen));
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken token)
    {
        var buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), token);
            if (read == 0) throw new IOException("Соединение закрыто сервером.");
            offset += read;
        }
        return buffer;
    }
}
