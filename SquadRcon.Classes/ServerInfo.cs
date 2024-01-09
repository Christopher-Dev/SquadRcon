using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SquadRcon.Classes
{
    public class ServerInfo
    {
        public int MaxPlayers { get; set; }
        public string GameMode_s { get; set; }
        public string MapName_s { get; set; }
        public string GameVersion_s { get; set; }
        public int PLAYTIME_I { get; set; }
        public int Flags_I { get; set; }
        public string MATCHHOPPER_s { get; set; }
        public double MatchTimeout_d { get; set; }
        public string SESSIONTEMPLATENAME_s { get; set; }
        public bool Password_b { get; set; }
        public int PlayerCount_I { get; set; }
        public string SEARCHKEYWORDS_s { get; set; }
        public string NextLayer_s { get; set; }
        public int PlayerReserveCount_I { get; set; }
        public string PublicQueueLimit_I { get; set; }
        public string ServerName_s { get; set; }
        public int CurrentModLoadedCount_I { get; set; }
        public bool AllModsWhitelisted_b { get; set; }
        public string TeamOne_s { get; set; }
        public string TeamTwo_s { get; set; }
        public int PublicQueue_I { get; set; }
        public int ReservedQueue_I { get; set; }
        public int BeaconPort_I { get; set; }
    }
}
