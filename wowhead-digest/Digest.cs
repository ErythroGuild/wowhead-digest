using System;
using System.Collections.Generic;
using System.Text;

using DSharpPlus;
using DSharpPlus.Entities;

namespace wowhead_digest {
	class Digest {
		private const Int32 color = 0xB21C1A;
		private const string url_favicon = @"https://wow.zamimg.com/images/logos/favicon-standard.png";

		public DiscordMessage message = null;
		public List<Article> articles;
		public List<Article> articles_spoiler;
		public List<Article> articles_unspoiler;

		public DiscordEmbed GetEmbed(GuildData guildData) {
			// TODO: error check and make sure an article exists
			string str_date = articles[0].time.Date.ToString("D");
			string str_time = articles[0].time.TimeOfDay.ToString("T");
			string url_thumbnail = articles[0].thumbnail;
			string description = "";

			foreach(Article article in articles) {
				description += "\n\u2022 [";
				if (article.hasSpoiler &&
					!articles_unspoiler.Contains(article) ||
					articles_spoiler.Contains(article)
				) {
					description += "--SPOILER!--";
				} else {
					description += article.title;
				}
				description += "](" + article.url + ")\n";
			}

			DiscordEmbedBuilder builder = new DiscordEmbedBuilder()
				.WithColor(new DiscordColor(color))
				.WithThumbnail(url_thumbnail)
				.WithTitle(str_date)
				.WithDescription(description)
				.WithFooter("last updated " + str_time, url_favicon);

			return builder.Build();
		}
	}
}
