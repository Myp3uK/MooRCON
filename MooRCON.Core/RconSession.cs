using System.Net.Sockets;

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

    // Подключение и команды идут под одним замком: иначе переподключение (keep-alive,
    // таймаут, кнопка) может подменить сокет посреди чужой команды.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private int _nextId;
    private volatile bool _disposed;

    public bool IsConnected { get; private set; }

    public async Task ConnectAndAuthAsync(string host, int port, string password, int timeoutMs = 10000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Disconnect();

            using var cts = new CancellationTokenSource(timeoutMs);
            var tcp = new TcpClient { NoDelay = true };
            _tcp = tcp;
            try
            {
                await tcp.ConnectAsync(host, port, cts.Token);
                var stream = tcp.GetStream();
                _stream = stream;
                _nextId = 1;

                await SendAsync(stream, RconPacketType.Auth, password, cts.Token);

                // Часть серверов перед ответом на авторизацию шлёт пустой RESPONSE_VALUE.
                RconPacket packet;
                do
                {
                    packet = await RconProtocol.ReadAsync(stream, cts.Token);
                }
                while (packet.Type == RconPacketType.ResponseValue && packet.BodyBytes.Length == 0);

                // По спеке отказ — это id = -1. Совпадение id с запросом не проверяем:
                // некоторые серверы возвращают чужой id даже при успешной авторизации.
                if (packet.Id == -1)
                {
                    Disconnect();
                    throw new RconAuthException("Ошибка авторизации: неверный пароль.");
                }

                // Сессию закрыли, пока шло подключение — сокет не должен пережить владельца.
                ObjectDisposedException.ThrowIf(_disposed, this);

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
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> ExecuteAsync(string command, int timeoutMs = 10000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Команды по одному соединению строго последовательны: иначе keep-alive
        // и ввод пользователя разъедутся по ответам.
        await _gate.WaitAsync();
        try
        {
            // Проверяем уже под замком: пока ждали очередь, соединение могли закрыть.
            var stream = _stream;
            if (stream is null || !IsConnected)
                throw new InvalidOperationException("Нет активного соединения.");

            using var cts = new CancellationTokenSource(timeoutMs);
            await SendAsync(stream, RconPacketType.ExecCommand, command, cts.Token);
            return await RconProtocol.ReadResponseAsync(stream, ContinuationGraceMs, cts.Token);
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

    private async Task SendAsync(NetworkStream stream, int type, string body, CancellationToken token)
    {
        var packet = RconProtocol.Encode(_nextId++, type, body);
        await stream.WriteAsync(packet, token);
    }

    /// <summary>
    /// Закрывает сокет. Можно звать из любого потока и в любой момент: зависшее на нём
    /// чтение сразу завершится ошибкой.
    /// </summary>
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
        // Семафор не диспозим: подключение или команда могут ещё ждать на нём и должны
        // завершиться штатной ошибкой, а не ObjectDisposedException из недр SemaphoreSlim.
        _disposed = true;
        Disconnect();
        return ValueTask.CompletedTask;
    }
}

public sealed class RconAuthException : Exception
{
    public RconAuthException(string message) : base(message) { }
}
