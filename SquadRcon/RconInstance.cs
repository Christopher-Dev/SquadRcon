using SquadRcon.Classes;
using SquadRcon.Classes.Constants;
using SquadRcon.Classes.Packets;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace SquadRcon
{
    public class RconInstance : IAsyncDisposable
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

        public string Host { get; }
        public int Port { get; }
        public string Password { get; }
        public bool IsAuthorized { get; private set; } = false;
        public DateTimeOffset ConnectedOn { get; private set; }

        public RconInstance(string host, int port, string password)
        {
            Host = host;
            Port = port;
            Password = password;
        }

        public async Task StartAsync(CancellationToken token)
        {
            try
            {
                tcpClient = new TcpClient();
                tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                await tcpClient.ConnectAsync(Host, Port, token).ConfigureAwait(false);
                stream = tcpClient.GetStream();
                ConnectedOn = DateTimeOffset.Now;

                _ = Task.Run(() => ReceiveDataAsync(token), token);
                _ = Task.Run(() => ProcessCommandsAsync(token), token);

                await AuthorizeAsync(Password).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection Error: {ex.Message}");
            }
        }

        private async ValueTask ProcessCommandsAsync(CancellationToken cancellationToken)
        {
            await foreach (var command in commandQueue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await SendCommandToStreamAsync(command, cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask SendCommandToStreamAsync(Packet command, CancellationToken cancellationToken)
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
                    Console.WriteLine($"Receive Data Error: {ex.Message}");
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
                    Console.WriteLine("Authentication Success");
                    IsAuthorized = true;
                    break;
                case RconConstants.SERVERDATA_CHAT_VALUE:
                    Console.WriteLine(packet.Body.FirstOrDefault());
                    break;
                case RconConstants.EmptyPacket:
                    break;
                case RconConstants.SERVERDATA_RESPONSE_VALUE:
                    responseBuilder.AppendJoin(Environment.NewLine, packet.Body);
                    break;
                case RconConstants.CommandComplete:
                    await PrintResponseAsync().ConfigureAwait(false);
                    break;
                default:
                    Console.WriteLine("Error Parsing Response");
                    break;
            }
        }

        public async ValueTask AuthorizeAsync(string password)
        {
            var authPacket = new Packet(RconConstants.SERVERDATA_AUTH, -1, password);
            await commandQueue.Writer.WriteAsync(authPacket).ConfigureAwait(false);
        }

        public async ValueTask SendCommandAsync(Packet packet)
        {
            await commandQueue.Writer.WriteAsync(packet).ConfigureAwait(false);
            await commandQueue.Writer.WriteAsync(new Packet(RconConstants.SERVERDATA_EXECCOMMAND, 99, "")).ConfigureAwait(false);
        }

        private async ValueTask PrintResponseAsync()
        {
            Console.WriteLine(responseBuilder.ToString());
            responseBuilder.Clear();
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
