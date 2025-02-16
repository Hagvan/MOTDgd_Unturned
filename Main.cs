﻿using Quobject.SocketIoClientDotNet.Client;
using Rocket.Core.Plugins;
using Rocket.API;
using System;
using System.Linq;
using Rocket.Unturned;
using Rocket.Unturned.Player;
using Rocket.Unturned.Chat;
using Steamworks;
using System.Collections.Generic;
using System.Timers;
using SDG.Unturned;
using Rocket.Core;
using Newtonsoft.Json;
using Rocket.API.Collections;
using UnityEngine;
using System.Collections;

namespace MOTDgd
{
    //Přidat podporu pro Uconomy a translation listy
    public class Main : RocketPlugin<MOTDgdConfiguration>
    {
        //Setting up variables
        public static int Server_ID;
        public static bool Connected;
        public static Dictionary<CSteamID, int> Ad_Views = new Dictionary<CSteamID,int>();
        public static Dictionary<CSteamID, long> Cooldown = new Dictionary<CSteamID, long>();
        public static Dictionary<string, int> Reward_dictionary = new Dictionary<string, int>();
        public static Dictionary<string, KeyValuePair<string, Color>> Translation_dictionary = new Dictionary<string, KeyValuePair<string, Color>>();
        public static Dictionary<CSteamID, int> Sequence = new Dictionary<CSteamID, int>();
        public static Dictionary<CSteamID, int> Awaiting_command = new Dictionary<CSteamID, int>();
        public static Dictionary<CSteamID, string> Connect_link = new Dictionary<CSteamID, string>();
        public static List<CSteamID> Request_players = new List<CSteamID>();
        public static List<CSteamID> Connect_awaiting = new List<CSteamID>();
        public static List<SayMessage> messages_to_say = new List<SayMessage>();
        public static bool cheats = Provider.hasCheats;
        public static int ads_before_cooldown;
        public static int reminder_delay;
        public static int cooldown_delay;
        public static bool global_messages;
        public static bool Ad_on_join;
        public static bool reapply_join;
        public static bool vid_unavailable;
        public static bool advanced_logging;
        public static string reward_mode;
        private static string mod_name = "MOTDgdCommandAd for Unturned";
        private static string P_version = "2.0.3.2";
        private Timer cooldownTimer;
        private Timer reminderTimer;
        public static string User_ID;
        public static Main Instance;
        public static Socket socket;
        public static CSteamID Executor_ID = (CSteamID)0;

        public class SayMessage
        {
            public IRocketPlayer player { get; }
            public string text { get; }
            public Color color { get; }

            public byte type;
            public SayMessage(IRocketPlayer player, string text, Color color)
            {
                this.player = player;
                this.text = text;
                this.color = color;
                type = 0;
            }

            public SayMessage(IRocketPlayer player, string text)
            {
                this.player = player;
                this.text = text;
                type = 1;
            }

            public SayMessage(string text, Color color)
            {
                this.text = text;
                this.color = color;
                type = 2;
            }
        }

        protected override void Load()
        {
            base.Load();
            try {
                Rocket.Core.Logging.Logger.Log("Loading " + mod_name + " version " + P_version);
                //Rocket.Core.Logging.Logger.Log("Server IP: " + " Server port: " + Provider.port);
                Instance = this;
                if (!parseConfig()) { return; };
                //Creating socket connection
                socket = IO.Socket("http://hub.motdgd.com");
                Rocket.Core.Logging.Logger.Log("Connecting to HUB");

                //Logging in to node
                socket.On("connect", () =>
                {
                    Rocket.Core.Logging.Logger.Log("Connected to HUB");
                    socket.Emit("login", new object[] { Configuration.Instance.User_ID, Parser.getIPFromUInt32(SDG.Unturned.Provider.ip), SDG.Unturned.Provider.port, SDG.Unturned.Provider.APP_VERSION, mod_name, P_version, "unturned" });
                    Connected = true;
                });

                //Reading Server ID
                socket.On("login_response", (arguments) =>
                {
                    string login_data = arguments + "";
                    int.TryParse(login_data, out Server_ID);
                    Rocket.Core.Logging.Logger.Log("Received ID " + Server_ID + " from the HUB");
                });

                //Getting names of people that completed Advertisement
                socket.On("complete_response", (arguments) =>
                {
                    string resp_data = arguments + "";
                    UnturnedPlayer currentPlayer = getPlayer(resp_data);

                    if (currentPlayer != null)
                    {
                        if (Request_players.Contains(currentPlayer.CSteamID))
                        {
                            Request_players.Remove(currentPlayer.CSteamID);
                            if (advanced_logging == true)
                            {
                                if (!OnCooldown(currentPlayer))
                                {
                                    Rocket.Core.Logging.Logger.Log("User " + currentPlayer.DisplayName + " completed advertisement.");
                                }
                                else
                                {
                                    Rocket.Core.Logging.Logger.Log("User " + currentPlayer.DisplayName + " completed advertisement, but is on cooldown");
                                }
                            }

                            if (!OnCooldown(currentPlayer))
                            {
                                GiveReward(currentPlayer);
                            }
                            else
                            {
                                messages_to_say.Add(new SayMessage(currentPlayer, Translation_dictionary["COOLDOWN"].Key, Translation_dictionary["COOLDOWN"].Value));
                            }
                        }
                    }
                    else
                    {
                        Rocket.Core.Logging.Logger.LogWarning("Player with CSteamID " + resp_data + " completed advertisement but is not on the server.");
                    }
                });

                socket.On("link_response", (args) =>
                {
                    Dictionary<string, string> Response = JsonConvert.DeserializeObject<Dictionary<string, string>>(args + "");
                    string pid = Response["pid"];
                    string link = Response["url"];
                    string message = Response["msg"];

                    UnturnedPlayer player = getPlayer(pid);
                    if (advanced_logging)
                    {
                        Rocket.Core.Logging.Logger.Log("Received link for player " + player.DisplayName + " with CSteamID " + pid);
                    }
                    if (player != null)
                    {
                        if (!Connect_awaiting.Contains(player.CSteamID))
                        {
                            if (Awaiting_command.ContainsKey(player.CSteamID))
                            {
                                Awaiting_command.Remove(player.CSteamID);
                            }
                            
                            player.Player.sendBrowserRequest(Translation_dictionary["LINK_RESPONSE"].Key, link);
                        }
                        else
                        {
                            if (Awaiting_command.ContainsKey(player.CSteamID))
                            {
                                Awaiting_command.Remove(player.CSteamID);
                            }

                            if (advanced_logging)
                            {
                                Rocket.Core.Logging.Logger.Log("Received link on login for player " + player.DisplayName + " - link = " + link.Substring(0, 15) + "...");
                            }
                            Connect_link.Add(player.CSteamID, link);
                            Connect_awaiting.Remove(player.CSteamID);
                        }
                    }
                    else
                    {
                        Rocket.Core.Logging.Logger.LogError("Player with CSteamID " + pid + " requested link, but is not on the server");
                    }

                });

                //Disconnecting from node
                socket.On("disconnect", () =>
                {
                    Rocket.Core.Logging.Logger.LogWarning("Disconnected");
                    Server_ID = 0;
                    Connected = false;
                });

                socket.On("error", (arguments) =>
                {
                    Rocket.Core.Logging.Logger.LogError("There was an error with node: " + arguments);
                    Server_ID = 0;
                    Connected = false;
                });

                socket.On("aderror_response", (arguments) =>
                {
                    UnturnedPlayer player = getPlayer(arguments + "");

                    if (vid_unavailable)
                    {
                        if (player != null)
                        {
                            if (Request_players.Contains(player.CSteamID))
                            {
                                Request_players.Remove(player.CSteamID);
                                if (advanced_logging == true)
                                {
                                    if (!OnCooldown(player))
                                    {
                                        Rocket.Core.Logging.Logger.Log("User " + player.DisplayName + " completed advertisement.");
                                    }
                                    else
                                    {
                                        Rocket.Core.Logging.Logger.Log("User " + player.DisplayName + " completed advertisement, but is on cooldown");
                                    }
                                }

                                if (!OnCooldown(player))
                                {
                                    GiveReward(player);
                                }
                                else
                                {
                                    messages_to_say.Add(new SayMessage(player, Translation_dictionary["COOLDOWN"].Key, Translation_dictionary["COOLDOWN"].Value));
                                }
                            }
                        }
                        else
                        {
                            Rocket.Core.Logging.Logger.LogWarning("Player with CSteamID " + arguments + " completed advertisement but is not on the server.");
                        }
                    }
                    else
                    {
                        if (advanced_logging)
                        {
                            Rocket.Core.Logging.Logger.Log(player.DisplayName + " had trouble wathching video");
                        }
                        
                        messages_to_say.Add(new SayMessage(player, Translation_dictionary["COMPLETED_WITHOUT_VIDEO"].Key, Translation_dictionary["COMPLETED_WITHOUT_VIDEO"].Value));
                    }
                });

                //Telling player about rewards
                U.Events.OnPlayerConnected += Connect_event;
                U.Events.OnPlayerDisconnected += Disconnect_event;
                Rocket.Unturned.Events.UnturnedPlayerEvents.OnPlayerUpdatePosition += UnturnedPlayerEvents_OnPlayerUpdatePosition;

                //Timer checking Cooldown players
                //StartCoroutine(timerFunc());
                cooldownTimer = new Timer();
                cooldownTimer.Elapsed += new ElapsedEventHandler(timerFunc);
                cooldownTimer.Interval = 2000;
                cooldownTimer.Enabled = true;

                if (reminder_delay != 0)
                {
                    //StartCoroutine(reminderFunc());
                    reminderTimer = new Timer();
                    reminderTimer.Elapsed += new ElapsedEventHandler(reminderFunc);
                    reminderTimer.Interval = reminder_delay * 60 * 1000;
                    reminderTimer.Enabled = true;
                }

                //StartCoroutine(cooldownCheckerFunc());

                StartCoroutine(Say());
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
            }
        }

        private IEnumerator Say()
        {
            while (true)
            {
                if (Provider.isServer)
                {
                    foreach (SayMessage m in messages_to_say)
                    {
                        switch (m.type)
                        {
                            case 0:
                                UnturnedChat.Say(m.player, m.text, m.color);
                                break;
                            case 1:
                                UnturnedChat.Say(m.player, m.text);
                                break;
                            case 2:
                                UnturnedChat.Say(m.text, m.color);
                                break;
                        }
                    }
                    messages_to_say.Clear();
                }
                yield return new WaitForSeconds(1f);
            }
        }

        private void UnturnedPlayerEvents_OnPlayerUpdatePosition(UnturnedPlayer player, Vector3 position)
        {
            try
            {
                if (Connect_link.ContainsKey(player.CSteamID))
                {
                    if (advanced_logging)
                    {
                        Rocket.Core.Logging.Logger.Log("Player " + player + " moved. Sending link " + Connect_link[player.CSteamID].Substring(0, 15) + "...");
                    }
                    player.Player.sendBrowserRequest(Translation_dictionary["LINK_RESPONSE"].Key, Connect_link[player.CSteamID]);
                    Connect_link.Remove(player.CSteamID);
                }
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
            }
        }

        private void Disconnect_event(UnturnedPlayer player)
        {
            Awaiting_command.Remove(player.CSteamID);
            Connect_link.Remove(player.CSteamID);
            Connect_awaiting.Remove(player.CSteamID);
        }

        private void Connect_event(UnturnedPlayer player)
        {
            try
            {
                if (!Ad_Views.ContainsKey(player.CSteamID))
                {
                    Ad_Views[player.CSteamID] = 0;
                }

                if (!Sequence.ContainsKey(player.CSteamID))
                {
                    Sequence[player.CSteamID] = 0;
                }

                if (Configuration.Instance.Join_Commands.Count != 0 && !player.HasPermission("motdgd.immune"))
                {
                    foreach (string command in Configuration.Instance.Join_Commands)
                    {
                        if (!cheats)
                        {
                            Provider.hasCheats = true;
                        }

                        bool success = R.Commands.Execute((IRocketPlayer)UnturnedPlayer.FromCSteamID(Executor_ID), command.Replace("(player)", player.DisplayName).Replace("(steamid)", player.CSteamID + ""));

                        if (!cheats)
                        {
                            Provider.hasCheats = false;
                        }

                        if (!success)
                        {
                            Rocket.Core.Logging.Logger.LogError("Failed to execute command " + command.Replace("(player)", player.CharacterName).Replace("(steamid)", player.CSteamID + "") + " while trying to give reward to " + player.DisplayName);
                        }
                    }
                };

                if (Connected && !OnCooldown(player) && Ad_on_join && !player.HasPermission("motdgd.immune"))
                {
                    Connect_awaiting.Add(player.CSteamID);
                    if (advanced_logging)
                    {
                        Rocket.Core.Logging.Logger.Log("Adding player " + player.DisplayName + " to link on connect queue.");
                    }
                    request_link(player);
                }
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
            }
        }

        protected override void Unload()
        {
            base.Unload();
            if (reminderTimer != null)
            {
                reminderTimer.Enabled = false;
            }
            if (cooldownTimer != null)
            {
                cooldownTimer.Enabled = false;
            }
            StopAllCoroutines();
            U.Events.OnPlayerConnected -= Connect_event;
            U.Events.OnPlayerDisconnected -= Disconnect_event;
            Rocket.Unturned.Events.UnturnedPlayerEvents.OnPlayerUpdatePosition -= UnturnedPlayerEvents_OnPlayerUpdatePosition;
            if (socket != null)
            {
                socket.Disconnect();
            }
            Server_ID = 0;
            Connected = false;
            Ad_Views.Clear();
            Reward_dictionary.Clear();
            Sequence.Clear();
            Cooldown.Clear();
            Connect_link.Clear();
            Connect_awaiting.Clear();
        }


        //Get player variable from received CSteamID
        public UnturnedPlayer getPlayer(string id)
        {
            try
            {
                CSteamID new_ID = (CSteamID)UInt64.Parse(id);
                UnturnedPlayer player = UnturnedPlayer.FromCSteamID(new_ID);
                return player;
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
                return UnturnedPlayer.FromCSteamID((CSteamID)0);
            }
        }

        //Give Reward
        public void GiveReward(UnturnedPlayer player)
        {
            switch (reward_mode)
            {
                case "all":
                    GiveReward_All(player);
                    break;
                case "sequential":
                    GiveReward_Sequential(player);
                    break;
                case "weighted":
                    GiveReward_Weighted(player);
                    break;
                case "random":
                    GiveReward_Random(player);
                    break;
                default:
                    Rocket.Core.Logging.Logger.LogError("Couldn't determine reward mode. Check your config!");
                    break;
            }
        }

        public void GiveReward_All (UnturnedPlayer player)
        {
            try
            {
                foreach (var pair in Reward_dictionary)
                {
                    if (advanced_logging)
                    {
                        Rocket.Core.Logging.Logger.Log("Executing " + pair.Key.Replace("(player)", player.DisplayName).Replace("(steamid)", player.CSteamID + ""));
                    }

                    if (!cheats)
                    {
                        Provider.hasCheats = true;
                    }

                    bool success = R.Commands.Execute((IRocketPlayer)UnturnedPlayer.FromCSteamID(Executor_ID), pair.Key.Replace("(player)", player.DisplayName).Replace("(steamid)", player.CSteamID + ""));

                    if (!cheats)
                    {
                        Provider.hasCheats = false;
                    }

                    if (!success)
                    {
                        Rocket.Core.Logging.Logger.LogError("Failed to execute command " + pair.Key + " while trying to give reward to " + player.DisplayName);
                    }
                }

                Check_Cooldown(player);
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
            }
        }

        public void GiveReward_Sequential(UnturnedPlayer player)
        {
            try
            {
                List<string> Items = new List<string>();
                foreach (var pair in Reward_dictionary)
                {
                    for (int i = 0; i < pair.Value; i++)
                    {
                        Items.Add(pair.Key);
                    }
                }

                int sequence_number = 0;

                foreach (var pair in Sequence)
                {
                    if (pair.Key == player.CSteamID)
                    {
                        CSteamID user = pair.Key;
                        sequence_number = pair.Value;
                    }
                }

                if (advanced_logging)
                {
                    Rocket.Core.Logging.Logger.Log("Executing " + Items[sequence_number].Replace("(player)", player.DisplayName).Replace("(steamid)", player.CSteamID + ""));
                }

                if (!cheats)
                {
                    Provider.hasCheats = true;
                }

                bool success = R.Commands.Execute((IRocketPlayer)UnturnedPlayer.FromCSteamID(Executor_ID), Items[sequence_number].Replace("(player)", player.DisplayName).Replace("(steamid)", player.CSteamID + ""));

                if (!cheats)
                {
                    Provider.hasCheats = false;
                }

                if (!success)
                {
                    Rocket.Core.Logging.Logger.LogError("Failed to execute command " + Items[sequence_number].Replace("(player)", player.DisplayName).Replace("(steamid)", player.CSteamID + "") + " while trying to give reward to " + player.DisplayName);
                }

                if (sequence_number >= Reward_dictionary.Keys.Count - 1)
                {
                    Sequence[player.CSteamID] = 0;
                }
                else
                {
                    Sequence[player.CSteamID] = sequence_number + 1;
                }

                Check_Cooldown(player);
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
            }
        }

        public void GiveReward_Weighted(UnturnedPlayer player)
        {
            try
            {
                List<string> Rnd_Items = new List<string>();
                foreach (var pair in Reward_dictionary)
                {
                    for (int i = 0; i < pair.Value; i++)
                    {
                        Rnd_Items.Add(pair.Key);
                    }
                }

                System.Random rnd = new System.Random();
                int r = rnd.Next(Rnd_Items.Count);

                if (advanced_logging)
                {
                    Rocket.Core.Logging.Logger.Log("Executing " + Rnd_Items[r].Replace("(player)", player.DisplayName).Replace("(steamid)", player.CSteamID + ""));
                }

                if (!cheats)
                {
                    Provider.hasCheats = true;
                }

                bool success = R.Commands.Execute((IRocketPlayer)UnturnedPlayer.FromCSteamID(Executor_ID), Rnd_Items[r].Replace("(player)", player.DisplayName).Replace("(steamid)", player.CSteamID + ""));

                if (!cheats)
                {
                    Provider.hasCheats = false;
                }

                if (!success)
                {
                    Rocket.Core.Logging.Logger.LogError("Failed to execute command " + Rnd_Items[r] + " while trying to give reward to " + player.DisplayName);
                }

                Check_Cooldown(player);
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
            }
        }

        public void GiveReward_Random(UnturnedPlayer player)
        {
            try
            {
                List<string> Rnd_Items = new List<string>();
                foreach (var pair in Reward_dictionary)
                {
                    Rnd_Items.Add(pair.Key);
                }

                System.Random rnd = new System.Random();
                int r = rnd.Next(Rnd_Items.Count);

                if (advanced_logging)
                {
                    Rocket.Core.Logging.Logger.Log("Executing " + Rnd_Items[r].Replace("(player)", player.DisplayName).Replace("(steamid)", player.CSteamID + ""));
                }

                if (!cheats)
                {
                    Provider.hasCheats = true;
                }

                bool success = R.Commands.Execute((IRocketPlayer)UnturnedPlayer.FromCSteamID(Executor_ID), Rnd_Items[r].Replace("(player)", player.DisplayName).Replace("(steamid)", player.CSteamID + ""));

                if (!cheats)
                {
                    Provider.hasCheats = false;
                }

                if (!success)
                {
                    Rocket.Core.Logging.Logger.LogError("Failed to execute command " + Rnd_Items[r] + " while trying to give reward to " + player.DisplayName);
                }

                Check_Cooldown(player);
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
            }
        }

        public void Check_Cooldown(UnturnedPlayer player)
        {
            try
            {
                int done_ads = 0;

                if (Ad_Views.ContainsKey(player.CSteamID))
                {
                    done_ads = Ad_Views[player.CSteamID];
                }
                else
                {
                    Ad_Views[player.CSteamID] = 0;
                }


                if (global_messages == false)
                {
                    if (ads_before_cooldown == 1 || ads_before_cooldown - done_ads == 1)
                    {
                        messages_to_say.Add(new SayMessage(player, setTranslationParams(Translation_dictionary["EVENT_RECEIVED_REWARD_COOLDOWN"].Key, Configuration.Instance.CooldownTime), Translation_dictionary["EVENT_RECEIVED_REWARD_COOLDOWN"].Value));

                        if (Configuration.Instance.CooldownTime != 0)
                        {
                            var CooldownTime = CurrentTime.Millis + (Configuration.Instance.CooldownTime * 60 * 1000);
                            Cooldown[player.CSteamID] = CooldownTime;
                        }
                    }
                    else
                    {
                        int remaining_ads = ads_before_cooldown - done_ads;
                        messages_to_say.Add(new SayMessage(player, setTranslationParams(Translation_dictionary["EVENT_RECEIVED_REWARD_ADS_REMAIN"].Key, remaining_ads), Translation_dictionary["EVENT_RECEIVED_REWARD_ADS_REMAIN"].Value));

                        if (Configuration.Instance.CooldownTime != 0)
                        {
                            Ad_Views[player.CSteamID] = done_ads + 1;
                        }
                    }
                }
                else
                {
                    if (ads_before_cooldown == 1 || ads_before_cooldown - done_ads == 1)
                    {
                        messages_to_say.Add(new SayMessage(setTranslationParams(Translation_dictionary["EVENT_RECEIVED_REWARD_ADS_GLOBAL"].Key, player.DisplayName), Translation_dictionary["EVENT_RECEIVED_REWARD_ADS_GLOBAL"].Value));

                        if (Configuration.Instance.CooldownTime != 0)
                        {
                            var CooldownTime = CurrentTime.Millis + (Configuration.Instance.CooldownTime * 60 * 1000);
                            Cooldown[player.CSteamID] = CooldownTime;
                        }
                    }
                    else
                    {
                        messages_to_say.Add(new SayMessage(setTranslationParams(Translation_dictionary["EVENT_RECEIVED_REWARD_ADS_GLOBAL"].Key, player.DisplayName), Translation_dictionary["EVENT_RECEIVED_REWARD_ADS_GLOBAL"].Value));

                        if (Configuration.Instance.CooldownTime != 0)
                        {
                            Ad_Views[player.CSteamID] = done_ads + 1;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
            }
        }

        /*private IEnumerator cooldownCheckerFunc()
        {
            while (true)
            {
                if (Provider.isServer)
                {
                    foreach (UnturnedPlayer player in Provider.players)
                    {
                        try
                        {
                            int done_ads = 0;

                            if (Ad_Views.ContainsKey(player.CSteamID))
                            {
                                done_ads = Ad_Views[player.CSteamID];
                            }
                            else
                            {
                                Ad_Views[player.CSteamID] = 0;
                            }


                            if (global_messages == false)
                            {
                                if (ads_before_cooldown == 1 || ads_before_cooldown - done_ads == 1)
                                {
                                    messages_to_say.Add(new SayMessage(player, setTranslationParams(Translation_dictionary["EVENT_RECEIVED_REWARD_COOLDOWN"].Key, Configuration.Instance.CooldownTime), Translation_dictionary["EVENT_RECEIVED_REWARD_COOLDOWN"].Value));

                                    if (Configuration.Instance.CooldownTime != 0)
                                    {
                                        var CooldownTime = CurrentTime.Millis + (Configuration.Instance.CooldownTime * 60 * 1000);
                                        Cooldown[player.CSteamID] = CooldownTime;
                                    }
                                }
                                else
                                {
                                    int remaining_ads = ads_before_cooldown - done_ads;
                                    messages_to_say.Add(new SayMessage(player, setTranslationParams(Translation_dictionary["EVENT_RECEIVED_REWARD_ADS_REMAIN"].Key, remaining_ads), Translation_dictionary["EVENT_RECEIVED_REWARD_ADS_REMAIN"].Value));

                                    if (Configuration.Instance.CooldownTime != 0)
                                    {
                                        Ad_Views[player.CSteamID] = done_ads + 1;
                                    }
                                }
                            }
                            else
                            {
                                if (ads_before_cooldown == 1 || ads_before_cooldown - done_ads == 1)
                                {
                                    messages_to_say.Add(new SayMessage(setTranslationParams(Translation_dictionary["EVENT_RECEIVED_REWARD_ADS_GLOBAL"].Key, player.DisplayName), Translation_dictionary["EVENT_RECEIVED_REWARD_ADS_GLOBAL"].Value));

                                    if (Configuration.Instance.CooldownTime != 0)
                                    {
                                        var CooldownTime = CurrentTime.Millis + (Configuration.Instance.CooldownTime * 60 * 1000);
                                        Cooldown[player.CSteamID] = CooldownTime;
                                    }
                                }
                                else
                                {
                                    messages_to_say.Add(new SayMessage(setTranslationParams(Translation_dictionary["EVENT_RECEIVED_REWARD_ADS_GLOBAL"].Key, player.DisplayName), Translation_dictionary["EVENT_RECEIVED_REWARD_ADS_GLOBAL"].Value));

                                    if (Configuration.Instance.CooldownTime != 0)
                                    {
                                        Ad_Views[player.CSteamID] = done_ads + 1;
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Rocket.Core.Logging.Logger.LogException(e);
                        }
                    }
                    check_cooldown_list.Clear();
                }
                yield return new WaitForSeconds(2f);
            }
        }*/

        private void timerFunc(object sender, EventArgs e)
        {
            RemoveCooldownLoop();
            CheckRewardAvailability();
        }

        /*private IEnumerator timerFunc()
        {
            while (true)
            {
                if (Provider.isServer)
                {
                    RemoveCooldownLoop();
                    CheckRewardAvailability();
                }
                yield return new WaitForSeconds(2f);
            }
        }*/

        private void reminderFunc(object sender, EventArgs e)
        {
            try
            {
                if (Configuration.Instance.Join_Commands.Count != 0 && reapply_join)
                {
                    foreach (SteamPlayer steamplayer in Provider.clients)
                    {
                        UnturnedPlayer player = UnturnedPlayer.FromSteamPlayer(steamplayer);
                        if (!OnCooldown(player) && !player.HasPermission("motdgd.immune"))
                        {
                            foreach (string command in Configuration.Instance.Join_Commands)
                            {
                                if (!cheats)
                                {
                                    Provider.hasCheats = true;
                                }

                                bool success = R.Commands.Execute((IRocketPlayer)UnturnedPlayer.FromCSteamID(Executor_ID), command.Replace("(player)", player.DisplayName).Replace("(steamid)", player.CSteamID + ""));

                                if (!cheats)
                                {
                                    Provider.hasCheats = false;
                                }

                                if (!success)
                                {
                                    Rocket.Core.Logging.Logger.LogError("Failed to execute command " + command.Replace("(player)", player.DisplayName).Replace("(steamid)", player.CSteamID + "") + " while trying to give reward to " + player.DisplayName);
                                }
                            }
                            messages_to_say.Add(new SayMessage(player, Translation_dictionary["REMINDER_MESSAGE_JOIN"].Key, Translation_dictionary["REMINDER_MESSAGE_JOIN"].Value));
                        }
                    }
                }
                else
                {
                    foreach (SteamPlayer steam_player in Provider.clients)
                    {
                        UnturnedPlayer player = UnturnedPlayer.FromSteamPlayer(steam_player);
                        if (!OnCooldown(player) && !player.HasPermission("motdgd.immune"))
                        {
                            messages_to_say.Add(new SayMessage(player, Translation_dictionary["REMINDER_MESSAGE"].Key, Translation_dictionary["REMINDER_MESSAGE"].Value));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Rocket.Core.Logging.Logger.LogException(ex);
            }
        }


        /*private IEnumerator reminderFunc()
        {
            while (true)
            {
                if (Provider.isServer)
                {
                    try
                    {
                        if (Configuration.Instance.Join_Commands.Count != 0 && reapply_join)
                        {
                            foreach (SteamPlayer steamplayer in Provider.clients)
                            {
                                UnturnedPlayer player = UnturnedPlayer.FromSteamPlayer(steamplayer);
                                if (!OnCooldown(player) && !player.HasPermission("motdgd.immune"))
                                {
                                    foreach (string command in Configuration.Instance.Join_Commands)
                                    {
                                        if (!cheats)
                                        {
                                            Provider.hasCheats = true;
                                        }

                                        bool success = R.Commands.Execute((IRocketPlayer)UnturnedPlayer.FromCSteamID(Executor_ID), command.Replace("(player)", player.DisplayName).Replace("(steamid)", player.CSteamID + ""));

                                        if (!cheats)
                                        {
                                            Provider.hasCheats = false;
                                        }

                                        if (!success)
                                        {
                                            Rocket.Core.Logging.Logger.LogError("Failed to execute command " + command.Replace("(player)", player.DisplayName).Replace("(steamid)", player.CSteamID + "") + " while trying to give reward to " + player.DisplayName);
                                        }
                                    }
                                    messages_to_say.Add(new SayMessage()(player, Translation_dictionary["REMINDER_MESSAGE_JOIN"].Key, Translation_dictionary["REMINDER_MESSAGE_JOIN"].Value);
                                }
                            }
                        }
                        else
                        {
                            foreach (SteamPlayer steam_player in Provider.clients)
                            {
                                UnturnedPlayer player = UnturnedPlayer.FromSteamPlayer(steam_player);
                                if (!OnCooldown(player) && !player.HasPermission("motdgd.immune"))
                                {
                                    messages_to_say.Add(new SayMessage()(player, Translation_dictionary["REMINDER_MESSAGE"].Key, Translation_dictionary["REMINDER_MESSAGE"].Value);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Rocket.Core.Logging.Logger.LogException(ex);
                    }
                }
                yield return new WaitForSeconds(2f);
            }
        }*/

        //Loop checking cooldown list and removing players after cooldown expiry 
        public void RemoveCooldownLoop()
        {
            try
            {
                List<CSteamID> keys = new List<CSteamID>(Cooldown.Keys);
                foreach (var key in keys)
                {
                    var value = Cooldown[key];
                    var currentTime = CurrentTime.Millis;

                    if (value <= currentTime)
                    {
                        Cooldown.Remove(key);
                        Ad_Views[key] = 0;
                        
                        if (Provider.clients.Contains(PlayerTool.getSteamPlayer(key)))
                        {
                            UnturnedPlayer player = UnturnedPlayer.FromCSteamID(key);
                            messages_to_say.Add(new SayMessage(player, Translation_dictionary["COOLDOWN_EXPIRED"].Key, Translation_dictionary["COOLDOWN_EXPIRED"].Value));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
            }
        }

        //Find if in Cooldown
        public static bool OnCooldown(UnturnedPlayer player)
        {
            if (Cooldown.ContainsKey(player.CSteamID))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        //Return time in Millis since 1.1.1970
        static class CurrentTime
        {
            private static readonly DateTime Jan1St1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            public static long Millis { get { return (long)((DateTime.UtcNow - Jan1St1970).TotalMilliseconds); } }
        }

        //Return cooldown time
        public static string CooldownTime(UnturnedPlayer player)
        {
            try
            {
                foreach (var pair in Cooldown)
                {
                    var key = pair.Key;
                    var value = pair.Value;
                    var currentTime = CurrentTime.Millis;

                    if (key == player.CSteamID)
                    {
                        var milTime = value - currentTime;
                        double time = milTime / 1000;

                        var minutes = Math.Truncate(time / 60);
                        var seconds = time - (minutes * 60);

                        return minutes + " minutes and " + seconds + " seconds";
                    };
                }
                return "";
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
                return null;
            }
        }

        public static void request_link(UnturnedPlayer player) {
            try
            {
                socket.Emit("link", new object[] { player.CSteamID + "", GetIP(player) });
                if (advanced_logging)
                {
                    Rocket.Core.Logging.Logger.Log("Requesting ad link for player " + player.DisplayName + " with CSteamID " + player.CSteamID + " and IP " + GetIP(player));
                }
                if (!Awaiting_command.ContainsKey(player.CSteamID))
                {
                    Awaiting_command[player.CSteamID] = 0;
                }
                Request_players.Add(player.CSteamID);
                messages_to_say.Add(new SayMessage(player, Translation_dictionary["REQUEST_LINK_MESSAGE"].Key, Translation_dictionary["REQUEST_LINK_MESSAGE"].Value));
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
            }
        }

        private bool parseConfig()
        {
            try
            {
                advanced_logging = Configuration.Instance.AdvancedLogging;
                if (advanced_logging)
                {
                    Rocket.Core.Logging.Logger.Log("*****************************");
                    Rocket.Core.Logging.Logger.Log("*                           *");
                    Rocket.Core.Logging.Logger.Log("*   Parsing MOTDGD config   *");
                    Rocket.Core.Logging.Logger.Log("*                           *");
                    Rocket.Core.Logging.Logger.Log("*****************************");
                    Rocket.Core.Logging.Logger.Log("");
                    Rocket.Core.Logging.Logger.Log("*******************");
                    Rocket.Core.Logging.Logger.Log("* Loading rewards *");
                    Rocket.Core.Logging.Logger.Log("*******************");
                    Rocket.Core.Logging.Logger.Log("");
                }    
            /*
             * Parsing rewards to dictionary
             */
                for (int lastIndex = 0; lastIndex < Configuration.Instance.Rewards.Length; lastIndex++)
                {
                    MOTDgd.MOTDgdConfiguration.Reward reward = Configuration.Instance.Rewards[lastIndex];
                    string command = reward.Command;
                    int probability = reward.Probability;

                    Reward_dictionary[command] = probability;

                    if (advanced_logging)
                    {
                        Rocket.Core.Logging.Logger.Log("Loaded reward " + command + " with probability " + probability);
                    }
                }

                /*
                 * Parse translations from the config
                 */

                if (advanced_logging)
                {
                    Rocket.Core.Logging.Logger.Log("");
                    Rocket.Core.Logging.Logger.Log("************************");
                    Rocket.Core.Logging.Logger.Log("* Loading translations *");
                    Rocket.Core.Logging.Logger.Log("************************");
                    Rocket.Core.Logging.Logger.Log("");
                }
                for (int lastIndex = 0; lastIndex < Main.Instance.Configuration.Instance.Translations.Length; lastIndex++) 
                {
                    MOTDgd.MOTDgdConfiguration.Translation translation_object = Main.Instance.Configuration.Instance.Translations[lastIndex]; //get current translation
                    Color color = UnturnedChat.GetColorFromName(translation_object.Color, Color.green);
                    string translation = translation_object.Text; //Get translation from config
                    string identifier = translation_object.Identifier;

                    Translation_dictionary[identifier] = new KeyValuePair<string, Color>(translation, color);

                    if(advanced_logging)
                    {
                        Rocket.Core.Logging.Logger.Log("Loaded translation " + identifier + " with value " + translation + " and color " + translation_object.Color);
                    }
                }

                /* 
                 * Custom executor CSteamID 
                 */
                UInt64 int_result;
                if (Configuration.Instance.Executor_CSteamID != null && UInt64.TryParse(Configuration.Instance.Executor_CSteamID, out int_result))
                {
                    Executor_ID = (CSteamID)int_result;
                }

                /*
                 *  Checking MOTD ID
                 */

                if (Configuration.Instance.User_ID == 0)
                {
                    Rocket.Core.Logging.Logger.LogError("MOTD ID not set! Unloading plugin now!");
                    this.Unload();
                    this.UnloadPlugin(PluginState.Failure);
                    return false;
                }

                /*
                 * Checking reward mode
                 */

                reward_mode = Configuration.Instance.Reward_mode.ToLower().Replace(" ", "");

                if (reward_mode != "all" && reward_mode != "weighted" && reward_mode != "random" && reward_mode != "sequential")
                {
                    Rocket.Core.Logging.Logger.LogError("Reward mode is not set correctly! Unloading plugin now!");
                    this.Unload();
                    this.UnloadPlugin(PluginState.Failure);
                    return false;
                }

                /*
                 * Setting up rewards before cooldown
                 */

                if (Configuration.Instance.Number_of_ads_before_cooldown == 0)
                {
                    ads_before_cooldown = 1;
                }
                else
                {
                    ads_before_cooldown = Configuration.Instance.Number_of_ads_before_cooldown;
                }

                /*
                 * Other config parsing
                 */
                reminder_delay = Configuration.Instance.Reminder_delay;
                global_messages = Configuration.Instance.Global_messages;
                Ad_on_join = Configuration.Instance.Ad_on_join;
                reapply_join = Configuration.Instance.Reapply_join_command;
                vid_unavailable = Configuration.Instance.Give_reward_when_video_unavailable;
                if (advanced_logging)
                {
                    Rocket.Core.Logging.Logger.Log("");
                    Rocket.Core.Logging.Logger.Log("***********************");
                    Rocket.Core.Logging.Logger.Log("* Loaded successfully *");
                    Rocket.Core.Logging.Logger.Log("***********************");
                    Rocket.Core.Logging.Logger.Log("");
                }
                return true;
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
                return false;
            }
        }
        
        public static string setTranslationParams(string translation, params object[] parameters) {
            try
            {
                if (translation.Contains("{") && translation.Contains("}") && parameters != null && (int)parameters.Length != 0) //Replace parameters e.g. {0}
                {
                    for (int i = 0; i < (int)parameters.Length; i++)
                    {
                        if (parameters[i] == null)
                        {
                            parameters[i] = "NULL"; //Change null parameter to string
                        }
                    }
                    translation = string.Format(translation, parameters); //Format the string
                }
                return translation; //And return
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
                return string.Empty;
            }
        }

        private static string GetIP(UnturnedPlayer player)
        {
            try
            {
                uint mNRemoteIP;
                P2PSessionState_t p2PSessionStateT;
                CSteamID pid = player.CSteamID;

                if (!SteamGameServerNetworking.GetP2PSessionState(pid, out p2PSessionStateT))
                {
                    mNRemoteIP = 0;
                }
                else
                {
                    mNRemoteIP = p2PSessionStateT.m_nRemoteIP;
                }

                return Parser.getIPFromUInt32(mNRemoteIP);
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
                return null;
            }
        }

        private static void CheckRewardAvailability() {
            try
            {
                List<CSteamID> keys = new List<CSteamID>(Awaiting_command.Keys);
                foreach (var key in keys)
                {
                    int val = Awaiting_command[key];
                    if (val >= 30)
                    {
                        UnturnedPlayer plr = UnturnedPlayer.FromCSteamID(key);
                        messages_to_say.Add(new SayMessage(plr, "We are sorry, but we couldn't get add for you. Contact server owner for more information."));
                        Request_players.Remove(key);

                        Awaiting_command.Remove(key);
                    }
                    else
                    {
                        Awaiting_command[key] = val + 2;
                    }
                }
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogException(e);
            }
        }

    }
}