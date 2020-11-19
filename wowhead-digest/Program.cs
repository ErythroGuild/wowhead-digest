using System;
using System.Collections.Generic;
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

		const ulong channel_debug_id = 489274692255875091;  // <Erythro> - #test

		public static ref readonly Logger GetLogger() { return ref log; }

		static void Main() {
			const string title_ascii =
				" █   █ ▄▀▄ █   █ █▄█ ██▀ ▄▀▄ █▀▄   █▀▄ █ ▄▀  ██▀ ▄▀▀ ▀█▀ " + "\n" +
				" ▀▄▀▄▀ ▀▄▀ ▀▄▀▄▀ █ █ █▄▄ █▀█ █▄▀   █▄▀ █ ▀▄█ █▄▄ ▄██  █  " + "\n";
			Console.WriteLine(title_ascii);
			//log.show_timestamp = true;
			//log.type_minimum = Logger.Type.Debug;
			MainAsync().ConfigureAwait(false).GetAwaiter().GetResult();
		}

		static async Task MainAsync() {
			log.Info("Initializing...");
			Connect();
			if (discord == null) {
				log.Error("Terminating program.");
				return;
			}

			discord.Ready += async e => {
				log.Info("Connected to discord.");
			};
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
			puck = new DiscordClient(new DiscordConfiguration {
				Token = token,
				TokenType = TokenType.Bot
			});
			log.Info("Connecting to discord...");
		}
	}
}
