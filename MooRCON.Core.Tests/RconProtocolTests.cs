using System.Buffers.Binary;
using System.Text;
using MooRCON.Core;

namespace MooRCON.Core.Tests;

public class RconProtocolTests
{
    [Fact]
    public async Task Encode_Then_Read_RoundTrips()
    {
        var bytes = RconProtocol.Encode(7, RconPacketType.ExecCommand, "listplayers");
        var packet = await RconProtocol.ReadAsync(new MemoryStream(bytes), CancellationToken.None);

        Assert.Equal(7, packet.Id);
        Assert.Equal(RconPacketType.ExecCommand, packet.Type);
        Assert.Equal("listplayers", packet.Body);
        Assert.Equal(bytes.Length - 4, packet.Size);
    }

    [Fact]
    public async Task Read_AcceptsSingleNullTerminator()
    {
        // Часть серверов шлёт только один завершающий ноль.
        var body = Encoding.UTF8.GetBytes("ok");
        var buf = new byte[12 + body.Length + 1];
        BinaryPrimitives.WriteInt32LittleEndian(buf, 8 + body.Length + 1);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4), 1);
        body.CopyTo(buf, 12);

        var packet = await RconProtocol.ReadAsync(new MemoryStream(buf), CancellationToken.None);
        Assert.Equal("ok", packet.Body);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(10_000_000)]
    public async Task Read_RejectsGarbageSize(int size)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, size);
        await Assert.ThrowsAsync<IOException>(
            () => RconProtocol.ReadAsync(new MemoryStream(buf), CancellationToken.None));
    }

    [Fact]
    public async Task Read_ThrowsWhenStreamEndsMidPacket()
    {
        var bytes = RconProtocol.Encode(1, 0, "hello");
        var truncated = bytes.AsSpan(0, bytes.Length - 3).ToArray();
        await Assert.ThrowsAsync<IOException>(
            () => RconProtocol.ReadAsync(new MemoryStream(truncated), CancellationToken.None));
    }

    [Fact]
    public async Task ReadResponse_SinglePacket()
    {
        var stream = new MemoryStream(RconProtocol.Encode(1, 0, "Привет"));
        var text = await RconProtocol.ReadResponseAsync(stream, 100, CancellationToken.None);
        Assert.Equal("Привет", text);
    }

    [Fact]
    public async Task ReadResponse_GluesSplitPacketsWithoutBreakingCyrillic()
    {
        // Ответ из кириллицы, разрезанный сервером по байтам так, что граница пакета
        // приходится на середину двухбайтового символа.
        var full = string.Concat(Enumerable.Repeat("Игрок Вася; ", 280));
        var bytes = Encoding.UTF8.GetBytes(full);
        // Первый байт-продолжение UTF-8 после порога — резать там значит резать символ.
        int cut = RconProtocol.SplitThreshold;
        while ((bytes[cut] & 0xC0) != 0x80) cut++;

        var stream = new MemoryStream();
        stream.Write(RawPacket(bytes.AsSpan(0, cut)));
        stream.Write(RawPacket(bytes.AsSpan(cut)));
        stream.Position = 0;

        var text = await RconProtocol.ReadResponseAsync(stream, 1000, CancellationToken.None);

        Assert.Equal(full, text);
        Assert.DoesNotContain('�', text);
    }

    [Fact]
    public async Task ReadResponse_FullPacketWithoutContinuation_ReturnsAfterGrace()
    {
        // Ответ ровно на границе: пакет «полный», но продолжения нет — ждём grace и отдаём.
        var body = new string('x', RconProtocol.SplitThreshold);
        var stream = new StallingStream(RawPacket(Encoding.UTF8.GetBytes(body)));

        var text = await RconProtocol.ReadResponseAsync(stream, 100, CancellationToken.None);
        Assert.Equal(body, text);
    }

    [Fact]
    public async Task ReadResponse_OuterTimeoutIsNotSwallowed()
    {
        var stream = new StallingStream([]);
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RconProtocol.ReadResponseAsync(stream, 50, cts.Token));
    }

    private static byte[] RawPacket(ReadOnlySpan<byte> body)
    {
        var buf = new byte[12 + body.Length + 2];
        BinaryPrimitives.WriteInt32LittleEndian(buf, body.Length + 10);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4), 1);
        body.CopyTo(buf.AsSpan(12));
        return buf;
    }

    /// <summary>Отдаёт данные, а потом «висит» до отмены — как живой сокет без новых пакетов.</summary>
    private sealed class StallingStream(byte[] data) : Stream
    {
        private int _pos;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (_pos >= data.Length)
            {
                await Task.Delay(Timeout.Infinite, token);
                return 0;
            }
            int n = Math.Min(buffer.Length, data.Length - _pos);
            data.AsMemory(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
