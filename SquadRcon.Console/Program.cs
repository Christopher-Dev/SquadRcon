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
        string host = "198.133.237.27"; // Replace with your host
        int port = 10272;          // Replace with your port
        string password = "1234567890"; // Replace with your password

        var rconInstance = new RconClient(host, port, password);

        CancellationTokenSource cts = new CancellationTokenSource();

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await rconInstance.ConnectAsync(cts.Token);

        while (!cts.Token.IsCancellationRequested)
        {
            Console.WriteLine("Enter command (or 'exit' to close): ");
            var command = Console.ReadLine();
            if (command.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                cts.Cancel();
                break;
            }

            // Send the command to the Rcon server
            await rconInstance.QueueCommandAsync(new Packet(RconConstants.SERVERDATA_EXECCOMMAND, 99, command));
        }

        Console.WriteLine("Disconnecting...");
        // Perform any cleanup or disconnection logic here
    }
}

// Add the rest of your RconInstance class and related classes here
