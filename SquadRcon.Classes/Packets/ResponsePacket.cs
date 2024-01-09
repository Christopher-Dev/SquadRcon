using SquadRcon.Classes.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SquadRcon.Classes.Packets
{
    public class ResponsePacket
    {
        public int Type { get; private set; }
        public List<string>? Body { get; private set; }

        public ResponsePacket(byte[] buffer)
        {
            Parse(buffer.ToList());
        }

        private void Parse(List<byte> buffer)
        {
            if (buffer[4] == RconConstants.SERVERDATA_CHAT_VALUE)
            {
                Type = RconConstants.SERVERDATA_CHAT_VALUE;

                buffer.RemoveRange(0, 8);

                buffer.RemoveRange(buffer.Count - 2, 2);

                Body = ConvertBytesToStringList(buffer);

            }
            else
            {
                switch (buffer.Count())
                {
                    case 256:
                        Type = RconConstants.CommandComplete;

                        buffer.RemoveRange(0, 8);

                        buffer.RemoveRange(buffer.Count - 2, 2);

                        Body = ConvertBytesToStringList(buffer);

                        break;
                    case 10:
                        if (buffer[4] == RconConstants.SERVERDATA_AUTH_RESPONSE)
                        {
                            Type = RconConstants.AuthSuccess;

                            Body = null;
                        }
                        else
                        {
                            Type = RconConstants.EmptyPacket;

                            Body = null;
                        }
                        break;
                    default:
                        Type = RconConstants.SERVERDATA_RESPONSE_VALUE;

                        buffer.RemoveRange(0, 8);

                        buffer.RemoveRange(buffer.Count - 2, 2);

                        Body = ConvertBytesToStringList(buffer);

                        break;
                }
            }
        }

        private List<string> ConvertBytesToStringList(List<byte> buffer)
        {
            string fullString = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
            return new List<string>(fullString.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        // Add additional methods as needed
    }
}
