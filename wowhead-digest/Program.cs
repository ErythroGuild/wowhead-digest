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

			discord.Ready += async (s, e) => {
				log.Info("Connected to discord.");
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
