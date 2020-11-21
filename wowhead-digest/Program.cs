using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using DSharpPlus;
using DSharpPlus.Entities;

namespace wowhead_digest {
	class Program {
		static readonly Logger log = new Logger();
		static DiscordClient discord = null;

		static Dictionary<DiscordGuild, GuildData> guildData;

		const string path_token    = @"token.txt";
		const string path_settings = @"settings.txt";
		const string path_digests  = @"digests.txt";

		const ulong id_wh_wowhead = 779070312561770528;
		const ulong id_ch_ingest = 777935219193020426;	// <Erythro> - #ingest
		const ulong id_ch_debug = 489274692255875091;	// <Erythro> - #test

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

			discord.Ready += (s, e) => {
				log.Info("Connected to discord.");
				return Task.CompletedTask;
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

				log.Info("Writing back loaded data...");
				StreamWriter writer_settings = new StreamWriter(path_settings);
				StreamWriter writer_digests = new StreamWriter(path_digests);
				foreach (DiscordGuild guild in guildData.Keys) {
					string guild_str = "GUILD - " + guild.Id.ToString();
					writer_settings.WriteLine(guild_str);
					writer_settings.Write(guildData[guild].settings.Save());
					writer_settings.WriteLine();
					writer_digests.WriteLine(guild_str);
					writer_digests.Write(guildData[guild].digests.ToString());
					writer_digests.WriteLine();
				}
				writer_settings.Close();
				writer_digests.Close();
				log.Info("Data written.");
			};

			discord.MessageCreated += async (s, e) => {
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
	}
}
