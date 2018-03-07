using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace ServerLogParser
{

    public class SpaceEngineersLogParser
    {
        private const string databaseName = "TheLastBastionDatabase.sqlite";

        public Dictionary<string, List<Activity>> Sessions { get; set; }

        private SQLiteConnection sqlite;
        private DateTime lastDate;
        private List<string> logFileNames = new List<string>();
        private List<string> activeUserIds = new List<string>();
        private List<Activity> pendingActivities = new List<Activity>();

        /// <summary>
        /// Initializes members
        /// </summary>
        /// <param name="path"></param>
        public SpaceEngineersLogParser(string path)
        {
            Sessions = new Dictionary<string, List<Activity>>();

            if (Directory.Exists(path))
            {
                string[] fileList = Directory.GetFiles(path);

                foreach (string file in fileList)
                {
                    if (file.EndsWith(".log"))
                    {
                        logFileNames.Add(file);
                    }
                }

                if (logFileNames.Count == 0)
                {
                    Console.WriteLine("No .log files in directory");
                }
            }
            else
            {
                Console.WriteLine("{0} is not a valid directory name", path);
            }
        }

        /// <summary>
        /// Creates and/or establishes a connection to the database 
        /// </summary>
        public void Initialize()
        {
            if (!File.Exists(databaseName))
            {
                SQLiteConnection.CreateFile(databaseName);

                sqlite = new SQLiteConnection(string.Format("Data Source={0};Version=3;", databaseName));
                sqlite.Open();

                string databaseConfig = File.ReadAllText("./BuildDatabase.txt");
                SQLiteCommand command = new SQLiteCommand(databaseConfig, sqlite);
                command.ExecuteNonQuery();
            }
            else
            {
                sqlite = new SQLiteConnection(string.Format("Data Source={0};Version=3;", databaseName));
                sqlite.Open();
            }
        }

        /// <summary>
        /// Parses server logs to a dictionary
        /// </summary>
        public void Run()
        {
            foreach (string file in logFileNames)
            {
                Console.WriteLine("\nReading file {0}\n", file);

                string[] dataToBeParsed = File.ReadAllLines(file);

                // get the last known timestamp
                foreach (string line in dataToBeParsed.Reverse())
                {
                    if (hasTimestamp(line, out lastDate))
                    {
                        break;
                    }
                }

                for (int i = 0; i < dataToBeParsed.Length; i++)
                {
                    string line = dataToBeParsed[i];

                    ParseData data;
                    if (hasSessionRequest(line, out data))
                    {
                        activeUserIds.Add(data.Value);
                    }
                    else if (hasConnectedClient(line, out data))
                    {
                        pendingActivities.Add(new Activity()
                        {
                            Username = data.Value,
                            hasConnected = true,
                            Login = data.Timestamp
                        });
                        //Console.WriteLine("DEBUG {0} - User '{1}' added activity to queue", i, data.Value);
                    }
                    else if (hasConnectionFailed(line, out data))
                    {
                        if (Sessions.ContainsKey(data.Value))
                        {
                            Activity activity = Sessions[data.Value].Last();
                            activity.Logout = data.Timestamp;
                            activity.State = ParseState.Failed;
                            activeUserIds.RemoveAll(x => x == data.Value);

                           //printSessionLog(data.Value, activity);
                        }
                        else
                        {
                            Console.WriteLine("Line {1}: CONNECTION FAILED ERROR The Session ID '{0}' is not active", data.Value, i);
                        }
                    }
                    else if (hasValidAuthTicket(line, out data))
                    {
                        if (pendingActivities.Count > 0)
                        {
                            string username;
                            ParseData verifyUsername;
                            if (hasConnectedClient(dataToBeParsed[i - 1], out verifyUsername) && pendingActivities.Find(x => x.Username == verifyUsername.Value) != null)
                            {
                                username = verifyUsername.Value;
                            }
                            else
                            {
                                username = pendingActivities[0].Username;
                            }

                            if (Sessions.ContainsKey(data.Value))
                            {
                                string currentActivityUsername = Sessions[data.Value].Last().Username;
                                if (username != currentActivityUsername)
                                {

                                    if (pendingActivities.Find(x => x.Username == currentActivityUsername) != null)
                                    {
                                        username = currentActivityUsername;
                                    }
                                    else
                                    {
                                        Console.WriteLine("WARNING Player name change from '{0}' to '{1}'", Sessions[data.Value].Last().Username, username);

                                        if (pendingActivities.FindIndex(x => x.Username == username) != 0)
                                        {
                                            Console.WriteLine("WARNING WARNING WARNING pulling activity out of order index: {0}", pendingActivities.FindIndex(x => x.Username == username));
                                        }
                                    }
                                }
                            }

                                Activity activity = pendingActivities.Find(x => x.Username == username);
                            activity.hasValidated = true;
                            activity.State = ParseState.Active;

                            if (activeUserIds.Contains(data.Value))
                            {
                                activity.hasSessionRequest = true;
                                activeUserIds.RemoveAll(x => x == data.Value);
                            }

                            if (Sessions.ContainsKey(data.Value))
                            {
                                Sessions[data.Value].Add(activity);
                            }
                            else
                            {
                                Sessions.Add(data.Value, new List<Activity>() { activity });
                            }

                            pendingActivities.RemoveAll(x => x.Username == username);
                            //Console.WriteLine("DEBUG {0} - User '{1}' removed activity from queue", i, username);
                            //Console.WriteLine("DEBUG {0} - [Validated+Added] {1}  {2}", i, data.Value, Sessions[data.Value].Last().Username);
                        }
                        else
                        {
                            Console.WriteLine("Line {1}: VALIDATION ERROR no pending activities", data.Value, i);
                        }
                    }
                    else if (hasWorldRequest(line, out data))
                    {
                        try
                        {
                            KeyValuePair<string, List<Activity>> session = Sessions.First(x => x.Value.Find(y => y.Username == data.Value) != null);
                            session.Value.Last().hasWorldRequest = true;
                            //Console.WriteLine("DEBUG {0} - World Request {1}  {2}", i, session.Key, data.Value);
                        }
                        catch
                        {
                            Console.WriteLine("Line {1}: Warning: Could not find the user '{0}' on world connect", data.Value, i);
                        }
                    }
                    else if (hasPlayerDied(line, out data))
                    {

                        if (Sessions.ContainsKey(data.Value))
                        {
                            Sessions[data.Value].Last().Deaths.Add(data.Timestamp);
                            //Console.WriteLine("DEBUG {0} - Died {1}  {2}", i, data.Value, Sessions[data.Value].Last().Username);
                        }
                        else
                        {
                            Console.WriteLine("Line {1}: DEATH ERROR: Could not find user ID '{0}'", data.Value, i);
                        }
                    }
                    else if (hasUserLeft(line, out data))
                    {
                        if (data.Type == ParseType.ID)
                        {
                            if (Sessions.ContainsKey(data.Value))
                            {
                                Activity activity = Sessions[data.Value].Last();
                                if (activity.State == ParseState.Active)
                                {
                                    activity.Logout = data.Timestamp;
                                    activity.State = ParseState.Complete;
                                }
                                else
                                {
                                    activity.Logout = data.Timestamp;
                                }

                                printSessionLog(data.Value, activity);
                            }
                            else
                            {
                                Console.WriteLine("Line {1}: LOGOUT ERROR: Could not find steamId '{0}'", data.Value, i);
                            }
                        }
                        else if (data.Type == ParseType.Name)
                        {
                            try
                            {
                                KeyValuePair<string, List<Activity>> session = Sessions.First(x => x.Value.Find(y => y.Username == data.Value) != null);

                                Activity activity = session.Value.Last();
                                if (activity.State == ParseState.Active)
                                {
                                    activity.Logout = data.Timestamp;
                                    activity.State = ParseState.Complete;
                                }
                                else
                                {
                                    activity.Logout = data.Timestamp;
                                }

                                printSessionLog(session.Key, activity);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Line {1}: LOGOUT ERROR: Could not find the active user '{0}'", data.Value, i);
                            }
                        }
                        else
                        {
                            Console.WriteLine("ERROR: Bad data type");
                        }


                    }
                }

                Console.WriteLine("\n##########################################################\nApplying logout date to all users connected when the server shutdown\n##########################################################");
                foreach (KeyValuePair<string, List<Activity>> session in Sessions)
                {
                    for (int i = 0; i < session.Value.Count; i++)
                    {
                        Activity activity = session.Value[i];
                        if (activity.State == ParseState.Active)
                        {
                            if (i == session.Value.Count - 1)
                            {
                                activity.Logout = lastDate;
                                activity.State = ParseState.Complete;
                            }
                            else
                            {
                                Console.WriteLine("Warning a non primary activity is still in the Active state");
                            }

                            printSessionLog(session.Key, activity);
                        }
                        else if (activity.State == ParseState.Pending)
                        {
                            Console.WriteLine("Warning an activity made it to the end in the pending state");
                        }
                    }
                }
            }

            WriteToDatabase();
        }

        /// <summary>
        /// Does what the name implies
        /// </summary>
        private void WriteToDatabase()
        {
            Console.WriteLine(""); // for aestetics

            int counter = 0;
            List<string> sqlStatments = new List<string>();
            foreach (KeyValuePair<string, List<Activity>> session in Sessions)
            {
                foreach (Activity activity in session.Value)
                {
                    sqlStatments.Add(string.Format("INSERT INTO users (steam_id, username) VALUES ('{0}', '{1}')", session.Key, activity.Username));
                    sqlStatments.Add(string.Format(
                        "INSERT INTO activity (steam_id, username, login, logout, state, has_session_request, has_connected, has_validated, has_world_request) VALUES ('{0}', '{1}', '{2}', '{3}', '{4}', {5}, {6}, {7}, {8})",
                        session.Key,
                        activity.Username,
                        activity.Login.ToString("yyyy-MM-dd HH:mm:ss"),
                        activity.Logout.ToString("yyyy-MM-dd HH:mm:ss"),
                        activity.State.ToString(),
                        (activity.hasSessionRequest ? 1 : 0),
                        (activity.hasConnected ? 1 : 0),
                        (activity.hasValidated ? 1 : 0),
                        (activity.hasWorldRequest ? 1 : 0)
                    ));

                    foreach (DateTime timeOfDeath in activity.Deaths)
                    {
                        sqlStatments.Add(string.Format("INSERT INTO deaths (steam_id, username, time_of_death) VALUES ('{0}', '{1}', '{2}')", session.Key, activity.Username, timeOfDeath));
                        counter++;
                    }

                    counter += 2;
                    Console.SetCursorPosition(0, Console.CursorTop);
                    Console.Write("Generating Sql Statments: {0}", counter);
                    // note things left in the pending state are failed sessions and should not be logged
                }
            }

            for (int i = 0; i < sqlStatments.Count; i++)
            {
                try
                {
                    SQLiteCommand command = new SQLiteCommand(sqlStatments[i], sqlite);
                    command.ExecuteNonQuery();
                }
                catch (Exception e)
                {
                    if (!e.Message.Contains("UNIQUE constraint failed:"))
                    {
                        Console.WriteLine(e.ToString());
                    }
                }

                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write("Writing to database: {0} / {1}", i, sqlStatments.Count);
            }

            sqlite.Close();
            Console.WriteLine("    Done");
        }

        private void printSessionLog(string steamId, Activity activity)
        {
            int tab_count = (int)Math.Ceiling((32f - activity.Username.Length) / 8f);
            int tab_count2 = (int)Math.Ceiling((24f - steamId.Length) / 8f);
            Console.WriteLine("{4}{6}{0}{1}{2}   \t{3}\t{5}", activity.Username, new String('\t', tab_count), activity.Login, activity.Logout, steamId, activity.State.ToString(), new String('\t', tab_count2));
        }

        private const string Timestamp = " - Thread:";
        public static bool hasTimestamp(string entry, out DateTime date)
        {
            date = DateTime.MinValue;

            if (entry.Contains(Timestamp))
            {
                string[] data = entry.Split(new string[] { Timestamp }, StringSplitOptions.None);
                date = DateTime.Parse(data[0]);
                return true;
            }

            return false;
        }

        private const string SessionRequest = "Peer2Peer_SessionRequest ";
        public static bool hasSessionRequest(string entry, out ParseData data)
        {
            data = new ParseData();

            if (entry.Contains(SessionRequest))
            {
                data.Value = entry.Split(new string[] { SessionRequest }, StringSplitOptions.None)[1];
                hasTimestamp(entry, out data.Timestamp);
                return true;
            }

            return false;
        }

        private const string OnConnectedClient = "OnConnectedClient ";
        public static bool hasConnectedClient(string entry, out ParseData data)
        {
            data = new ParseData();

            if (entry.Contains(OnConnectedClient))
            {
                data.Value = entry.Split(new string[] { OnConnectedClient }, StringSplitOptions.None)[1];
                hasTimestamp(entry, out data.Timestamp);
                // removes " attempt" from the end of the line
                data.Value = data.Value.Substring(0, data.Value.Length - 8);
                return true;
            }

            return false;
        }

        private const string ConnectionFailed = "Peer2Peer_ConnectionFailed ";
        public static bool hasConnectionFailed(string entry, out ParseData data)
        {
            data = new ParseData();

            if (entry.Contains(ConnectionFailed))
            {
                data.Value = entry.Split(new string[] { ConnectionFailed, ", Timeout" }, StringSplitOptions.None)[1];
                hasTimestamp(entry, out data.Timestamp);
                return true;
            }

            return false;
        }

        private const string ValidateAuthTicket = "Server ValidateAuthTicketResponse (OK), owner: ";
        public static bool hasValidAuthTicket(string entry, out ParseData data)
        {
            data = new ParseData();

            if (entry.Contains(ValidateAuthTicket))
            {
                data.Value = entry.Split(new string[] { ValidateAuthTicket }, StringSplitOptions.None)[1];
                hasTimestamp(entry, out data.Timestamp);
                return true;
            }

            return false;
        }

        private const string WorldRequest = "World request received: ";
        public static bool hasWorldRequest(string entry, out ParseData username)
        {
            username = new ParseData();

            if (entry.Contains(WorldRequest))
            {
                username.Value = entry.Split(new string[] { WorldRequest }, StringSplitOptions.None)[1];
                hasTimestamp(entry, out username.Timestamp);
                return true;
            }

            return false;
        }

        private const string PlayerDied = "Player character died. Id : ";
        public static bool hasPlayerDied(string entry, out ParseData data)
        {
            data = new ParseData();

            if (entry.Contains(PlayerDied))
            {
                data.Value = entry.Split(new string[] { PlayerDied }, StringSplitOptions.None)[1];
                hasTimestamp(entry, out data.Timestamp);
                return true;
            }

            return false;
        }

        private const string UserLeftID = "User left ID:";
        private const string UserLeft = "User left ";
        public static bool hasUserLeft(string entry, out ParseData data)
        {
            data = new ParseData();

            if (entry.Contains(UserLeftID))
            {
                data.Type = ParseType.ID;
                data.Value = entry.Split(new string[] { UserLeftID }, StringSplitOptions.None)[1];
                hasTimestamp(entry, out data.Timestamp);
                return true;
            }
            else if (entry.Contains(UserLeft))
            {
                data.Type = ParseType.Name;
                data.Value = entry.Split(new string[] { UserLeft }, StringSplitOptions.None)[1];
                hasTimestamp(entry, out data.Timestamp);
                return true;
            }

            return false;
        }

    }

    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                SpaceEngineersLogParser parser = new SpaceEngineersLogParser(args[0]);
                parser.Initialize();
                parser.Run();
            }
            else
            {
                Console.WriteLine("ERROR: Please supply the directory path to the log files");
            }

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();

        }
    }
}
