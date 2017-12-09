﻿using Discord.Commands;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using Discord;
using System;
using System.Collections.Generic;
using Discord.Rest;

namespace BotHATTwaffle.Modules
{
    public class LevelTesting
    {
        RestUserMessage announceMessage = null;
        public GoogleCalendar _googleCalendar;
        string[] lastEventInfo;
        string[] currentEventInfo;
        Boolean alertedHour = false;
        Boolean alertedStart = false;
        int calUpdateTicks = 2;
        int caltick = 0;

        public LevelTesting()
        {
            if ((Program.config.ContainsKey("calUpdateTicks") && !int.TryParse(Program.config["calUpdateTicks"], out calUpdateTicks)))
            {
                Console.WriteLine($"Key \"calUpdateTicks\" not found or valid. Using default {calUpdateTicks}.");
            }
            calUpdateTicks = calUpdateTicks - 1; //This is so the if statement does not add 1.
            _googleCalendar = new GoogleCalendar();
            currentEventInfo = _googleCalendar.GetEvents(); //Initial get of playtest.
            lastEventInfo = currentEventInfo; //Make sure array is same size for doing compares later.
        }

        public async Task Announce()
        {
            caltick++;
            if (calUpdateTicks < caltick)
            {
                caltick = 0;
                currentEventInfo = _googleCalendar.GetEvents();

                if (announceMessage == null) //No current message.
                {
                    await PostAnnounce(FormatPlaytestInformationAsync(currentEventInfo, false));
                }
                else if (currentEventInfo[2] == lastEventInfo[2]) //Title is same. 
                {
                    await UpdateAnnounce(FormatPlaytestInformationAsync(currentEventInfo, false));
                }
                else //Title is different, scrub and rebuild
                {
                    await RebuildAnnounce();
                }
            }

        }

        private async Task PostAnnounce(Embed embed)
        {
            announceMessage = await Program.announcementChannel.SendMessageAsync("",false,embed);
            lastEventInfo = currentEventInfo;
        }

        private async Task UpdateAnnounce(Embed embed)
        {
            await announceMessage.ModifyAsync(x =>
            {
                x.Content = "";
                x.Embed = embed;
            });
            lastEventInfo = currentEventInfo;
        }

        private async Task RebuildAnnounce()
        {
            await Program.ChannelLog("Scrubbing Announcement","Playtest is different from the last one. This is probably because" +
                "the last playtest is past. Let's tear it down and get the next test.");
            await announceMessage.DeleteAsync();
            announceMessage = null;
            lastEventInfo = currentEventInfo;

            //Reset announcement flags.
            alertedHour = false;
            alertedStart = false;

            await Announce();
        }

        public Embed FormatPlaytestInformationAsync(string[] eventInfo, Boolean userCall)
        {
            //0 EVENT HEADER. "BEGIN_EVENT" or "NO_EVENT_FOUND"
            //1 Time-
            //2 Title-
            //3 Creator-
            //4 Featured Image-
            //5 Map Images-
            //6 Workshop Link-
            //7 Game Mode-
            //8 Moderator-
            //9 Description-
            //10 Location-

            var builder = new EmbedBuilder();
            var authBuilder = new EmbedAuthorBuilder();
            List<EmbedFieldBuilder> fieldBuilder = new List<EmbedFieldBuilder>();
            var footBuilder = new EmbedFooterBuilder();

            if (eventInfo[0].Equals("BEGIN_EVENT"))
            {
                DateTime time = Convert.ToDateTime(eventInfo[1]);
                string timeStr = time.ToString("MMMM ddd d, HH:mm");
                TimeSpan timeLeft = time.Subtract(DateTime.Now);
                string timeLeftStr = null;
                DateTime utcTime = time.ToUniversalTime();

                //Timezones!
                string est = TimeZoneInfo.ConvertTimeFromUtc(utcTime, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")).ToString("ddd HH:mm");
                string pst = TimeZoneInfo.ConvertTimeFromUtc(utcTime, TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time")).ToString("ddd HH:mm");
                string gmt = TimeZoneInfo.ConvertTimeFromUtc(utcTime, TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time")).ToString("ddd HH:mm");
                //Screw Australia. 
                //string gmt8 = TimeZoneInfo.ConvertTimeFromUtc(utcTime, TimeZoneInfo.FindSystemTimeZoneById("W. Australia Standard Time")).ToString("ddd HH:mm");

                string postTime = $"{timeStr} CT | {est} EST | {pst} PST | {gmt} GMT"; // | {gmt8} GMT+8";

                //Check if we need to adjust the time until for after a test starts.
                if (time.CompareTo(DateTime.Now) < 0)
                {
                    timeLeftStr = $"Started: {timeLeft.ToString("d'D 'h'H 'm'M'").TrimStart(' ', 'D', 'H', '0')} ago!";
                    if(!userCall && !alertedStart) //Prevents user calls for upcoming from sending alert message.
                    {
                        alertedStart = true;
                        Program.testingChannel.SendMessageAsync("", false, FormatPlaytestInformationAsync(currentEventInfo, false));
                        Program.testingChannel.SendMessageAsync($"{Program.playTesterRole.Mention}" +
                            $"\n**Playtest starting now!** `connect {eventInfo[10]}`");
                    }
                }
                else
                {
                    timeLeftStr = timeLeft.ToString("d'D 'h'H 'm'M'").TrimStart(' ', 'D', 'H', '0');
                }

                //Let's check if we should be announcing a playtest. Easier to do it here since the variables are already computed.
                TimeSpan singleHour = new TimeSpan(1, 0, 0);
                DateTime adjusted = DateTime.Now.Add(singleHour);
                int timeCompare = DateTime.Compare(adjusted, time);
                if (timeCompare > 0 && !alertedHour)
                {
                    alertedHour = true;
                    Program.testingChannel.SendMessageAsync($"{Program.playTesterRole.Mention}" +
                            $"\n**Playtest starting in 1 hour**");
                    Program.testingChannel.SendMessageAsync("", false, FormatPlaytestInformationAsync(currentEventInfo, false));
                }


                authBuilder = new EmbedAuthorBuilder()
                {
                    Name = eventInfo[2],
                    IconUrl = "https://cdn.discordapp.com/icons/111951182947258368/0e82dec99052c22abfbe989ece074cf5.png"
                };


                fieldBuilder.Add(new EmbedFieldBuilder { Name = "Time Until Test", Value = timeLeftStr, IsInline = true });
                fieldBuilder.Add(new EmbedFieldBuilder { Name = "Creator", Value = eventInfo[3], IsInline = true });
                fieldBuilder.Add(new EmbedFieldBuilder { Name = "Where?", Value = eventInfo[10], IsInline = true });
                fieldBuilder.Add(new EmbedFieldBuilder { Name = "Moderator", Value = eventInfo[8], IsInline = true });
                fieldBuilder.Add(new EmbedFieldBuilder { Name = "More Images", Value = eventInfo[5], IsInline = false });
                fieldBuilder.Add(new EmbedFieldBuilder { Name = "When?", Value = postTime, IsInline = false });

                footBuilder = new EmbedFooterBuilder()
                {
                    Text = "https://www.tophattwaffle.com/playtesting/",
                    IconUrl = Program._client.CurrentUser.GetAvatarUrl()
                };

                builder = new EmbedBuilder()
                {
                    Author = authBuilder,
                    Footer = footBuilder,
                    Fields = fieldBuilder,

                    Title = $"--Workshop Link--",
                    Url = eventInfo[6],
                    ImageUrl = eventInfo[4],
                    ThumbnailUrl = "https://www.tophattwaffle.com/wp-content/uploads/2017/11/1024_png-300x300.png",
                    Color = new Color(71, 126, 159),

                    Description = eventInfo[9]
                };
            }
            else
            {
                string announceDiag = null;
                if (eventInfo[0].Equals("BAD_DESCRIPTION"))
                    announceDiag = "\n\n\nThere was an issue with the Google Calendar event. Someone tell TopHATTwaffle..." +
                        "If you're seeing this, that means there is probably a test scheduled, but the description contains " +
                        "HTML code so I cannot properly parse it. ReeeeeeEEEeeE";

                //Program.ChannelLog($"No playtest was found. Posting default message.");
                authBuilder = new EmbedAuthorBuilder()
                {
                    Name = "No Playtests Found!",
                    IconUrl = "https://cdn.discordapp.com/icons/111951182947258368/0e82dec99052c22abfbe989ece074cf5.png"
                };

                footBuilder = new EmbedFooterBuilder()
                {
                    Text = "https://www.tophattwaffle.com/playtesting/",
                    IconUrl = Program._client.CurrentUser.GetAvatarUrl()
                };

                builder = new EmbedBuilder()
                {
                    Author = authBuilder,
                    Footer = footBuilder,

                    Title = $"Click here to schedule your playtest!",
                    Url = "https://www.tophattwaffle.com/playtesting/",
                    ImageUrl = "https://www.tophattwaffle.com/wp-content/uploads/2017/11/header.png",
                    //ThumbnailUrl = "https://www.tophattwaffle.com/wp-content/uploads/2017/11/1024_png-300x300.png",
                    Color = new Color(214, 91, 47),

                    Description = $"Believe it or not, there aren't any tests scheduled. Click the link above to schedule your own playtest!**{announceDiag}**"
                };
            }
            return builder.Build();
        }
    }

    public class LevelTestingModule : ModuleBase<SocketCommandContext>
    {
        private readonly LevelTesting _levelTesting;

        public LevelTestingModule(LevelTesting levelTesting)
        {
            _levelTesting = levelTesting;
        }

        [Command("playtester")]
        [Summary("`>playtester` Toggles your playtest notifications.")]
        [Remarks("Toggles your subscription to the playtester notification group.")]
        [Alias("pt")]
        public async Task PlaytesterAsync()
        {
            if (Context.IsPrivate)
            {
                await ReplyAsync("**This command can not be used in a DM**");
                return;
            }
            var user = Context.User as SocketGuildUser;

            string playtesterRoleStr = null;
            if (Program.config.ContainsKey("playTesterRole"))
                playtesterRoleStr = (Program.config["playTesterRole"]);

            var playtesterRole = Context.Guild.Roles.FirstOrDefault(x => x.Name == playtesterRoleStr);

            if (user.Roles.Contains(playtesterRole))
            {
                await Program.ChannelLog($"{Context.User} has unsubscribed from playtest notifications!");
                await ReplyAsync($"Sorry to see you go from playtest notifications {Context.User.Mention}!");
                await (user as IGuildUser).RemoveRoleAsync(playtesterRole);
            }
            else
            {
                await Program.ChannelLog($"{Context.User} has subscribed from playtest notifications!");
                await ReplyAsync($"Thanks for subscribing to playtest notifications {Context.User.Mention}!");
                await (user as IGuildUser).AddRoleAsync(playtesterRole);
            }
        }

        [Command("upcoming")]
        [Summary("`>upcoming` Shows you the next playtest")]
        [Remarks("Automatically looks up the next playtest for you. You can always just look in the announcement channel")]
        [Alias("up")]
        public async Task UpcomingAsync()
        {
            await ReplyAsync("", false, _levelTesting.FormatPlaytestInformationAsync(_levelTesting._googleCalendar.GetEvents(), true));
        }
    }
}