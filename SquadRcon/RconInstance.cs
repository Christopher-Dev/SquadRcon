using SquadRcon.Classes;
using SquadRcon.Classes.Constants;
using SquadRcon.Classes.Packets;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace SquadRcon
{
    public class RconInstance
    {
        private TcpClient tcpClient;

        private NetworkStream stream;

        private readonly byte[] buffer;

        private readonly ConcurrentQueue<Packet> commandQueue;

        private BinaryReader binaryReader;

        #region ServerInfo

        public string Host { get; private set; }

        public int Port { get; private set; }

        public string Password { get; private set; }

        #endregion

        private List<string> accumulatedBuffer;

        public bool IsAuthorized { get; set; } = false;

        private bool Connected;

        private bool sendingCommand;


        private CancellationTokenSource cancellationTokenSource;

        public DateTimeOffset ConnectedOn { get; set; }

        public DateTimeOffset KeepAliveuntil { get; set; }

        public RconInstance(string host, int port, string password)
        {
            this.Host = host;
            this.Port = port;
            this.Password = password;

            Connected = false;

            cancellationTokenSource = new CancellationTokenSource();

            accumulatedBuffer = new List<string>();

            commandQueue = new ConcurrentQueue<Packet>();

            buffer = new byte[4096];


        }

        public async Task StartAsync(CancellationToken token)
        {
            try
            {
                
                try
                {
                    tcpClient = new TcpClient();
                    await tcpClient.ConnectAsync(Host, Port);
                    stream = tcpClient.GetStream();
                    binaryReader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

                    await Task.Delay(100);

                    BeginTasks(cancellationToken: cancellationTokenSource.Token);

                    await Authorize(Password);

                    await ServerPoll();
                }
                catch(Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private async Task BeginTasks(CancellationToken cancellationToken)
        {

            try
            {
                var receiveDataTask = ReceiveDataAsync(cancellationToken);

                var commandProcessingTask = CommandProcessingTask(cancellationToken);

                //await Task.WhenAll(receiveDataTask, commandProcessingTask);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while receiving data: {ex.Message}");
            }

        }

        public async Task CommandProcessingTask(CancellationToken cancellationToken)
        {
            var serverPollScheduler = new ServerPollScheduler(TimeSpan.FromSeconds(5));
            serverPollScheduler.Start();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await PollAndProcessCommands(cancellationToken);

                    if (serverPollScheduler.IsTimeToPoll)
                    {
                        await ServerPoll();
                        serverPollScheduler.Reset();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                HandleCancellation();
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
            finally
            {
                serverPollScheduler.Stop();
            }
        }

        private async Task PollAndProcessCommands(CancellationToken cancellationToken)
        {
            if (commandQueue.TryDequeue(out var command))
            {
                await ProcessCommandAsync(command);
            }
            else
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        private void HandleCancellation()
        {
            // Cancellation specific logic
        }

        private void HandleException(Exception ex)
        {
            Console.WriteLine($"An error occurred in command processing: {ex.Message}");
        }

        public async Task ServerPoll()
        {
            await ShowServerInfo();
        }

        public async Task ShowServerInfo()
        {
            var authPacket = new Packet(RconConstants.SERVERDATA_EXECCOMMAND, 89, "ShowServerInfo");
            commandQueue.Enqueue(authPacket);
        }
        private async Task ProcessCommandAsync(Packet command)
        {
            var payload = Encoding.UTF8.GetBytes(command.Body);
            var size = payload.Length + 14;
            var buffer = new byte[size];

            BitConverter.GetBytes(size - 4).CopyTo(buffer, 0);
            BitConverter.GetBytes(command.Type).CopyTo(buffer, 8);
            payload.CopyTo(buffer, 12);
            BitConverter.GetBytes((short)0).CopyTo(buffer, size - 2);

            // Convert buffer to hex string
            string hex = BitConverter.ToString(buffer).Replace("-", "");

            //// Write hex string to console
            //Console.WriteLine($"Sending packet: {hex}");

            await stream.WriteAsync(buffer, 0, size);
            await stream.FlushAsync();
        }



        public async Task ReceiveDataAsync(CancellationToken cancellationToken)
        {
            while (tcpClient.Connected)
            {
                try
                {
                    // Throw if cancellation is requested
                    cancellationToken.ThrowIfCancellationRequested();

                    if (stream.DataAvailable)
                    {
                        // Read the size of the packet
                        int size = binaryReader.ReadInt32();
                        if (size > 0 && size <= 4096) // Size validation
                        {
                            // Create a buffer for the rest of the packet
                            var buffer = new byte[size];

                            // Read the rest of the packet into the buffer
                            int bytesRead = binaryReader.Read(buffer, 0, buffer.Length);
                            if (bytesRead == 0)
                            {
                                break;
                            }


                            await ParseResponse(buffer);
                        }
                    }
                    else
                    {
                        // Use cancellationToken in Task.Delay
                        await Task.Delay(100, cancellationToken); // Wait for data
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Data reception canceled.");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    break;
                }
            }
        }
        public async Task ClearBuffer()
        {
            accumulatedBuffer.Clear();
        }

        public async Task PrintBytesAsAscii()
        {
            foreach (var item in accumulatedBuffer)
            {

                Console.WriteLine(item);

            }

            await ClearBuffer();
        }


        public async Task ParseResponse(byte[] buffer)
        {

            var packet = new ResponsePacket(buffer);


            switch (packet.Type)
            {
                case RconConstants.AuthSuccess:
                    Console.WriteLine($"Authentication Success Response");
                    break;
                case RconConstants.SERVERDATA_CHAT_VALUE:
                    Console.WriteLine($"{packet.Body.FirstOrDefault()}");
                    break;
                //Do nothing with empty packets
                case RconConstants.EmptyPacket:
                    break;
                case RconConstants.SERVERDATA_RESPONSE_VALUE:
                    accumulatedBuffer.AddRange(packet.Body);
                    break;
                case RconConstants.CommandComplete:
                    PrintBytesAsAscii();
                    break;
                default:
                    Console.WriteLine($"Error Parsing Response");
                    break;
            }

        }



        public async Task Authorize(string password)
        {
            var authPacket = new Packet(RconConstants.SERVERDATA_AUTH, -1, password);
            commandQueue.Enqueue(authPacket);
            IsAuthorized = true;
        }

        public async Task SendCommandAsync(Packet packet)
        {
            sendingCommand = true;
            commandQueue.Enqueue(packet);
            commandQueue.Enqueue(new Packet(RconConstants.SERVERDATA_EXECCOMMAND, 99, ""));
            sendingCommand = false;
        }
    }
}
