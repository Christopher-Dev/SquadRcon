using SquadRcon;
using SquadRcon.Constants;

class Program
{
    private static Timer _keepaliveTimer;
    private static RconClient _rconInstance;
    private static bool _isKeepaliveCommand = false;
    private static bool _developerMode = false;

    // Change this to whichever RCON command your server uses for status
    private const string SERVER_INFO_COMMAND = "ServerInfo";

    static async Task Main(string[] args)
    {
        PrintBanner();

        Console.Write("Server IP : ");
        string host = Console.ReadLine();

        Console.Write("Port      : ");
        int port;
        while (!int.TryParse(Console.ReadLine(), out port))
            Console.Write("Port      : ");

        Console.Write("Password  : ");
        string password = Console.ReadLine();

        _rconInstance = new RconClient(host, port, password);

        // Hook up RCON events
        _rconInstance.AuthenticationResult += OnAuthenticationResult;
        _rconInstance.ChatMessageReceived += OnChatMessageReceived;
        _rconInstance.CommandResponseReceived += OnCommandResponseReceived;
        _rconInstance.ErrorOccurred += OnErrorOccurred;

        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            await _rconInstance.ConnectAsync(cts.Token);
            await Task.Delay(1_000, cts.Token);           // handshake time

            if (!_rconInstance.IsAuthorized)
            {
                Console.WriteLine("Authentication failed. Exiting…");
                return;
            }

            ShowMenu();

            // 30-second keep-alive
            _keepaliveTimer = new Timer(
                SendKeepalive,
                null,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30));

            // ───────── Main REPL loop ─────────
            while (!cts.Token.IsCancellationRequested)
            {
                Console.Write("> ");
                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;

                string command = input.Trim();

                if (command.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    cts.Cancel();
                    break;
                }

                if (command.Equals("clear", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Clear();
                    PrintBanner();
                    ShowMenu();
                    continue;
                }

                if (command.Equals("list", StringComparison.OrdinalIgnoreCase))
                {
                    PrintHelpMenu();   // detailed list
                    continue;
                }


                if (command.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    ShowMenu();
                    continue;
                }

                if (command.Equals("info", StringComparison.OrdinalIgnoreCase))
                {
                    await _rconInstance.QueueCommandAsync(
                        new Packet(RconConstants.SERVERDATA_EXECCOMMAND, 201, SERVER_INFO_COMMAND));
                    continue;
                }

                if (command.Equals("enterdevelopermode", StringComparison.OrdinalIgnoreCase))
                {
                    _developerMode = true;
                    Console.WriteLine("[DEV] Developer mode enabled. All logs will now be displayed.");
                    continue;
                }

                // Forward any other text as a raw RCON command
                await _rconInstance.QueueCommandAsync(
                    new Packet(RconConstants.SERVERDATA_EXECCOMMAND, 101, command));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error: {ex.Message}");
        }
        finally
        {
            _keepaliveTimer?.Dispose();
            await _rconInstance.DisposeAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Helper: ASCII-art banner
    // ─────────────────────────────────────────────────────────────
    private static void PrintBanner()
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                                                               ║");
        Console.WriteLine("║   ███████╗ ██████╗ ██╗   ██╗ █████╗ ██████╗                   ║");
        Console.WriteLine("║   ██╔════╝██╔═══██╗██║   ██║██╔══██╗██╔══██╗                  ║");
        Console.WriteLine("║   ███████╗██║   ██║██║   ██║███████║██║  ██║                  ║");
        Console.WriteLine("║   ╚════██║██║▄▄ ██║██║   ██║██╔══██║██║  ██║                  ║");
        Console.WriteLine("║   ███████║╚██████╔╝╚██████╔╝██║  ██║██████╔╝                  ║");
        Console.WriteLine("║   ╚══════╝ ╚══▀▀═╝  ╚═════╝ ╚═╝  ╚═╝╚═════╝                   ║");
        Console.WriteLine("║                                                               ║");
        Console.WriteLine("║          A Lightweight Server Configuration Tool              ║");
        Console.WriteLine("║                  Created by QG Edwin                          ║");
        Console.WriteLine("║                                                               ║");
        Console.WriteLine("║                                                               ║");
        Console.WriteLine("║                                                               ║");
        Console.WriteLine("║                    !!!Known Issue!!!                          ║");
        Console.WriteLine("║       If you Dont See the < after sending a command,          ║");
        Console.WriteLine("║        Press Enter again and it will self resolve.            ║");
        Console.WriteLine("║                                                               ║");
        Console.WriteLine("║           I'll get to fixing that at some point.              ║");
        Console.WriteLine("║                                                               ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────────
    //  Helper: Pretty text menu
    // ─────────────────────────────────────────────────────────────
    private static void ShowMenu()
    {
        Console.WriteLine("╔═══════════════════ MENU ═══════════════════╗");
        Console.WriteLine("║  help               Show Tool Commands     ║");
        Console.WriteLine("║  list               List Server Commands   ║");
        Console.WriteLine("║  info               Get server status      ║");
        Console.WriteLine("║  clear              Clear the screen       ║");
        Console.WriteLine("║  enterdevelopermode Show full dev logs     ║");
        Console.WriteLine("║  exit               Quit the tool          ║");
        Console.WriteLine("╚════════════════════════════════════════════╝\n");
    }

    // ─────────────────────────────────────────────────────────────
    //  Keep-alive packet
    // ─────────────────────────────────────────────────────────────
    private static async void SendKeepalive(object? _)
    {
        if (_rconInstance?.IsAuthorized != true) return;

        try
        {
            _isKeepaliveCommand = true;
            await _rconInstance.QueueCommandAsync(
                new Packet(RconConstants.SERVERDATA_EXECCOMMAND, 999, string.Empty));

            if (_developerMode)                                  //  ← NEW
                Console.WriteLine("[DEV] Keep-alive sent.");      //  ← NEW
        }
        catch
        {
            /* swallow */
        }
    }


    // ─────────────────────────────────────────────────────────────
    //  Event handlers
    // ─────────────────────────────────────────────────────────────
    private static void OnAuthenticationResult(object? _, AuthenticationEventArgs e)
    {
        if (_developerMode)
            Console.WriteLine("[DEV] Authentication event received.");
    }

    private static void OnChatMessageReceived(object? _, ChatMessageEventArgs e) =>
        Console.WriteLine($"[CHAT] {e.Message}");

    /// <summary>
    /// Pretty console help for all server-side commands (admin + public).
    /// </summary>
    private static void PrintHelpMenu()
    {
        Console.WriteLine("\n══════════════════════════ ADMIN COMMANDS ══════════════════════════");
        Console.WriteLine("{0,-28} {1}", "Command", "Description");
        Console.WriteLine(new string('─', 74));

        void row(string cmd, string desc) =>
            Console.WriteLine("{0,-28} {1}", cmd, desc);

        // ► Kick / Ban
        row("AdminKick", "Kick player by name or Steam ID");
        row("AdminKickById", "Kick player by slot ID");
        row("AdminBan", "Ban player (0=perm, 1d, 1M …)");
        row("AdminBanById", "Ban player by slot ID");

        // ► Broadcast / Chat
        row("AdminBroadcast", "System message to everyone");
        row("ChatToAdmin", "Message only online admins");

        // ► Match flow
        row("AdminEndMatch", "End the match immediately");
        row("AdminPauseMatch", "Pause the match");
        row("AdminUnpauseMatch", "Resume paused match");
        row("AdminRestartMatch", "Restart current layer");

        // ► Map / Layer management
        row("AdminChangeLayer", "Instantly change to layer");
        row("AdminSetNextLayer", "Set next layer in rotation");

        // ► Server config
        row("AdminSetMaxNumPlayers", "Set max server slots");
        row("AdminSetServerPassword", "Set or clear server password");

        // ► Force actions
        row("AdminForceTeamChange", "Move player to other team");
        row("AdminForceTeamChangeById", "Move player by slot ID");

        // ► Squad management
        row("AdminDisbandSquad", "Disband squad (team, index)");
        row("AdminRemovePlayerFromSquad", "Kick player from squad");
        row("AdminRemovePlayerFromSquadById", "Kick by slot ID");

        // ► Commander / Warnings
        row("AdminDemoteCommander", "Demote commander by name/ID");
        row("AdminDemoteCommanderById", "Demote commander by slot ID");
        row("AdminWarn", "Warn player");
        row("AdminWarnById", "Warn by slot ID");

        // ► Info
        row("AdminListDisconnectedPlayers", "Show recently disconnected");

        // ► Misc / Cheats / Debug  (only on licensed test/local servers)
        row("AdminSlomo", "Set server time-dilation");
        row("AdminDisableVehicleClaiming", "Toggle claiming on layer");
        row("AdminForceAllRoleAvailability", "Ignore kit restrictions");
        row("AdminNoRespawnTimer", "Disable respawn timer");
        row("AdminNoTeamChangeTimer", "Disable team-swap timer");
        row("AdminForceAllDeployableAvailability", "Ignore deployable rules");
        row("AdminNetTestStart|Stop", "Start/stop network test");
        row("AdminProfileServer", "CPU profile X seconds");
        row("TraceViewToggle", "Screen-center trace debug");
        row("AdminCreateVehicle", "Spawn vehicle (unlicensed)");
        row("AdminForceNetUpdateOnClientSaturation",
                                            "ForceNetUpdate when saturated");
        row("AdminSetPublicQueueLimit", "Cap or disable public queue");

        Console.WriteLine("\n══════════════════════════ PUBLIC COMMANDS ═════════════════════════");
        Console.WriteLine("{0,-28} {1}", "Command", "Description");
        Console.WriteLine(new string('─', 74));

        // helper for public rows
        row("ChangeTeams / WithId", "Swap to other or given team");
        row("ChatToAll|Team|Squad", "Chat scopes");
        row("CreateSquad", "Create squad with name");
        row("JoinSquadWithName|Id", "Join squad by name / ID");
        row("LeaveSquad", "Leave current squad");
        row("CreateRallyPoint", "SL drops rally point");
        row("ListCommands", "Print every engine command");
        row("ListPermittedCommands", "Print commands you can run");
        row("ListPlayers", "List IDs ↔ names ↔ SteamIDs");
        row("ListSquads", "Show squad indexes");
        row("ShowNextMap", "Display next layer");
        row("ShowCommandInfo", "Details for one command");
        row("Respawn / GiveUp", "Self-respawn options");
        row("DisableUI / SetHudWidgetsEnabled", "Hide/show HUD widgets");
        row("HighResShot", "Take screenshot to AppData");
        row("stat FPS / Unit / UnitGraph", "Perf overlay commands");
        row("r.setres", "Change resolution");
        row("RecordHeatmap", "Generate stat heatmap");
        row("TraceViewToggle", "Object trace (client side)");
        row("DisconnectToMenu", "Exit to main menu");

        Console.WriteLine(new string('═', 74) + "\n");
    }


    private static void OnCommandResponseReceived(object? _, CommandResponseEventArgs e)
    {
        if (_isKeepaliveCommand && !_developerMode)
        {
            _isKeepaliveCommand = false;
            return;   // suppress unless in dev mode
        }
        _isKeepaliveCommand = false;

        if (!string.IsNullOrWhiteSpace(e.Response))
            Console.WriteLine(e.Response);
    }

    private static void OnErrorOccurred(object? _, RconErrorEventArgs e)
    {
        Console.WriteLine($"[ERROR] {e.ErrorMessage}");
        if (e.Exception != null)
            Console.WriteLine($"[EXCEPTION] {e.Exception.GetType().Name}: {e.Exception.Message}");
    }
}
