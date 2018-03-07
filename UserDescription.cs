using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerLogParser
{

    public enum ParseState { Pending, Active, Complete, Failed }
    public enum ParseType { None, Name, ID }

    public struct ParseData
    {
        public string Value;
        public DateTime Timestamp;
        public ParseType Type;
    }

    public class Activity
    {
        public string Username { get; set; }

        public DateTime Login { get; set; }
        public DateTime Logout { get; set; }
        public List<DateTime> Deaths { get; set; }

        public ParseState State { get; set; }

        public bool hasSessionRequest { get; set; }

        public bool hasConnected { get; set; }

        public bool hasValidated { get; set; }

        public bool hasWorldRequest { get; set; }

        public bool IsValidUser
        {
            get
            {
                return hasSessionRequest && hasConnected && hasValidated && hasWorldRequest;
            }
        }

        public Activity()
        {
            Username = string.Empty;

            Deaths = new List<DateTime>();

            State = ParseState.Pending;
            hasSessionRequest = false;
            hasConnected = false;
            hasValidated = false;
            hasWorldRequest = false;
        }
    }
}
