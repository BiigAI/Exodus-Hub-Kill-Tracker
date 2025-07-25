using System;

namespace ExodusHub_Kill_Tracker
{
    public class KillData
    {
        public string Killer { get; set; }
        public string Victim { get; set; }
        public string Weapon { get; set; }
        public string Location { get; set; }
        public string Timestamp { get; set; }
        public string EventId { get; set; } // Optional, can be empty
        public string Details { get; set; } // Optional, can be empty
    }
}
