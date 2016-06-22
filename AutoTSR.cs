using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Timers;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using Wolfje.Plugins.SEconomy;
using Wolfje.Plugins.SEconomy.Journal;

namespace AutoTSR
{
    [ApiVersion(1, 23)]
    public class AutoTSR : TerrariaPlugin
    {
        private static string savepath = Path.Combine(TShock.SavePath, "AutoTSR\\");
        public static string configpath = Path.Combine(savepath, "TSRconfig.json");
        public static int LastStamp = 0;
        public static Color MsgColor = new Color(50, 128, 237);
        public static WebClient wc = new WebClient()
        {
            Proxy = null
        };
        public static Voteinfo vi = new Voteinfo();
        public static Timer Timer_IntervalMessages = new Timer() { Enabled = true };
        public static Timer Timer_Checkvotes = new Timer() { Enabled = true };
        public IDbConnection Database;
        public static Config config;

        public override Version Version
        {
            get
            {
                return Assembly.GetExecutingAssembly().GetName().Version;
            }
        }

        public override string Author
        {
            get
            {
                return "Jewsus, Ancientgods";
            }
        }

        public override string Name
        {
            get
            {
                return "AutoTSR";
            }
        }

        public override string Description
        {
            get
            {
                return "Rewrite of Ancientgods plugin. Hands out rewards to those who have voted on terraria-servers.com";
            }
        }

        public AutoTSR(Main game) : base(game)
        {
            Order = 13;
        }

        public override void Initialize()
        {
            bool flag = !Directory.Exists(savepath);
            if (flag)
            {
                Directory.CreateDirectory(savepath);
            }
            ReadConfig();
            bool flag2 = !File.Exists(Path.Combine(savepath, "timestamp"));
            if (flag2)
            {
                save();
            }
            Commands.ChatCommands.Add(new Command("atsr.reload", new CommandDelegate(Reload_Config), new string[]
            {
                "atsreload"
            }));
            Commands.ChatCommands.Add(new Command(new CommandDelegate(vote), new string[]
            {
                "autovote"
            }));
            ServerApi.Hooks.GamePostInitialize.Register(this, new HookHandler<EventArgs>(postInit));
            Database = new SqliteConnection(string.Format("uri=file://{0},Version=3", Path.Combine(savepath, "DailyVote.sqlite")));
            SqlTable table = new SqlTable("Votes", new SqlColumn[]
            {
                new SqlColumn("Username", MySqlDbType.VarChar)
                {
                    Unique = true,
                    Primary = true,
                    Length = new int?(255)
                },
                new SqlColumn("Remaining", MySqlDbType.Int32)
            });
            IDbConnection arg_160_0 = Database;
            IQueryBuilder arg_160_1;
            if (Database.GetSqlType() != SqlType.Sqlite)
            {
                IQueryBuilder queryBuilder = new MysqlQueryCreator();
                arg_160_1 = queryBuilder;
            }
            else
            {
                IQueryBuilder queryBuilder = new SqliteQueryCreator();
                arg_160_1 = queryBuilder;
            }
            SqlTableCreator sqlTableCreator = new SqlTableCreator(arg_160_0, arg_160_1);
            sqlTableCreator.EnsureTableStructure(table);
        }

        private void postInit(EventArgs args)
        {
            Timer_IntervalMessages.Interval = config.MessageInterval_InSeconds * 1000;
            Timer_IntervalMessages.Elapsed += new ElapsedEventHandler(Timer_IntervalMessages_Elapsed);
            Timer_Checkvotes.Interval = config.VoteCheckInterval_InSeconds * 1000;
            Timer_Checkvotes.Elapsed += new ElapsedEventHandler(Timer_Checkvotes_Elapsed);
            LastStamp = load();
        }

        private void vote(CommandArgs args)
        {
            bool flag = !args.Player.IsLoggedIn;
            if (flag)
            {
                args.Player.SendErrorMessage("You need to be logged in to use this command!");
            }
            else
            {
                int num;
                for (int i = 0; i < config.VoteCommandMessage.Length; i = num)
                {
                    string msg = config.VoteCommandMessage[i].Replace("%money%", config.CurrenyReward.ToString());
                    args.Player.SendMessage(msg, MsgColor);
                    num = i + 1;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        private void Timer_IntervalMessages_Elapsed(object sender, ElapsedEventArgs e)
        {
            int num;
            for (int i = 0; i < config.InterValMessage.Length; i = num)
            {
                string msg = config.InterValMessage[i].Replace("%money%", config.CurrenyReward.ToString());
                TSPlayer.All.SendMessage(msg, MsgColor);
                num = i + 1;
            }
        }

        private void Timer_Checkvotes_Elapsed(object sender, ElapsedEventArgs e)
        {
            bool flag = SEconomyPlugin.Instance == null;
            if (flag)
            {
                Console.WriteLine("SEconomy not active, could not hand out currency reward!");
            }
            else
            {
                foreach (Vote current in GetNewVotes())
                {
                    AddVote(current.nickname);
                }
                foreach (DBInfo current2 in GetDbInfos())
                {
                    bool flag2 = current2 != null;
                    if (flag2)
                    {
                        bool flag3 = IsOnLine(current2.Username);
                        if (flag3)
                        {
                            TSPlayer player = GetPlayer(current2.Username);
                            bool flag4 = player != null;
                            if (flag4)
                            {
                                Database.Query("DELETE FROM Votes WHERE Username = @0", new object[]
                                {
                                    current2.Username
                                });
                                IBankAccount bankAccount = SEconomyPlugin.Instance.GetBankAccount(player);
                                Money money = new Money((long)(config.CurrenyReward * current2.Remaining));
                                TSPlayer.All.SendMessage(string.Format("{0} just received {1} for voting on terraria-servers.com!", current2.Username, money), MsgColor);
                                TSPlayer.All.SendMessage("Gain money aswell by voting too, we really appreciate it!", MsgColor);
                                TSPlayer.All.SendMessage("Type /vote for more information.", MsgColor);
                                SEconomyPlugin.Instance.WorldAccount.TransferToAsync(bankAccount, money, BankAccountTransferOptions.AnnounceToReceiver, "voting on terraria-servers.com", "Voted for the server");
                            }
                        }
                    }
                }
            }
        }

        public void AddVote(string name)
        {
            DBInfo dbInfo = GetDbInfo(name);
            bool flag = dbInfo.Username != null;
            if (flag)
            {
                Database.Query("UPDATE Votes SET Remaining = Remaining + 1 WHERE Username = @1", new object[]
                {
                    name
                });
            }
            else
            {
                Database.Query("INSERT INTO Votes (Username, Remaining) VALUES (@0, @1)", new object[]
                {
                    name,
                    1
                });
            }
        }

        public TSPlayer GetPlayer(string name)
        {
            return TShock.Players.FirstOrDefault((TSPlayer p) => p != null && p.IsLoggedIn && p.User != null && p.User.Name == name);
        }

        public bool IsOnLine(string name)
        {
            TSPlayer player = GetPlayer(name);
            return player != null;
        }

        public DBInfo GetDbInfo(string Username)
        {
            DBInfo dBInfo = new DBInfo();
            try
            {
                using (QueryResult queryResult = Database.QueryReader("SELECT * FROM Votes WHERE Username = @0", new object[]
                {
                    Username
                }))
                {
                    bool flag = queryResult.Read();
                    if (flag)
                    {
                        dBInfo.Username = queryResult.Get<string>("Username");
                        dBInfo.Remaining = queryResult.Get<int>("Remaining");
                    }
                }
            }
            catch
            {
            }
            return dBInfo;
        }

        public List<DBInfo> GetDbInfos()
        {
            List<DBInfo> list = new List<DBInfo>();
            try
            {
                using (QueryResult queryResult = Database.QueryReader("SELECT * FROM Votes", new object[0]))
                {
                    while (queryResult.Read())
                    {
                        list.Add(new DBInfo
                        {
                            Username = queryResult.Get<string>("Username"),
                            Remaining = queryResult.Get<int>("Remaining")
                        });
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        public List<Vote> GetNewVotes()
        {
            Voteinfo info = GetInfo();
            List<Vote> voteList = new List<Vote>();
            if (info.votes.Count > 0)
            {
                voteList = info.votes.Where(p => p.timestamp > LastStamp).ToList();
                vi = info;
                save();
            }
            return voteList;
        }

        public Voteinfo GetInfo()
        {
            Voteinfo result = new Voteinfo();
            try
            {
                string value = wc.DownloadString("http://terraria-servers.com/api/?object=servers&element=votes&key=" + config.ServerKey + "&format=json");
                result = JsonConvert.DeserializeObject<Voteinfo>(value);
            }
            catch
            {
                Console.WriteLine("Could not aqcuire vote info, perhaps an invalid key or terraria-servers.com is offline?");
            }
            return result;
        }

        public void save()
        {
            try
            {
                vi = GetInfo();
                LastStamp = ((vi.votes.Count() > 1) ? vi.votes[0].timestamp : 0);
                using (StreamWriter streamWriter = new StreamWriter(File.Open(Path.Combine(savepath, "timestamp"), FileMode.Create)))
                {
                    streamWriter.Write(LastStamp.ToString());
                }
            }
            catch (Exception arg)
            {
                TShock.Log.ConsoleError("[AutoTSR] Error writing to log: " + arg);
            }
        }

        public int load()
        {
            int result;
            try
            {
                using (StreamReader streamReader = new StreamReader(File.Open(Path.Combine(savepath, "timestamp"), FileMode.Open)))
                {
                    result = int.Parse(streamReader.ReadToEnd());
                    return result;
                }
            }
            catch
            {
                vi = GetInfo();
                bool flag = vi.votes.Count > 0;
                if (flag)
                {
                    result = vi.votes[0].timestamp;
                    return result;
                }
            }
            result = 0;
            return result;
        }

        private static void CreateConfig()
        {
            try
            {
                using (StreamWriter streamWriter = new StreamWriter(File.Open(configpath, FileMode.Create)))
                {
                    config = new Config();
                    streamWriter.Write(JsonConvert.SerializeObject(config, Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError(ex.Message);
                config = new Config();
            }
        }

        private static bool ReadConfig()
        {
            bool result;
            try
            {
                bool flag = File.Exists(configpath);
                if (flag)
                {
                    using (StreamReader streamReader = new StreamReader(File.Open(configpath, FileMode.Open)))
                    {
                        config = JsonConvert.DeserializeObject<Config>(streamReader.ReadToEnd());
                        result = true;
                        return result;
                    }
                }
                TShock.Log.ConsoleError("AutoTSR config not found. Creating new one...");
                CreateConfig();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError(ex.Message);
            }
            result = false;
            return result;
        }

        private void Reload_Config(CommandArgs args)
        {
            if (ReadConfig())
            {
                args.Player.SendMessage("AutoTSR config reloaded sucessfully.", Color.Green);
                Timer_IntervalMessages.Interval = config.MessageInterval_InSeconds * 1000;
                Timer_Checkvotes.Interval = config.VoteCheckInterval_InSeconds * 1000;
            }
            else
                args.Player.SendErrorMessage("AutoTSR config reloaded unsucessfully. Check logs for details.");
        }

        public class DBInfo
        {
            public string Username = null;
            public int Remaining = 0;
        }

        public class Voteinfo
        {
            public List<Vote> votes;

            public Voteinfo()
            {
                votes = new List<Vote>();
            }
        }

        public class Vote
        {
            public string date;
            public int timestamp;
            public string nickname;
            public int claimed;
        }

        public class Config
        {
            public string ServerKey = "put_serverkey_here";
            public int CurrenyReward = 10000;
            public string[] VoteCommandMessage = new string[4]
            {
        "Vote for our server to earn %money% GoldPieces!",
        "Go to www.terraria-servers.com, click \"enter_server_name_here\"",
        "Now click the vote button and type your name in the \"NickName\" field",
        "Do the captcha and click vote. Rewards are issued 1-4 minutes after voting!"
            };
            public int VoteCheckInterval_InSeconds = 60;
            public int MessageInterval_InSeconds = 300;
            public string[] InterValMessage = new string[2]
            {
        "Vote for our server and get %money% Goldpieces for FREE!",
        "Type /vote for more information."
            };
        }
    }
}
