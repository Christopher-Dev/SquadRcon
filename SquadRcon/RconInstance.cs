using SquadRcon.Classes;
using SquadRcon.Classes.Constants;
using SquadRcon.Classes.Packets;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace SquadRcon
{
    public class RconInstance : IDisposable
    {
        private TcpClient tcpClient;
        private NetworkStream stream;
        private readonly MemoryStream receivedDataBuffer = new();
        private readonly Channel<Packet> commandQueue = Channel.CreateUnbounded<Packet>();
        private readonly List<string> accumulatedBuffer = new();
        private readonly byte[] buffer = new byte[4];
        private readonly SemaphoreSlim streamLock = new(1, 1);  // Prevents concurrent access to stream

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
                await tcpClient.ConnectAsync(Host, Port, token);
                stream = tcpClient.GetStream();
                ConnectedOn = DateTimeOffset.Now;

                // Start the data reception and command processing tasks
                _ = Task.Run(() => ReceiveDataAsync(token), token);
                _ = Task.Run(() => ProcessCommandsAsync(token), token);

                await AuthorizeAsync(Password);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection Error: {ex.Message}");
            }
        }

        private async Task ProcessCommandsAsync(CancellationToken cancellationToken)
        {
            await foreach (var command in commandQueue.Reader.ReadAllAsync(cancellationToken))
            {
                await ProcessCommandAsync(command, cancellationToken);
            }
        }

        private async Task ProcessCommandAsync(Packet command, CancellationToken cancellationToken)
        {
            var payload = Encoding.UTF8.GetBytes(command.Body);
            var size = payload.Length + 14;
            var buffer = new byte[size];

            BitConverter.GetBytes(size - 4).CopyTo(buffer, 0);
            BitConverter.GetBytes(command.Type).CopyTo(buffer, 8);
            payload.CopyTo(buffer, 12);
            BitConverter.GetBytes((short)0).CopyTo(buffer, size - 2);

            await streamLock.WaitAsync(cancellationToken);  // Prevent concurrent writes
            try
            {
                await stream.WriteAsync(buffer.AsMemory(0, size), cancellationToken);
                await stream.FlushAsync(cancellationToken);
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
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                try
                {
                    int size = await ReadInt32Async(cancellationToken);
                    if (size > 0)
                    {
                        var dataBuffer = new byte[size];
                        int bytesRead = await stream.ReadAsync(dataBuffer.AsMemory(0, size), cancellationToken);
                        if (bytesRead > 0) await ParseResponseAsync(dataBuffer);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException)
                {
                    Console.WriteLine($"Receive Data Error: {ex.Message}");
                    break;
                }
            }
        }

        private async Task<int> ReadInt32Async(CancellationToken cancellationToken)
        {
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 4), cancellationToken);
            return bytesRead == 4 ? BitConverter.ToInt32(buffer, 0) : 0;
        }

        private async Task ParseResponseAsync(byte[] buffer)
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
                    accumulatedBuffer.AddRange(packet.Body);
                    break;
                case RconConstants.CommandComplete:
                    await PrintBytesAsAsciiAsync();
                    break;
                default:
                    Console.WriteLine("Error Parsing Response");
                    break;
            }
        }

        public async Task AuthorizeAsync(string password)
        {
            var authPacket = new Packet(RconConstants.SERVERDATA_AUTH, -1, password);
            await commandQueue.Writer.WriteAsync(authPacket);
        }

        public async Task SendCommandAsync(Packet packet)
        {
            await commandQueue.Writer.WriteAsync(packet);
            await commandQueue.Writer.WriteAsync(new Packet(RconConstants.SERVERDATA_EXECCOMMAND, 99, ""));
        }

        public async Task PrintBytesAsAsciiAsync()
        {
            accumulatedBuffer.ForEach(Console.WriteLine);
            accumulatedBuffer.Clear();
        }

        public void Dispose()
        {
            stream?.Dispose();
            tcpClient?.Dispose();
            streamLock.Dispose();
        }
    }

}
