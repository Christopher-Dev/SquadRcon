// Event argument classes
using SquadRcon.Classes;
using SquadRcon.Classes.Constants;
using SquadRcon.Classes.Packets;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

public class AuthenticationEventArgs : EventArgs
{
    public bool IsSuccess { get; }
    public AuthenticationEventArgs(bool isSuccess) => IsSuccess = isSuccess;
}

public class ChatMessageEventArgs : EventArgs
{
    public string Message { get; }
    public ChatMessageEventArgs(string message) => Message = message;
}

public class CommandResponseEventArgs : EventArgs
{
    public string Response { get; }
    public CommandResponseEventArgs(string response) => Response = response;
}

public class RconErrorEventArgs : EventArgs
{
    public string ErrorMessage { get; }
    public Exception Exception { get; }
    public RconErrorEventArgs(string errorMessage, Exception exception = null)
    {
        ErrorMessage = errorMessage;
        Exception = exception;
    }
}

// Interfaces for better testability and separation of concerns
public interface IRconClient : IAsyncDisposable
{
    string Host { get; }
    int Port { get; }
    bool IsAuthorized { get; }
    DateTimeOffset ConnectedOn { get; }

    event EventHandler<AuthenticationEventArgs> AuthenticationResult;
    event EventHandler<ChatMessageEventArgs> ChatMessageReceived;
    event EventHandler<CommandResponseEventArgs> CommandResponseReceived;
    event EventHandler<RconErrorEventArgs> ErrorOccurred;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    ValueTask AuthenticateAsync(string password);
    ValueTask QueueCommandAsync(Packet packet);
}

public interface IPacketProcessor
{
    ValueTask ProcessPacketAsync(ResponsePacket packet);
}

public interface INetworkStreamWrapper : IAsyncDisposable
{
    bool DataAvailable { get; }
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
    ValueTask FlushAsync(CancellationToken cancellationToken = default);
}

// Network stream wrapper for better testability
public class NetworkStreamWrapper : INetworkStreamWrapper
{
    private readonly NetworkStream _stream;

    public NetworkStreamWrapper(NetworkStream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public bool DataAvailable => _stream.DataAvailable;

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _stream.ReadAsync(buffer, cancellationToken);

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _stream.WriteAsync(buffer, cancellationToken);

    public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
        new ValueTask(_stream.FlushAsync(cancellationToken));

    public async ValueTask DisposeAsync() => await _stream.DisposeAsync();
}

// Packet processor - Single Responsibility
public class RconPacketProcessor : IPacketProcessor
{
    private readonly StringBuilder _responseBuilder = new();
    private readonly Action<bool> _onAuthenticated;
    private readonly Action<string> _onChatMessage;
    private readonly Action<string> _onCommandResponse;
    private readonly Action<string, Exception> _onError;

    public RconPacketProcessor(
        Action<bool> onAuthenticated,
        Action<string> onChatMessage,
        Action<string> onCommandResponse,
        Action<string, Exception> onError)
    {
        _onAuthenticated = onAuthenticated ?? throw new ArgumentNullException(nameof(onAuthenticated));
        _onChatMessage = onChatMessage ?? throw new ArgumentNullException(nameof(onChatMessage));
        _onCommandResponse = onCommandResponse ?? throw new ArgumentNullException(nameof(onCommandResponse));
        _onError = onError ?? throw new ArgumentNullException(nameof(onError));
    }

    public async ValueTask ProcessPacketAsync(ResponsePacket packet)
    {
        switch (packet.Type)
        {
            case RconConstants.AuthSuccess:
                _onAuthenticated(true);
                break;

            case RconConstants.SERVERDATA_CHAT_VALUE:
                var chatMessage = packet.Body.FirstOrDefault();
                if (!string.IsNullOrEmpty(chatMessage))
                {
                    _onChatMessage(chatMessage);
                }
                break;

            case RconConstants.EmptyPacket:
                // No action needed
                break;

            case RconConstants.SERVERDATA_RESPONSE_VALUE:
                _responseBuilder.AppendJoin(Environment.NewLine, packet.Body);
                break;

            case RconConstants.CommandComplete:
                await ProcessCompleteResponseAsync();
                break;

            default:
                _onError("Error Parsing Response - Unknown packet type received", null);
                break;
        }
    }

    private async ValueTask ProcessCompleteResponseAsync()
    {
        var response = _responseBuilder.ToString();
        _onCommandResponse(response);
        _responseBuilder.Clear();
        await Task.CompletedTask; // Maintain async signature for future extensibility
    }
}

// Connection manager - Single Responsibility
public class RconConnectionManager : IAsyncDisposable
{
    private TcpClient _tcpClient;
    private INetworkStreamWrapper _stream;
    private readonly SemaphoreSlim _streamLock = new(1, 1);

    public bool IsConnected => _tcpClient?.Connected == true;

    public async Task<INetworkStreamWrapper> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            _tcpClient = new TcpClient();
            _tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            await _tcpClient.ConnectAsync(host, port, cancellationToken);
            var networkStream = _tcpClient.GetStream();
            _stream = new NetworkStreamWrapper(networkStream);

            return _stream;
        }
        catch (Exception ex)
        {
            await DisposeAsync();
            throw new InvalidOperationException($"Failed to connect to {host}:{port}", ex);
        }
    }

    public SemaphoreSlim GetStreamLock() => _streamLock;

    public async ValueTask DisposeAsync()
    {
        if (_stream != null)
        {
            await _stream.DisposeAsync();
            _stream = null;
        }

        _tcpClient?.Dispose();
        _tcpClient = null;

        _streamLock.Dispose();
    }
}

// Main RCON client - follows Open/Closed and Dependency Inversion principles
public class RconClient : IRconClient
{
    private readonly RconConnectionManager _connectionManager;
    private readonly Channel<Packet> _commandQueue;
    private readonly IPacketProcessor _packetProcessor;
    private INetworkStreamWrapper _stream;
    private readonly byte[] _readBuffer = new byte[4];

    // Events
    public event EventHandler<AuthenticationEventArgs> AuthenticationResult;
    public event EventHandler<ChatMessageEventArgs> ChatMessageReceived;
    public event EventHandler<CommandResponseEventArgs> CommandResponseReceived;
    public event EventHandler<RconErrorEventArgs> ErrorOccurred;

    // Properties
    public string Host { get; }
    public int Port { get; }
    public string Password { get; }
    public bool IsAuthorized { get; private set; }
    public DateTimeOffset ConnectedOn { get; private set; }

    public RconClient(string host, int port, string password, IPacketProcessor packetProcessor = null)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Port = port;
        Password = password ?? throw new ArgumentNullException(nameof(password));

        _connectionManager = new RconConnectionManager();
        _commandQueue = Channel.CreateBounded<Packet>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        _packetProcessor = packetProcessor ?? new RconPacketProcessor(
            OnAuthenticationResult,
            OnChatMessageReceived,
            OnCommandResponseReceived,
            OnErrorOccurred);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _stream = await _connectionManager.ConnectAsync(Host, Port, cancellationToken);
            ConnectedOn = DateTimeOffset.Now;

            // Start background tasks
            _ = Task.Run(() => ReceiveDataAsync(cancellationToken), cancellationToken);
            _ = Task.Run(() => ProcessCommandQueueAsync(cancellationToken), cancellationToken);

            await AuthenticateAsync(Password);
        }
        catch (Exception ex)
        {
            OnErrorOccurred($"Connection Error: {ex.Message}", ex);
            throw;
        }
    }

    private async ValueTask ProcessCommandQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var command in _commandQueue.Reader.ReadAllAsync(cancellationToken))
            {
                await WriteCommandToStreamAsync(command, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            OnErrorOccurred($"Command queue processing error: {ex.Message}", ex);
        }
    }

    private async ValueTask WriteCommandToStreamAsync(Packet command, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(command.Body);
        var size = payload.Length + 14;
        var buffer = new byte[size];

        // Build packet
        BitConverter.GetBytes(size - 4).CopyTo(buffer, 0);
        BitConverter.GetBytes(command.Id).CopyTo(buffer, 4); // Fixed: was missing ID
        BitConverter.GetBytes(command.Type).CopyTo(buffer, 8);
        payload.CopyTo(buffer, 12);
        BitConverter.GetBytes((short)0).CopyTo(buffer, size - 2);

        var streamLock = _connectionManager.GetStreamLock();
        await streamLock.WaitAsync(cancellationToken);
        try
        {
            await _stream.WriteAsync(buffer, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            streamLock.Release();
        }
    }

    private async Task ReceiveDataAsync(CancellationToken cancellationToken)
    {
        while (_connectionManager.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            if (!_stream.DataAvailable)
            {
                await Task.Delay(10, cancellationToken); // Better than Task.Yield for network operations
                continue;
            }

            try
            {
                int size = await ReadInt32Async(cancellationToken);
                if (size > 0)
                {
                    var dataBuffer = new byte[size];
                    int bytesRead = await _stream.ReadAsync(dataBuffer.AsMemory(0, size), cancellationToken);
                    if (bytesRead > 0)
                    {
                        var packet = new ResponsePacket(dataBuffer);
                        await _packetProcessor.ProcessPacketAsync(packet);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException ex)
            {
                OnErrorOccurred($"Receive Data Error: {ex.Message}", ex);
                break;
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Unexpected error during data reception: {ex.Message}", ex);
                break;
            }
        }
    }

    private async ValueTask<int> ReadInt32Async(CancellationToken cancellationToken)
    {
        int bytesRead = await _stream.ReadAsync(_readBuffer, cancellationToken);
        return bytesRead == 4 ? BitConverter.ToInt32(_readBuffer, 0) : 0;
    }

    public async ValueTask AuthenticateAsync(string password)
    {
        var authPacket = new Packet(RconConstants.SERVERDATA_AUTH, -1, password);
        await _commandQueue.Writer.WriteAsync(authPacket);
    }

    public async ValueTask QueueCommandAsync(Packet packet)
    {
        await _commandQueue.Writer.WriteAsync(packet);
        await _commandQueue.Writer.WriteAsync(new Packet(RconConstants.SERVERDATA_EXECCOMMAND, 99, ""));
    }

    // Event trigger methods - made private as they're implementation details
    private void OnAuthenticationResult(bool isSuccess)
    {
        if (isSuccess)
        {
            IsAuthorized = true;
        }
        AuthenticationResult?.Invoke(this, new AuthenticationEventArgs(isSuccess));
    }

    private void OnChatMessageReceived(string message)
    {
        ChatMessageReceived?.Invoke(this, new ChatMessageEventArgs(message));
    }

    private void OnCommandResponseReceived(string response)
    {
        CommandResponseReceived?.Invoke(this, new CommandResponseEventArgs(response));
    }

    private void OnErrorOccurred(string errorMessage, Exception exception = null)
    {
        ErrorOccurred?.Invoke(this, new RconErrorEventArgs(errorMessage, exception));
    }

    public async ValueTask DisposeAsync()
    {
        _commandQueue.Writer.Complete();
        await _connectionManager.DisposeAsync();
    }
}