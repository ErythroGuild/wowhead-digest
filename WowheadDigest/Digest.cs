﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using DSharpPlus;
using DSharpPlus.Entities;

using static WowheadDigest.Article;

namespace WowheadDigest {
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

		public const int max_articles = 10;

		private static readonly string endl = Environment.NewLine;
		private const Int32 color = 0xB21C1A;
		private const string url_favicon = @"https://wow.zamimg.com/images/logos/favicon-standard.png";

		public static async Task<Digest> FromString(
			string data,
			Settings settings,
			DiscordClient client
		) {
			Digest digest = new Digest();

			StringReader reader = new StringReader(data);
			while (reader.Peek() != -1) {
				string line = reader.ReadLine();
				if (line.StartsWith("- ")) {
					Match split = Regex.Match(line, @"- (\d+)\/(\d+)#(\d+)@([\d-]+)");

					ulong ch_id = Convert.ToUInt64(split.Groups[1].Value);
					DiscordChannel channel = await client.GetChannelAsync(ch_id);
					ulong msg_id = Convert.ToUInt64(split.Groups[2].Value);
					digest.message = await channel.GetMessageAsync(msg_id);
					digest.date_i = Convert.ToInt32(Convert.ToInt32(split.Groups[3].Value));
					digest.date = DateTime.ParseExact(split.Groups[4].Value, "yyyy-MM-dd", null);

					while (reader.Peek() != -1) {
						line = reader.ReadLine();
						if (!line.StartsWith("\t"))
							break;
						line = line.Substring(1);
						digest.articles.Add(Article.FromString(line));
					}

					if (settings.doCensorSpoilers) {
						foreach (Article article in digest.articles) {
							if (settings.doDetectSpoilers && article.hasSpoiler)
								digest.articles_spoiler.Add(article);
							if (settings.articles_unspoilered.Contains(article))
								digest.articles_spoiler.Remove(article);
							if (settings.articles_spoilered.Contains(article))
								digest.articles_spoiler.Add(article);
						}
					}

					break;
				}
			}

			return digest;
		}

		public DateTime date;
		public int date_i;

		public DiscordMessage message = null;
		public SortedSet<Article> articles =
			new SortedSet<Article>(new ArticleTimeComparer());

		public HashSet<Article> articles_spoiler = new HashSet<Article>();

		public override string ToString() {
			string data = "";
			data += "- " +
				message.ChannelId.ToString() + "/" +
				message.Id.ToString() + "#" +
				date_i.ToString() + "@" +
				date.ToString("yyyy-MM-dd") + endl;
			foreach (Article article in articles) {
				data += "\t" + article.ToString() + endl;
			}
			return data;
		}

		public bool IsFull() {
			return articles.Count >= max_articles;
		}

		public DiscordEmbed GetEmbed() {
			// Make sure an article exists
			if (articles.Count < 1)
				return null;
			Article article_example = articles.Min;
			string url_thumbnail = article_example.thumbnail;

			bool isFirstArticle = true;
			string content = "";
			foreach (Article article in articles) {
				if (!isFirstArticle)
					content += "\n";
				else
					isFirstArticle = false;

				content += bullets[article.category] + " [";
				if (articles_spoiler.Contains(article)) {
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
				.WithFooter("last updated", url_favicon)
				.WithTimestamp(DateTimeOffset.Now);

			return builder.Build();
		}
	}
}
