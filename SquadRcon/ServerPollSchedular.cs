using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SquadRcon.SDK
{
    class ServerPollScheduler
    {
        private DateTime nextPollTime;
        private TimeSpan pollInterval;
        private bool isActive;

        public ServerPollScheduler(TimeSpan interval)
        {
            pollInterval = interval;
            isActive = false;
        }

        public bool IsTimeToPoll => DateTime.Now >= nextPollTime;

        public void Start()
        {
            nextPollTime = DateTime.Now.Add(pollInterval);
            isActive = true;
        }

        public void Reset()
        {
            if (isActive)
            {
                nextPollTime = DateTime.Now.Add(pollInterval);
            }
        }

        public void Stop()
        {
            isActive = false;
            // Add any additional cleanup logic here if needed
        }
    }
}
