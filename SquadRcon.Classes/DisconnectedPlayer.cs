using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SquadRcon.Classes
{
    public class DisconnectedPlayer
    {
        public int Id { get; set; }
        public string EosId { get; set; }
        public string SteamId { get; set; }
        public string SinceDisconnect { get; set; }
        public string Name { get; set; }

    }
}
