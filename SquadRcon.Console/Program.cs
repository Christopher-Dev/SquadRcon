using SquadRcon;
using SquadRcon.Classes;
using SquadRcon.Classes.Constants;
using System;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Squad Rcon Client - Enter Connection Details:");

        Console.Write("Enter the server IP address: ");
        string host = Console.ReadLine();

        Console.Write("Enter the server port: ");
        int port = 0;
        while (!int.TryParse(Console.ReadLine(), out port))
        {
            Console.WriteLine("Invalid port. Please enter a valid number:");
        }

        Console.Write("Enter the Rcon password: ");
        string password = Console.ReadLine();

        var rconInstance = new RconClient(host, port, password);

        CancellationTokenSource cts = new CancellationTokenSource();

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await rconInstance.ConnectAsync(cts.Token);
            Console.WriteLine($"Connected to {host}:{port}");

            while (!cts.Token.IsCancellationRequested)
            {
                Console.Write("Enter command (or 'exit' to close): ");
                var command = Console.ReadLine();
                if (command.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    cts.Cancel();
                    break;
                }

                // Send the command to the Rcon server
                await rconInstance.QueueCommandAsync(new Packet(RconConstants.SERVERDATA_EXECCOMMAND, 99, command));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            Console.WriteLine("Disconnecting...");
            // Perform any cleanup or disconnection logic here
        }
    }
}
