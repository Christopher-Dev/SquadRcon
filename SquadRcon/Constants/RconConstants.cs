using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SquadRcon.Constants
{
    public static class RconConstants
    {
        //Server Command Request
        public const int SERVERDATA_EXECCOMMAND = 0x02;

        //Auth Request Packet
        public const int SERVERDATA_AUTH = 0x03;

        //Auth response Success
        public const int SERVERDATA_AUTH_RESPONSE = 0x02;

        //Server Chat Response
        public const int SERVERDATA_CHAT_VALUE = 0x01;

        //Server response on from the server
        public const int SERVERDATA_RESPONSE_VALUE = 0x00;

        //AuthSuccess
        public const int AuthSuccess = 2;

        //EmptyPacket
        public const int EmptyPacket = 100;

        //EmptyPacket
        public const int CommandComplete = 256;
    }
}
