using System;
using System.Collections.Generic;

using DSharpPlus.Entities;

using static wowhead_digest.Article;

namespace wowhead_digest {
	class Digest {
		public class DigestTimeComparer : IComparer<Digest> {
			public int Compare(Digest x, Digest y) {
				if (x.date != y.date) {
					return x.date.CompareTo(y.date);
				} else {
					return x.date_i - y.date_i;
				}
			}
		}

		private static readonly Dictionary<Category, string> bullets =
			new Dictionary<Category, string> {
			{Category.Live     , "\uD83D\uDFE9"},	// square, green
			{Category.PTR      , "\uD83D\uDFE6"},	// square, blue
			{Category.Beta     , "\uD83D\uDFEA"},	// square, purple
			{Category.Classic  , "\uD83D\uDFE4"},	// circle, brown
			{Category.Warcraft3, "\u26AA"      },	// circle, white
			{Category.Diablo   , "\uD83D\uDD34"},	// circle, red
			// {Category.Overwatch, "\uD83D\uDFE1"},	// circle, yellow
			{Category.Blizzard , "\uD83D\uDD37"},	// diamond, blue
			{Category.Wowhead  , "\uD83D\uDD36"},	// diamond, orange
		};

		private const Int32 color = 0xB21C1A;
		private const string url_favicon = @"https://wow.zamimg.com/images/logos/favicon-standard.png";

		public DateTime date;
		public int date_i;

		public DiscordMessage message = null;
		public SortedSet<Article> articles =
			new SortedSet<Article>(new ArticleTimeComparer());

		public HashSet<Article> articles_spoiler = new HashSet<Article>();
		public HashSet<Article> articles_unspoiler = new HashSet<Article>();

		public DiscordEmbed GetEmbed() {
			// Make sure an article exists
			if (articles.Count < 1)
				return null;
			Article article_example = articles.Min;

			string str_time = article_example.time.TimeOfDay.ToString("T");
			string url_thumbnail = article_example.thumbnail;

			bool isFirstArticle = true;
			string content = "";
			foreach (Article article in articles) {
				if (!isFirstArticle)
					content += "\n";
				else
					isFirstArticle = false;

				content += bullets[article.category] + " [";
				if (article.hasSpoiler &&
					!articles_unspoiler.Contains(article) ||
					articles_spoiler.Contains(article)
				) {
					content += "--**SPOILER**--";
				} else {
					content += article.title;
				}
				content += "](" + article.url + ")\n";
			}

			DiscordEmbedBuilder builder = new DiscordEmbedBuilder()
				.WithColor(new DiscordColor(color))
				.WithThumbnail(url_thumbnail)
				.WithDescription(content)
				.WithFooter("last updated " + str_time, url_favicon);

			return builder.Build();
		}
	}
}
