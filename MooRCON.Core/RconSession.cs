using System.Net.Sockets;
using System.Text;

namespace MooRCON.Core;

/// <summary>
/// Одно RCON-подключение к серверу. Каждая вкладка UI держит свой экземпляр —
/// подключения независимы.
///
/// Ответы читаются строго по порядку прихода, а не по id пакета: в некоторых
/// сборках Conan сервер возвращает id предыдущего запроса, и сопоставление по id
/// там не работает вовсе. По одному соединению RCON последователен, поэтому
/// порядка достаточно.
/// </summary>
public sealed class RconSession : IAsyncDisposable
{
    /// <summary>Сколько ждать продолжение разрезанного ответа после очередного пакета.</summary>
    private const int ContinuationGraceMs = 1500;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private int _nextId;

    public bool IsConnected { get; private set; }

    public async Task ConnectAndAuthAsync(string host, int port, string password, int timeoutMs = 10000)
    {
        Disconnect();

        using var cts = new CancellationTokenSource(timeoutMs);
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            await tcp.ConnectAsync(host, port, cts.Token);
            _tcp = tcp;
            _stream = tcp.GetStream();
            _nextId = 1;

            await SendAsync(RconPacketType.Auth, password, cts.Token);

            // Часть серверов перед ответом на авторизацию шлёт пустой RESPONSE_VALUE.
            RconPacket packet;
            do
            {
                packet = await RconProtocol.ReadAsync(_stream, cts.Token);
            }
            while (packet.Type == RconPacketType.ResponseValue && packet.Body.Length == 0);

            // По спеке отказ — это id = -1. Совпадение id с запросом не проверяем:
            // некоторые серверы возвращают чужой id даже при успешной авторизации.
            if (packet.Id == -1)
            {
                Disconnect();
                throw new RconAuthException("Ошибка авторизации: неверный пароль.");
            }

            IsConnected = true;
        }
        catch (RconAuthException)
        {
            throw;
        }
        catch
        {
            Disconnect();
            throw;
        }
    }

    public async Task<string> ExecuteAsync(string command, int timeoutMs = 10000)
    {
        if (_stream is null || !IsConnected)
            throw new InvalidOperationException("Нет активного соединения.");

        // Команды по одному соединению строго последовательны: иначе keep-alive
        // и ввод пользователя разъедутся по ответам.
        await _gate.WaitAsync();
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await SendAsync(RconPacketType.ExecCommand, command, cts.Token);

            var packet = await RconProtocol.ReadAsync(_stream, cts.Token);
            var body = new StringBuilder(packet.Body);

            // Ответ длиннее ~4 КБ сервер режет на несколько пакетов. Признак продолжения —
            // пакет, забитый до предела; хвост приходит следом почти мгновенно.
            while (packet.Size >= RconProtocol.SplitThreshold)
            {
                using var grace = new CancellationTokenSource(ContinuationGraceMs);
                try
                {
                    packet = await RconProtocol.ReadAsync(_stream, grace.Token);
                    body.Append(packet.Body);
                }
                catch (OperationCanceledException)
                {
                    break; // продолжения нет — ответ был ровно на границе
                }
            }

            return body.ToString();
        }
        catch
        {
            // Ответ не дочитан — дальше по этому сокету поток пакетов не сходится.
            Disconnect();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SendAsync(int type, string body, CancellationToken token)
    {
        var packet = RconProtocol.Encode(_nextId++, type, body);
        await _stream!.WriteAsync(packet, token);
    }

    public void Disconnect()
    {
        IsConnected = false;
        try { _stream?.Dispose(); } catch { /* возможна гонка — игнорируем */ }
        try { _tcp?.Dispose(); } catch { }
        _stream = null;
        _tcp = null;
    }

    public ValueTask DisposeAsync()
    {
        Disconnect();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class RconAuthException : Exception
{
    public RconAuthException(string message) : base(message) { }
}
