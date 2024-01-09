using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SquadRcon.Classes
{
    public class Packet
    {
        public int Size { get; set; }
        public int Id { get; set; }
        public int Type { get; set; }
        public string Body { get; set; }

        public Packet(int type, int id, string body)
        {
            Type = type;
            Id = id;
            Body = body;
            Size = Encoding.UTF8.GetByteCount(body) + 10;
        }
    }
}
