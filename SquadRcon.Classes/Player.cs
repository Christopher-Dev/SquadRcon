using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SquadRcon.Classes
{
    public class Player
    {
        public int Id { get; set; }
        public string EosId { get; set; }
        public string SteamId { get; set; }
        public string Name { get; set; }
        public int TeamId { get; set; }
        public int SquadId { get; set; }
        public bool IsLeader { get; set; }
        public string Role { get; set; }

    }
}
