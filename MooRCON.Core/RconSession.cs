using RconSharp;

namespace MooRCON.Core;

/// <summary>
/// Одно RCON-подключение к серверу. Каждая вкладка UI держит свой экземпляр —
/// подключения независимы.
/// </summary>
public sealed class RconSession : IAsyncDisposable
{
    private RconClient? _client;

    public bool IsConnected { get; private set; }

    public async Task ConnectAndAuthAsync(string host, int port, string password)
    {
        Disconnect();
        _client = RconClient.Create(host, port);
        await _client.ConnectAsync();

        bool ok = await _client.AuthenticateAsync(password);
        if (!ok)
        {
            Disconnect();
            throw new RconAuthException("Ошибка авторизации: неверный пароль.");
        }
        IsConnected = true;
    }

    public async Task<string> ExecuteAsync(string command, int timeoutMs = 10000)
    {
        if (_client is null || !IsConnected)
            throw new InvalidOperationException("Нет активного соединения.");

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            return await _client.ExecuteCommandAsync(command).WaitAsync(cts.Token);
        }
        catch
        {
            // Если соединение отвалилось во время команды — отражаем это в состоянии.
            IsConnected = _client is not null && IsConnected;
            throw;
        }
    }

    public void Disconnect()
    {
        IsConnected = false;
        try { _client?.Disconnect(); } catch { /* возможна гонка — игнорируем */ }
        _client = null;
    }

    public ValueTask DisposeAsync()
    {
        Disconnect();
        return ValueTask.CompletedTask;
    }
}

public sealed class RconAuthException : Exception
{
    public RconAuthException(string message) : base(message) { }
}
