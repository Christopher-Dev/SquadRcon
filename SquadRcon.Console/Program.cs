using SquadRcon;
using SquadRcon.Classes;
using SquadRcon.Classes.Constants;
using System;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static Timer _keepaliveTimer;
    private static RconClient _rconInstance;
    private static bool _isKeepaliveCommand = false;

    static async Task Main(string[] args)
    {
        // Display banner
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                                                               ║");
        Console.WriteLine("║   ███████╗ ██████╗ ██╗   ██╗ █████╗ ██████╗                 ║");
        Console.WriteLine("║   ██╔════╝██╔═══██╗██║   ██║██╔══██╗██╔══██╗                ║");
        Console.WriteLine("║   ███████╗██║   ██║██║   ██║███████║██║  ██║                ║");
        Console.WriteLine("║   ╚════██║██║▄▄ ██║██║   ██║██╔══██║██║  ██║                ║");
        Console.WriteLine("║   ███████║╚██████╔╝╚██████╔╝██║  ██║██████╔╝                ║");
        Console.WriteLine("║   ╚══════╝ ╚══▀▀═╝  ╚═════╝ ╚═╝  ╚═╝╚═════╝                 ║");
        Console.WriteLine("║                                                               ║");
        Console.WriteLine("║              RCON Server Manager Tool                        ║");
        Console.WriteLine("║                  Created by QG Edwin                         ║");
        Console.WriteLine("║                                                               ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        Console.Write("Server IP: ");
        string host = Console.ReadLine();
        Console.Write("Port: ");
        int port = 0;
        while (!int.TryParse(Console.ReadLine(), out port))
        {
            Console.Write("Port: ");
        }
        Console.Write("Password: ");
        string password = Console.ReadLine();

        _rconInstance = new RconClient(host, port, password);

        // Subscribe to events
        _rconInstance.AuthenticationResult += OnAuthenticationResult;
        _rconInstance.ChatMessageReceived += OnChatMessageReceived;
        _rconInstance.CommandResponseReceived += OnCommandResponseReceived;
        _rconInstance.ErrorOccurred += OnErrorOccurred;

        CancellationTokenSource cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await _rconInstance.ConnectAsync(cts.Token);

            // Wait a moment for authentication before proceeding
            await Task.Delay(1000, cts.Token);

            if (!_rconInstance.IsAuthorized)
            {
                Console.WriteLine("Authentication failed. Exiting...");
                return;
            }

            Console.WriteLine("\nLightweight SquadRcon Server Manager Tool\n");

            // Start the keepalive timer (30 seconds interval)
            _keepaliveTimer = new Timer(SendKeepalive, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            while (!cts.Token.IsCancellationRequested)
            {
                Console.Write("> ");
                var command = Console.ReadLine();

                if (command.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    cts.Cancel();
                    break;
                }

                if (string.IsNullOrWhiteSpace(command))
                {
                    continue;
                }

                // Send the command to the Rcon server
                await _rconInstance.QueueCommandAsync(new Packet(RconConstants.SERVERDATA_EXECCOMMAND, 99, command));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected Error: {ex.Message}");
        }
        finally
        {
            // Stop the keepalive timer
            _keepaliveTimer?.Dispose();

            await _rconInstance.DisposeAsync();
        }
    }

    private static async void SendKeepalive(object state)
    {
        try
        {
            if (_rconInstance != null && _rconInstance.IsAuthorized)
            {
                _isKeepaliveCommand = true;
                // Send an empty command as keepalive
                await _rconInstance.QueueCommandAsync(new Packet(RconConstants.SERVERDATA_EXECCOMMAND, 999, ""));
            }
        }
        catch (Exception ex)
        {
            // Silently handle keepalive errors to avoid cluttering the console
            // You could log this to a file if needed for debugging
        }
    }

    // Event handlers
    private static void OnAuthenticationResult(object sender, AuthenticationEventArgs e)
    {
        // Silent authentication - no console output
    }

    private static void OnChatMessageReceived(object sender, ChatMessageEventArgs e)
    {
        Console.WriteLine($"[CHAT] {e.Message}");
    }

    private static void OnCommandResponseReceived(object sender, CommandResponseEventArgs e)
    {
        // Skip displaying response if it's from a keepalive command
        if (_isKeepaliveCommand)
        {
            _isKeepaliveCommand = false;
            return;
        }

        if (!string.IsNullOrWhiteSpace(e.Response))
        {
            Console.WriteLine(e.Response);
        }
    }

    private static void OnErrorOccurred(object sender, RconErrorEventArgs e)
    {
        Console.WriteLine($"[ERROR] {e.ErrorMessage}");
        if (e.Exception != null)
        {
            Console.WriteLine($"[EXCEPTION] {e.Exception.GetType().Name}: {e.Exception.Message}");
        }
    }
}