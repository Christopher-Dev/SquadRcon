using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SquadRcon
{
    public class DisconnectedPlayer
    {
        public int Id { get; set; }
        public string EosId { get; set; } = string.Empty;
        public string SteamId { get; set; } = string.Empty;
        public string SinceDisconnect { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

    }
}
