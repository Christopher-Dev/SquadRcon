using SquadRcon.Classes;
using SquadRcon.Classes.Constants;
using SquadRcon.Classes.Packets;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace SquadRcon
{
    // Event argument classes for different response types
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
        public System.Exception Exception { get; }
        public RconErrorEventArgs(string errorMessage, System.Exception exception = null)
        {
            ErrorMessage = errorMessage;
            Exception = exception;
        }
    }

    public class RconClient : IAsyncDisposable
    {
        private TcpClient tcpClient;
        private NetworkStream stream;
        private readonly Channel<Packet> commandQueue = Channel.CreateBounded<Packet>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        private readonly StringBuilder responseBuilder = new();
        private readonly byte[] readBuffer = new byte[4];
        private readonly SemaphoreSlim streamLock = new(1, 1);

        // Events
        public event EventHandler<AuthenticationEventArgs> AuthenticationResult;
        public event EventHandler<ChatMessageEventArgs> ChatMessageReceived;
        public event EventHandler<CommandResponseEventArgs> CommandResponseReceived;
        public event EventHandler<RconErrorEventArgs> ErrorOccurred;

        public string Host { get; }
        public int Port { get; }
        public string Password { get; }
        public bool IsAuthorized { get; private set; } = false;
        public DateTimeOffset ConnectedOn { get; private set; }

        public RconClient(string host, int port, string password)
        {
            Host = host;
            Port = port;
            Password = password;
        }

        public async Task ConnectAsync(CancellationToken token)
        {
            try
            {
                tcpClient = new TcpClient();
                tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                await tcpClient.ConnectAsync(Host, Port, token).ConfigureAwait(false);
                stream = tcpClient.GetStream();
                ConnectedOn = DateTimeOffset.Now;

                _ = Task.Run(() => ReceiveDataAsync(token), token);
                _ = Task.Run(() => ProcessCommandQueueAsync(token), token);

                await AuthenticateAsync(Password).ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                OnErrorOccurred($"Connection Error: {ex.Message}", ex);
            }
        }

        private async ValueTask ProcessCommandQueueAsync(CancellationToken cancellationToken)
        {
            await foreach (var command in commandQueue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await WriteCommandToStreamAsync(command, cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask WriteCommandToStreamAsync(Packet command, CancellationToken cancellationToken)
        {
            var payload = Encoding.UTF8.GetBytes(command.Body);
            var size = payload.Length + 14;
            var buffer = new byte[size];

            BitConverter.GetBytes(size - 4).CopyTo(buffer, 0);
            BitConverter.GetBytes(command.Type).CopyTo(buffer, 8);
            payload.CopyTo(buffer, 12);
            BitConverter.GetBytes((short)0).CopyTo(buffer, size - 2);

            await streamLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                streamLock.Release();
            }
        }

        private async Task ReceiveDataAsync(CancellationToken cancellationToken)
        {
            while (tcpClient.Connected && !cancellationToken.IsCancellationRequested)
            {
                if (!stream.DataAvailable)
                {
                    await Task.Yield();
                    continue;
                }

                try
                {
                    int size = await ReadInt32Async(cancellationToken).ConfigureAwait(false);
                    if (size > 0)
                    {
                        var dataBuffer = new byte[size];
                        int bytesRead = await stream.ReadAsync(dataBuffer.AsMemory(0, size), cancellationToken).ConfigureAwait(false);
                        if (bytesRead > 0) await ParseResponseAsync(dataBuffer).ConfigureAwait(false);
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
            }
        }

        private async ValueTask<int> ReadInt32Async(CancellationToken cancellationToken)
        {
            int bytesRead = await stream.ReadAsync(readBuffer, cancellationToken).ConfigureAwait(false);
            return bytesRead == 4 ? BitConverter.ToInt32(readBuffer, 0) : 0;
        }

        private async ValueTask ParseResponseAsync(byte[] buffer)
        {
            var packet = new ResponsePacket(buffer);
            switch (packet.Type)
            {
                case RconConstants.AuthSuccess:
                    IsAuthorized = true;
                    OnAuthenticationResult(true);
                    break;
                case RconConstants.SERVERDATA_CHAT_VALUE:
                    var chatMessage = packet.Body.FirstOrDefault();
                    if (!string.IsNullOrEmpty(chatMessage))
                    {
                        OnChatMessageReceived(chatMessage);
                    }
                    break;
                case RconConstants.EmptyPacket:
                    break;
                case RconConstants.SERVERDATA_RESPONSE_VALUE:
                    responseBuilder.AppendJoin(Environment.NewLine, packet.Body);
                    break;
                case RconConstants.CommandComplete:
                    await ProcessCompleteResponseAsync().ConfigureAwait(false);
                    break;
                default:
                    OnErrorOccurred("Error Parsing Response - Unknown packet type received");
                    break;
            }
        }

        public async ValueTask AuthenticateAsync(string password)
        {
            var authPacket = new Packet(RconConstants.SERVERDATA_AUTH, -1, password);
            await commandQueue.Writer.WriteAsync(authPacket).ConfigureAwait(false);
        }

        public async ValueTask QueueCommandAsync(Packet packet)
        {
            await commandQueue.Writer.WriteAsync(packet).ConfigureAwait(false);
            await commandQueue.Writer.WriteAsync(new Packet(RconConstants.SERVERDATA_EXECCOMMAND, 99, "")).ConfigureAwait(false);
        }

        private async ValueTask ProcessCompleteResponseAsync()
        {
            var response = responseBuilder.ToString();
            OnCommandResponseReceived(response);
            responseBuilder.Clear();
        }

        // Event trigger methods
        protected virtual void OnAuthenticationResult(bool isSuccess)
        {
            AuthenticationResult?.Invoke(this, new AuthenticationEventArgs(isSuccess));
        }

        protected virtual void OnChatMessageReceived(string message)
        {
            ChatMessageReceived?.Invoke(this, new ChatMessageEventArgs(message));
        }

        protected virtual void OnCommandResponseReceived(string response)
        {
            CommandResponseReceived?.Invoke(this, new CommandResponseEventArgs(response));
        }

        protected virtual void OnErrorOccurred(string errorMessage, System.Exception exception = null)
        {
            ErrorOccurred?.Invoke(this, new RconErrorEventArgs(errorMessage, exception));
        }

        public async ValueTask DisposeAsync()
        {
            if (stream != null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            if (tcpClient != null)
            {
                tcpClient.Dispose();
            }
            streamLock.Dispose();
        }
    }
}