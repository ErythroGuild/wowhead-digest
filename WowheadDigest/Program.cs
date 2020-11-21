using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using DSharpPlus;
using DSharpPlus.Entities;

namespace WowheadDigest {
	class Program {
		static readonly Logger log = new Logger();
		static DiscordClient discord = null;
		static string str_mention = null;

		static Dictionary<DiscordGuild, GuildData> guildData =
			new Dictionary<DiscordGuild, GuildData>();

		const string path_token    = @"token.txt";
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
						cmd = split[0];
						arg = split[1];
					} else {
						cmd = message;
						arg = "";
					}
					log.Debug("  cmd: " + cmd);
					log.Debug("  arg: " + arg);
					string reply = ParseCommand(cmd, arg, e.Guild);
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

		static string ParseCommand(string cmd, string arg, DiscordGuild guild) {
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
				break;
			case str_cmd_setLogs:
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
				output += "Post frequency set to *" +
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
					output += "Spoiler post titles are now being *hidden*." + "\n";
					break;
				case false:
					output += "Spoiler post titles are now being *shown*." + "\n";
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
					output += "Will now try to automatically detect spoilers." + "\n";
					break;
				case false:
					output += "Spoilers will need to be manually marked." + "\n";
					break;
				}
				break;
			}

			return output;
		}
	}
}
