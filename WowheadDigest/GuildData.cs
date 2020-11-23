using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

using DSharpPlus;
using DSharpPlus.Entities;

namespace WowheadDigest {
	class GuildData {
		public Settings settings;
		public List<Digest> digests;

		public async Task ImportDigests(string data, Settings settings, DiscordClient client) {
			digests = new List<Digest>();

			StringReader reader = new StringReader(data);
			string line_carry = null;
			while (reader.Peek() != -1) {
				string line = line_carry;
				if (line == null) {
					line = reader.ReadLine();
				}
				if (line.StartsWith("- ")) {
					string data_digest = line + "\n";
					while (reader.Peek() != -1) {
						line = reader.ReadLine();
						if (line.StartsWith("\t")) {
							data_digest += line + "\n";
						} else if (line.StartsWith("- ")) {
							line_carry = line;
							break;
						} else {
							break;
						}
					}
					Digest digest = await Digest.FromString(
						data_digest,
						settings,
						client);
					digests.Add(digest);
				}
			}
		}

		public string ExportDigests() {
			string data = "";
			foreach (Digest digest in digests) {
				data += digest.ToString();
			}
			return data;
		}

		public async Task UpdateEmbeds(DiscordClient client) {
			string msg_log = ":arrows_counterclockwise: Updated all tracked embeds.";
			_ = client.SendMessageAsync(settings.ch_logs, msg_log);
			foreach (Digest digest in digests) {
				DiscordEmbed embed = digest.GetEmbed();
				await digest.message.ModifyAsync(null, embed);
				break;
			}
		}

		public async Task UpdateEmbed(Digest digest, DiscordClient client) {
			string msg_log = ":arrows_counterclockwise: Updated embed.";
			_ = client.SendMessageAsync(settings.ch_logs, msg_log);
			await digest.message.ModifyAsync(null, digest.GetEmbed());
		}

		public async Task UpdateEmbed(Article article, DiscordClient client) {
			foreach (Digest digest in digests) {
				if (digest.articles.Contains(article)) {
					string msg_log = ":arrows_counterclockwise: Updated article: <" + article.url + ">" + "\n";
					msg_log += "> *" + article.title + "*";
					_ = client.SendMessageAsync(settings.ch_logs, msg_log);
					DiscordEmbed embed = digest.GetEmbed();
					await digest.message.ModifyAsync(null, embed);
					break;
				}
			}
		}

		public async Task Add(Article article, DiscordClient client) {
			if (!ShouldAdd(article)) {
				string msg_hide = ":no_entry_sign: Filtered out article: <" + article.url + ">" + "\n";
				msg_hide += "> *" + article.title + "*";
				_ = client.SendMessageAsync(settings.ch_logs, msg_hide);
				return;
			}

			// check and notify if article already exists (updating)
			bool isUpdate = false;
			foreach (Digest digest in digests) {
				foreach (Article article_existing in digest.articles) {
					if (article == article_existing) {
						isUpdate = true;
						break;
					}
				}
				if (isUpdate)
					break;
			}
			if (isUpdate) {
				string msg_update = "Updating article: <" + article.url + ">" + "\n";
				msg_update += "> *" + article.title + "*";
				_ = client.SendMessageAsync(settings.ch_logs, msg_update);
				DiscordEmbed embed_update = digests[digests.Count - 1].GetEmbed();
				_ = digests[digests.Count - 1].message.ModifyAsync(null, embed_update);
				return;
			}

			// populate list of Digests with initial digest if needed
			if (digests.Count == 0) {
				Digest digest = new Digest() {
					date = DateTime.Today,
					date_i = 1
				};
				digests.Add(digest);
			}

			// add new Digest if post frequency ticked over
			switch (settings.postFrequency) {
			case Settings.PostFrequency.Daily:
				if (digests[digests.Count - 1].date.Day != DateTime.Today.Day) {
					Digest digest = new Digest() {
						date = DateTime.Today,
						date_i = 1
					};
					digests.Add(digest);
				}
				break;
			case Settings.PostFrequency.Weekly:
				Calendar calendar = CultureInfo.InvariantCulture.Calendar;
				int weekOfYear_prev = calendar.GetWeekOfYear(
					digests[digests.Count - 1].date,
					CalendarWeekRule.FirstFullWeek,
					DayOfWeek.Tuesday
				);
				int weekOfyear_now = calendar.GetWeekOfYear(
					DateTime.Today,
					CalendarWeekRule.FirstFullWeek,
					DayOfWeek.Tuesday
				);
				if (weekOfyear_now > weekOfYear_prev) {
					Digest digest = new Digest() {
						date = DateTime.Today,
						date_i = 1
					};
					digests.Add(digest);
				}
				break;
			}

			// add new Digest if last one is full
			if (digests[digests.Count - 1].IsFull()) {
				Digest digest = new Digest() {
					date = DateTime.Today,
					date_i = digests[digests.Count - 1].date_i + 1
				};
			}

			// appropriately mark spoilers
			if (settings.doCensorSpoilers) {
				if (settings.doDetectSpoilers && article.hasSpoiler)
					digests[digests.Count - 1].articles_spoiler.Add(article);
				if (settings.articles_unspoilered.Contains(article))
					digests[digests.Count - 1].articles_spoiler.Remove(article);
				if (settings.articles_spoilered.Contains(article))
					digests[digests.Count - 1].articles_spoiler.Add(article);
			}

			// add Article to Digest
			digests[digests.Count - 1].articles.Add(article);
			string msg_log = ":mailbox: Added article: <" + article.url + ">" + "\n";
			if (digests[digests.Count - 1].articles_spoiler.Contains(article))
				msg_log += ":see_no_evil: Flagged as spoiler." + "\n";
			msg_log += "> *" + article.title + "*";
			_ = client.SendMessageAsync(settings.ch_logs, msg_log);

			// update/create embeds
			DiscordEmbed embed = digests[digests.Count - 1].GetEmbed();
			if (digests[digests.Count - 1].message == null) {
				digests[digests.Count - 1].message =
					await client.SendMessageAsync(settings.ch_news, null, false, embed);
				// TODO: make asynchronous
			} else {
				await digests[digests.Count - 1].message.ModifyAsync(null, embed);
			}
		}

		public async Task Remove(Article article, DiscordClient client) {
			foreach (Digest digest in digests) {
				if (digest.articles.Contains(article)) {
					digest.articles.Remove(article);
					string msg_log = ":no_entry_sign: Removed article: <" + article.url + ">" + "\n";
					msg_log += "> *" + article.title + "*";
					//_ = client.SendMessageAsync(settings.ch_logs, msg_log);

					DiscordEmbed embed = digest.GetEmbed();
					await digest.message.ModifyAsync(null, embed);
					break;
				}
			}
		}

		private bool ShouldAdd(Article article) {
			if (settings.articles_hidden.Contains(article))
				return false;
			if (settings.articles_shown.Contains(article))
				return true;
			return
				settings.doShowCategory[article.category] &&
				settings.doShowSeries[article.series];
		}
	}
}
