using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace WowheadDigest {
	class Program {
		static readonly Logger log = new Logger();
		static DiscordClient discord = null;
		static string str_mention = null;

		static HashSet<Article> articles =
			new HashSet<Article>();
		static Dictionary<DiscordGuild, GuildData> guildData =
			new Dictionary<DiscordGuild, GuildData>();

		const string path_token    = @"token.txt";
		const string path_articles = @"articles.txt";
		const string path_settings = @"settings.txt";
		const string path_digests  = @"digests.txt";

		const ulong id_u_self = 779053723489140759;
		const ulong id_wh_wowhead = 779070312561770528;
		const ulong id_ch_ingest = 777935219193020426;  // <Erythro> - #ingest
#if DEBUG
		const ulong id_ch_debug = 489274692255875091;   // <Erythro> - #test
#endif

		public static ref readonly Logger GetLogger() { return ref log; }

		static void Main() {
			const string title_ascii =
				" █   █ ▄▀▄ █   █ █▄█ ██▀ ▄▀▄ █▀▄   █▀▄ █ ▄▀  ██▀ ▄▀▀ ▀█▀ " + "\n" +
				" ▀▄▀▄▀ ▀▄▀ ▀▄▀▄▀ █ █ █▄▄ █▀█ █▄▀   █▄▀ █ ▀▄█ █▄▄ ▄██  █  " + "\n";
			Console.WriteLine(title_ascii);
			log.show_timestamp = true;
			log.type_minimum = Logger.Type.Debug;
			MainAsync().ConfigureAwait(false).GetAwaiter().GetResult();
		}

		static async Task MainAsync() {
			log.Info("Initializing...");
			Connect();
			if (discord == null) {
				log.Error("Terminating program.");
				return;
			}

			discord.Ready += async (s, e) => {
				log.Info("Connected to discord.");

				str_mention = (await discord.GetUserAsync(id_u_self)).Mention;
				log.Debug("  Self mention string: " + str_mention);

				log.Info("Loading article database...");
				StreamReader reader = new StreamReader(path_articles);
				while (reader.Peek() != -1) {
					string line = reader.ReadLine();
					if (line == "")
						continue;
					Article article = Article.FromString(line);
					articles.Add(article);
				}
				log.Info("Article database loaded.");
				log.Debug("  " + articles.Count.ToString() + " item(s) loaded.");
			};

			discord.GuildDownloadCompleted += async (s, e) => {
				log.Info("Guild streaming completed.");

				// load guild settings
				log.Info("Loading guild settings...");
				Dictionary<DiscordGuild, string> data_settings =
					new Dictionary<DiscordGuild, string>();
				using (StreamReader reader = new StreamReader(path_settings)) {
					while (reader.Peek() != -1) {
						string line = reader.ReadLine();
						if (line == "")
							continue;
						if (line.StartsWith("GUILD - ")) {
							string guild_str = Regex.Match(line, @"^GUILD - (\d+)$").Groups[1].Value;
							DiscordGuild guild =
								await discord.GetGuildAsync(Convert.ToUInt64(guild_str));

							string settings = "";
							while (reader.Peek() != -1) {
								line = reader.ReadLine();
								if (line.StartsWith("\t")) {
									settings += line + "\n";
								} else {
									break;
								}
							}
							data_settings.Add(guild, settings);
						}
					}
				}
				log.Info("Guild settings loaded.");

				// load guild digests
				log.Info("Loading guilds' saved digests...");
				Dictionary<DiscordGuild, string> data_digests =
					new Dictionary<DiscordGuild, string>();
				using (StreamReader reader = new StreamReader(path_digests)) {
					while (reader.Peek() != -1) {
						string line = reader.ReadLine();
						if (line == "")
							continue;
						if (line.StartsWith("GUILD - ")) {
							string guild_str = Regex.Match(line, @"^GUILD - (\d+)$").Groups[1].Value;
							DiscordGuild guild =
								await discord.GetGuildAsync(Convert.ToUInt64(guild_str));

							string digests = "";
							while (reader.Peek() != -1) {
								line = reader.ReadLine();
								if (line.StartsWith("\t")) {
									digests += line + "\n";
								} else {
									break;
								}
							}
							data_digests.Add(guild, digests);
						}
					}
				}
				log.Info("Saved digests loaded.");

				// TODO: handle exceptions
				// parse guild settings
				log.Info("Parsing guild settings...");
				foreach (DiscordGuild guild in data_settings.Keys) {
					GuildData guildData_i = new GuildData();
					guildData_i.settings =
						await Settings.Load(data_settings[guild], discord);
					guildData_i.digests = new List<Digest>();
					if (data_digests.ContainsKey(guild)) {
						await guildData_i.ImportDigests(data_digests[guild], discord);
					}
					guildData.Add(guild, guildData_i);
				}
				// add defaults settings if guild without saved settings is found
				foreach (DiscordGuild guild in discord.Guilds.Values) {
					if (!guildData.ContainsKey(guild)) {
						GuildData guildData_i = new GuildData();
						guildData_i.settings = Settings.Default();
						guildData_i.digests = new List<Digest>();
					}
				}
				log.Info("Guild settings parsed.");

				log.Info("Writing back loaded data:");
				Save();
			};

			discord.MessageCreated += async (s, e) => {
				if (e.Message.Content.StartsWith(str_mention)) {
					DiscordMember author = await e.Guild.GetMemberAsync(e.Message.Author.Id);
					if (!author.PermissionsIn(e.Channel).HasPermission(Permissions.ManageGuild)) {
						log.Warning("Command attempted (insufficient permissions).");
						log.Debug("  user: " + author.DisplayName);
						log.Debug("  cmd : " + e.Message.Content);
						return;
					}

					log.Info("Command detected.");
					log.Debug(e.Message.Content);
					string message = e.Message.Content.Substring(str_mention.Length + 1);
					string[] split = message.Split(' ', 2);
					string cmd, arg;
					if (message.Contains(' ')) {
						cmd = split[0].Trim();
						arg = split[1].TrimStart();
					} else {
						cmd = message.TrimStart();
						arg = "";
					}
					log.Debug("  cmd: " + cmd);
					log.Debug("  arg: " + arg);
					// TODO: make this synchronous...
					string reply = ParseCommand(cmd, arg, e);
					DiscordChannel ch_reply = e.Channel;
#if DEBUG
					ch_reply = await discord.GetChannelAsync(id_ch_debug);
#endif
					_ = discord.SendMessageAsync(ch_reply, reply);
				}

				// Must be webhook message & author must be Wowhead webhook.
				if (!e.Message.WebhookMessage || e.Message.WebhookId != id_wh_wowhead)
					return;
				// Must be posted to <Erythro> ingest channel.
				if (e.Channel.Id != id_ch_ingest)
					return;

				log.Info("Received Wowhead news.", 1);
				foreach (DiscordEmbed embed in e.Message.Embeds) {
					string url = embed.Url.AbsoluteUri;
					string id = Article.UrlToId(url);
					Article article = new Article(id, DateTime.Now);

					log.Info("New article posted!");

					//foreach (DiscordGuild guild in discord.Guilds.Values) {
					//	guildData[guild].Push(article);
					//}
				}
			};

			await discord.ConnectAsync();
			log.Info("Monitoring messages...");

			await Task.Delay(-1);
		}

		static void Connect() {
			log.Info("Reading authentication token...", 1);

			// Open text file.
			StreamReader file;
			try {
				file = new StreamReader(path_token);
			} catch (Exception) {
				log.Error("Could not open \"" + path_token + "\".", 1);
				log.Error("Cannot connect to Discord.", 1);
				return;
			}

			// Read text file.
			string token = file.ReadLine() ?? "";
			if (token != "") {
				log.Info("Authentication token found.", 1);
				int uncensor = 8;
				string token_censored =
					token.Substring(0, uncensor) +
					new string('*', token.Length - 2 * uncensor) +
					token.Substring(token.Length - uncensor);
				log.Debug("token: " + token_censored, 1);
			} else {
				log.Error("Authentication token missing!", 1);
				log.Error("Cannot connect to Discord.", 1);
				return;
			}
			file.Close();

			// Instantiate discord client.
			discord = new DiscordClient(new DiscordConfiguration {
				Token = token,
				TokenType = TokenType.Bot
			});
			log.Info("Connecting to discord...");
		}

		static void Save() {
			log.Info("Saving data...");
			StreamWriter writer_settings = new StreamWriter(path_settings);
			StreamWriter writer_digests = new StreamWriter(path_digests);
			foreach (DiscordGuild guild in guildData.Keys) {
				string guild_str = "GUILD - " + guild.Id.ToString();
				writer_settings.WriteLine(guild_str);
				writer_settings.Write(guildData[guild].settings.Save());
				writer_settings.WriteLine();
				writer_digests.WriteLine(guild_str);
				writer_digests.Write(guildData[guild].ExportDigests());
				writer_digests.WriteLine();
			}
			writer_settings.Close();
			writer_digests.Close();
			log.Info("Data saved.");
		}

		static string ParseCommand(string cmd, string arg, MessageCreateEventArgs e) {
			const string str_cmd_listSettings		= "list-settings";
			const string str_cmd_listCategories		= "list-categories";
			const string str_cmd_listSeries			= "list-series";
			const string str_cmd_listOverrides		= "list-overrides";
			const string str_cmd_setNews			= "set-news";
			const string str_cmd_setLogs			= "set-logs";
			const string str_cmd_setFreq			= "set-freq";
			const string str_cmd_setCensorSpoilers	= "set-censor-spoilers";
			const string str_cmd_setDetectSpoilers	= "set-detect-spoilers";
			const string str_cmd_filterCategory		= "filter-category";
			const string str_cmd_unfilterCategory	= "unfilter-category";
			const string str_cmd_filterSeries		= "filter-series";
			const string str_cmd_unfilterSeries		= "unfilter-series";
			const string str_cmd_hide		= "hide";
			const string str_cmd_show		= "show";
			const string str_cmd_spoiler	= "spoiler";
			const string str_cmd_unspoiler	= "unspoiler";

			string output = "";
			DiscordGuild guild = e.Guild;
			Settings settings = guildData[guild].settings;

			switch (cmd) {
			case str_cmd_listSettings:
				output = "**<" + guild.Name + "> Settings**" + "\n";
				output += "\u2023\u2002" + "News Channel: " +
					settings.ch_news.Mention + "\n";
				output += "\u2023\u2002" + "Logs Channel: " +
					settings.ch_logs.Mention + "\n";
				output += "\u2023\u2002" + "Posting digests ";
				switch (settings.postFrequency) {
				case Settings.PostFrequency.Daily:
					output += "*daily*." + "\n";
					break;
				case Settings.PostFrequency.Weekly:
					output += "*weekly*." + "\n";
					break;
				}
				output += "\u2023\u2002" + "Spoiler post titles are ";
				switch (settings.doCensorSpoilers) {
				case true:
					output += "*hidden*." + "\n";
					break;
				case false:
					output += "*shown*." + "\n";
					break;
				}
				output += "\u2023\u2002";
				switch (settings.doDetectSpoilers) {
				case true:
					output += "Will try to automatically detect spoilers." + "\n";
					break;
				case false:
					output += "Spoilers must be marked manually." + "\n";
					break;
				}
				break;
			case str_cmd_listCategories:
				Dictionary<Article.Category, bool> doShowCategory = settings.doShowCategory;
				output = "**<" + guild.Name + "> Category Filters**" + "\n";
				foreach(Article.Category category in doShowCategory.Keys) {
					switch (doShowCategory[category]) {
					case true:
						output += ":white_check_mark:\u2002" +
							category.ToString() + "\n";
						break;
					case false:
						output += ":no_entry_sign:\u2002~~" +
							category.ToString() + "~~" + "\n";
						break;
					}
				}
				break;
			case str_cmd_listSeries:
				Dictionary<Article.Series, bool> doShowSeries = settings.doShowSeries;
				output = "**<" + guild.Name + "> Series Filters**" + "\n";
				foreach (Article.Series series in doShowSeries.Keys) {
					switch (doShowSeries[series]) {
					case true:
						output += ":white_check_mark:\u2002" +
							series.ToString() + "\n";
						break;
					case false:
						output += ":no_entry_sign:\u2002~~" +
							series.ToString() + "~~" + "\n";
						break;
					}
				}
				break;
			case str_cmd_listOverrides:
				output = "**<" + guild.Name + "> Filter Overrides**" + "\n";
				output += "Hide Articles:" + "\n";
				if (settings.articles_hidden.Count == 0) {
					output += "\u2003" + "*none*" + "\n";
				} else {
					foreach (Article article in settings.articles_hidden) {
						output += "\u2003" + "<" + article.url + ">" + "\n";
					}
				}
				output += "Show Articles:" + "\n";
				if (settings.articles_shown.Count == 0) {
					output += "\u2003" + "*none*" + "\n";
				} else {
					foreach (Article article in settings.articles_shown) {
						output += "\u2003" + "<" + article.url + ">" + "\n";
					}
				}
				output += "Spoiler Articles:" + "\n";
				if (settings.articles_spoilered.Count == 0) {
					output += "\u2003" + "*none*" + "\n";
				} else {
					foreach (Article article in settings.articles_spoilered) {
						output += "\u2003" + "<" + article.url + ">" + "\n";
					}
				}
				output += "Un-spoiler Articles:" + "\n";
				if (settings.articles_unspoilered.Count == 0) {
					output += "\u2003" + "*none*" + "\n";
				} else {
					foreach (Article article in settings.articles_unspoilered) {
						output += "\u2003" + "<" + article.url + ">" + "\n";
					}
				}
				break;
			case str_cmd_setNews:
				DiscordChannel ch_news = null;
				if (e.Message.MentionedChannels.Count > 0) {
					ch_news = e.Message.MentionedChannels[0];
				} else if (Regex.IsMatch(arg, @"^\d+$")) {
					ch_news = guild.GetChannel(Convert.ToUInt64(arg));
				} else if (arg != "null" && arg != "none") {
					foreach (DiscordChannel channel in guild.Channels.Values) {
						if (channel.Name == arg) {
							ch_news = channel;
							break;
						}
					}
				}
				guildData[guild].settings.ch_news = ch_news;
				Save();
				if (ch_news == null) {
					output =
						"News channel set to none." + "\n" +
						"Future news digests will be hidden.";
				} else {
					output =
						"News channel set to " + ch_news.Mention + "." + "\n" +
						"Future news digests will be posted there.";
				}
				break;
			case str_cmd_setLogs:
				DiscordChannel ch_logs = null;
				if (e.Message.MentionedChannels.Count > 0) {
					ch_logs = e.Message.MentionedChannels[0];
				} else if (Regex.IsMatch(arg, @"^\d+$")) {
					ch_logs = guild.GetChannel(Convert.ToUInt64(arg));
				} else if (arg != "null" && arg != "none") {
					foreach (DiscordChannel channel in guild.Channels.Values) {
						if (channel.Name == arg) {
							ch_logs = channel;
							break;
						}
					}
				}
				guildData[guild].settings.ch_logs = ch_logs;
				Save();
				if (ch_logs == null) {
					output =
						"Logging channel set to none." + "\n" +
						"Future log messages will be hidden.";
				} else {
					output =
						"Logging channel set to " + ch_logs.Mention + "." + "\n" +
						"Future log messages will be posted there.";
				}
				break;
			case str_cmd_setFreq:
				Dictionary<string, Settings.PostFrequency> strToPostFreq =
					new Dictionary<string, Settings.PostFrequency> {
						{ "daily", Settings.PostFrequency.Daily },
						{ "day"  , Settings.PostFrequency.Daily },
						{ "d"    , Settings.PostFrequency.Daily },
						{ "1"    , Settings.PostFrequency.Daily },
						{ "weekly", Settings.PostFrequency.Weekly },
						{ "week"  , Settings.PostFrequency.Weekly },
						{ "w"     , Settings.PostFrequency.Weekly },
						{ "7"     , Settings.PostFrequency.Weekly },
					};
				Settings.PostFrequency postFreq = strToPostFreq[arg.ToLower()];
				guildData[guild].settings.postFrequency = postFreq;
				Save();
				output = "Post frequency set to *" +
					postFreq.ToString().ToLower() +
					"*." + "\n";
				break;
			case str_cmd_setCensorSpoilers:
				Dictionary<string, bool> strToCensorSpoilers =
					new Dictionary<string, bool> {
						{ "true" , true },
						{ "t"    , true },
						{ "yes"  , true },
						{ "y"    , true },
						{ "on"   , true },
						{ "false", false },
						{ "f"    , false },
						{ "no"   , false },
						{ "n"    , false },
						{ "off"  , false },
					};
				bool doCensorSpoilers = strToCensorSpoilers[arg.ToLower()];
				guildData[guild].settings.doCensorSpoilers = doCensorSpoilers;
				Save();
				switch (doCensorSpoilers) {
				case true:
					output = "Spoiler post titles are now being *hidden*." + "\n";
					break;
				case false:
					output = "Spoiler post titles are now being *shown*." + "\n";
					break;
				}
				break;
			case str_cmd_setDetectSpoilers:
				Dictionary<string, bool> strToDetectSpoilers =
					new Dictionary<string, bool> {
						{ "true" , true },
						{ "t"    , true },
						{ "yes"  , true },
						{ "y"    , true },
						{ "on"   , true },
						{ "false", false },
						{ "f"    , false },
						{ "no"   , false },
						{ "n"    , false },
						{ "off"  , false },
					};
				bool doDetectSpoilers = strToDetectSpoilers[arg.ToLower()];
				guildData[guild].settings.doDetectSpoilers = doDetectSpoilers;
				Save();
				switch (doDetectSpoilers) {
				case true:
					output = "Will now try to automatically detect spoilers." + "\n";
					break;
				case false:
					output = "Spoilers will need to be manually marked." + "\n";
					break;
				}
				break;
			case str_cmd_filterCategory:
				Dictionary<string, Article.Category> strToCategoryFilter =
					new Dictionary<string, Article.Category> {
						{ "live"     , Article.Category.Live },
						{ "ptr"      , Article.Category.PTR },
						{ "beta"     , Article.Category.Beta },
						{ "classic"  , Article.Category.Classic },
						{ "warcraft3", Article.Category.Warcraft3 },
						{ "wc3"      , Article.Category.Warcraft3 },
						//{ "overwatch", Article.Category.Overwatch },
						//{ "ow"       , Article.Category.Overwatch },
						{ "diablo"    , Article.Category.Diablo },
						{ "blizzard"  , Article.Category.Blizzard },
						{ "blizz"     , Article.Category.Blizzard },
						{ "wowhead"   , Article.Category.Wowhead },
						{ "wh"        , Article.Category.Wowhead },
					};
				Article.Category categoryFilter = strToCategoryFilter[arg.ToLower()];
				guildData[guild].settings.doShowCategory[categoryFilter] = false;
				Save();
				output = "No longer showing **" + categoryFilter.ToString() + "** posts in digests." + "\n";
				break;
			case str_cmd_unfilterCategory:
				Dictionary<string, Article.Category> strToCategoryUnfilter =
					new Dictionary<string, Article.Category> {
						{ "live"     , Article.Category.Live },
						{ "ptr"      , Article.Category.PTR },
						{ "beta"     , Article.Category.Beta },
						{ "classic"  , Article.Category.Classic },
						{ "warcraft3", Article.Category.Warcraft3 },
						{ "wc3"      , Article.Category.Warcraft3 },
						//{ "overwatch", Article.Category.Overwatch },
						//{ "ow"       , Article.Category.Overwatch },
						{ "diablo"    , Article.Category.Diablo },
						{ "blizzard"  , Article.Category.Blizzard },
						{ "blizz"     , Article.Category.Blizzard },
						{ "wowhead"   , Article.Category.Wowhead },
						{ "wh"        , Article.Category.Wowhead },
					};
				Article.Category categoryUnfilter = strToCategoryUnfilter[arg.ToLower()];
				guildData[guild].settings.doShowCategory[categoryUnfilter] = true;
				Save();
				output = "Now showing **" + categoryUnfilter.ToString() + "** posts in digests." + "\n";
				break;
			case str_cmd_filterSeries:
				Dictionary<string, Article.Series> strToSeriesFilter =
					new Dictionary<string, Article.Series> {
						{ "other"                , Article.Series.Other },
						{ "miscellaneous"        , Article.Series.Other },
						{ "m"                    , Article.Series.Other },
						{ "wowheadweekly"        , Article.Series.WowheadWeekly },
						{ "wowhead-weekly"       , Article.Series.WowheadWeekly },
						{ "wowhead weekly"       , Article.Series.WowheadWeekly },
						{ "whweekly"             , Article.Series.WowheadWeekly },
						{ "wh-weekly"            , Article.Series.WowheadWeekly },
						{ "wh weekly"            , Article.Series.WowheadWeekly },
						{ "economywrapup"        , Article.Series.EconomyWrapup },
						{ "economy-wrapup"       , Article.Series.EconomyWrapup },
						{ "economy wrapup"       , Article.Series.EconomyWrapup },
						{ "weeklyeconomywrapup"  , Article.Series.EconomyWrapup },
						{ "weekly-economy-wrapup", Article.Series.EconomyWrapup },
						{ "weekly economy wrapup", Article.Series.EconomyWrapup },
						{ "economyweeklywrapup"  , Article.Series.EconomyWrapup },
						{ "economy-weekly-wrapup", Article.Series.EconomyWrapup },
						{ "economy weekly wrapup", Article.Series.EconomyWrapup },
						{ "taliesinevitel"       , Article.Series.TaliesinEvitel },
						{ "taliesin-evitel"      , Article.Series.TaliesinEvitel },
						{ "taliesin evitel"      , Article.Series.TaliesinEvitel },
						{ "taliesin&evitel"      , Article.Series.TaliesinEvitel },
						{ "taliesin & evitel"    , Article.Series.TaliesinEvitel },
						{ "taliesin+evitel"      , Article.Series.TaliesinEvitel },
						{ "taliesin + evitel"    , Article.Series.TaliesinEvitel },
						{ "taliesinandevitel"    , Article.Series.TaliesinEvitel },
						{ "taliesin-and-evitel"  , Article.Series.TaliesinEvitel },
						{ "taliesin and evitel"  , Article.Series.TaliesinEvitel },
						{ "taliesin"             , Article.Series.TaliesinEvitel },
						{ "evitel"               , Article.Series.TaliesinEvitel },
						{ "t&e"                  , Article.Series.TaliesinEvitel },
						{ "t+e"                  , Article.Series.TaliesinEvitel },
					};
				Article.Series seriesFilter = strToSeriesFilter[arg.ToLower()];
				guildData[guild].settings.doShowSeries[seriesFilter] = false;
				Save();
				output = "No longer showing **" + seriesFilter.ToString() + "** posts in digests." + "\n";
				break;
			case str_cmd_unfilterSeries:
				Dictionary<string, Article.Series> strToSeriesUnfilter =
					new Dictionary<string, Article.Series> {
						{ "other"                , Article.Series.Other },
						{ "miscellaneous"        , Article.Series.Other },
						{ "m"                    , Article.Series.Other },
						{ "wowheadweekly"        , Article.Series.WowheadWeekly },
						{ "wowhead-weekly"       , Article.Series.WowheadWeekly },
						{ "wowhead weekly"       , Article.Series.WowheadWeekly },
						{ "whweekly"             , Article.Series.WowheadWeekly },
						{ "wh-weekly"            , Article.Series.WowheadWeekly },
						{ "wh weekly"            , Article.Series.WowheadWeekly },
						{ "economywrapup"        , Article.Series.EconomyWrapup },
						{ "economy-wrapup"       , Article.Series.EconomyWrapup },
						{ "economy wrapup"       , Article.Series.EconomyWrapup },
						{ "weeklyeconomywrapup"  , Article.Series.EconomyWrapup },
						{ "weekly-economy-wrapup", Article.Series.EconomyWrapup },
						{ "weekly economy wrapup", Article.Series.EconomyWrapup },
						{ "economyweeklywrapup"  , Article.Series.EconomyWrapup },
						{ "economy-weekly-wrapup", Article.Series.EconomyWrapup },
						{ "economy weekly wrapup", Article.Series.EconomyWrapup },
						{ "taliesinevitel"       , Article.Series.TaliesinEvitel },
						{ "taliesin-evitel"      , Article.Series.TaliesinEvitel },
						{ "taliesin evitel"      , Article.Series.TaliesinEvitel },
						{ "taliesin&evitel"      , Article.Series.TaliesinEvitel },
						{ "taliesin & evitel"    , Article.Series.TaliesinEvitel },
						{ "taliesin+evitel"      , Article.Series.TaliesinEvitel },
						{ "taliesin + evitel"    , Article.Series.TaliesinEvitel },
						{ "taliesinandevitel"    , Article.Series.TaliesinEvitel },
						{ "taliesin-and-evitel"  , Article.Series.TaliesinEvitel },
						{ "taliesin and evitel"  , Article.Series.TaliesinEvitel },
						{ "taliesin"             , Article.Series.TaliesinEvitel },
						{ "evitel"               , Article.Series.TaliesinEvitel },
						{ "t&e"                  , Article.Series.TaliesinEvitel },
						{ "t+e"                  , Article.Series.TaliesinEvitel },
					};
				Article.Series seriesUnfilter = strToSeriesUnfilter[arg.ToLower()];
				guildData[guild].settings.doShowSeries[seriesUnfilter] = true;
				Save();
				output = "Now showing **" + seriesUnfilter.ToString() + "** posts in digests." + "\n";
				break;
			case str_cmd_hide:
				if (!Regex.IsMatch(arg, @"^\d+$")) {
					arg = Article.UrlToId(arg);
				}
				Article articleHide = new Article(arg, DateTime.Now);	// datetime doesn't matter
				if (articles.Contains(articleHide)) {
					articles.TryGetValue(articleHide, out articleHide);
					guildData[guild].settings.articles_hidden.Add(articleHide);
					if (settings.articles_shown.Contains(articleHide))
						guildData[guild].settings.articles_shown.Remove(articleHide);
					Save();
					output = ":no_entry_sign: Article hidden: <" + articleHide.url + ">" + "\n";
				} else {
					output = "That article isn't being tracked." + "\n";
				}
				break;
			case str_cmd_show:
				if (!Regex.IsMatch(arg, @"^\d+$")) {
					arg = Article.UrlToId(arg);
				}
				Article articleShow = new Article(arg, DateTime.Now);   // datetime doesn't matter
				if (articles.Contains(articleShow)) {
					articles.TryGetValue(articleShow, out articleShow);
					guildData[guild].settings.articles_shown.Add(articleShow);
					if (settings.articles_hidden.Contains(articleShow))
						guildData[guild].settings.articles_hidden.Remove(articleShow);
					Save();
					output = ":white_check_mark: Article shown: <" + articleShow.url + ">" + "\n";
				} else {
					output = "That article isn't being tracked." + "\n";
				}
				break;
			case str_cmd_spoiler:
				if (!Regex.IsMatch(arg, @"^\d+$")) {
					arg = Article.UrlToId(arg);
				}
				Article articleSpoiler = new Article(arg, DateTime.Now);   // datetime doesn't matter
				if (articles.Contains(articleSpoiler)) {
					articles.TryGetValue(articleSpoiler, out articleSpoiler);
					guildData[guild].settings.articles_spoilered.Add(articleSpoiler);
					if (settings.articles_unspoilered.Contains(articleSpoiler))
						guildData[guild].settings.articles_unspoilered.Remove(articleSpoiler);
					Save();
					output = ":see_no_evil: Article marked as spoiler: <" + articleSpoiler.url + ">" + "\n";
				} else {
					output = "That article isn't being tracked." + "\n";
				}
				break;
			case str_cmd_unspoiler:
				if (!Regex.IsMatch(arg, @"^\d+$")) {
					arg = Article.UrlToId(arg);
				}
				Article articleUnspoiler = new Article(arg, DateTime.Now);   // datetime doesn't matter
				if (articles.Contains(articleUnspoiler)) {
					articles.TryGetValue(articleUnspoiler, out articleUnspoiler);
					guildData[guild].settings.articles_spoilered.Add(articleUnspoiler);
					if (settings.articles_unspoilered.Contains(articleUnspoiler))
						guildData[guild].settings.articles_unspoilered.Remove(articleUnspoiler);
					Save();
					output = ":monkey_face: Article marked as spoiler-free: <" + articleUnspoiler.url + ">" + "\n";
				} else {
					output = "That article isn't being tracked." + "\n";
				}
				break;
			default:
				output = "Command not recognized." + "\n" +
					"command: `" + cmd + "`" + "\n";
				if (arg != "")
					output += "parameter(s): `" + arg + "`" + "\n";
				break;
			}

			return output;
		}
	}
}
