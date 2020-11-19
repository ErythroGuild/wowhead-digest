using System;
using System.Text.RegularExpressions;

using HtmlAgilityPack;

namespace wowhead_digest {
	class Article {
		// Overwatch is not a valid category (anymore?).
		// All articles regarding Overwatch seem to be classified as
		// "Blizzard" instead.
		public enum Category {
			Live = 1, PTR = 2, Beta = 3,
			Classic = 8, Warcraft3 = 9,
			Diablo = 6, /* Overwatch = 7, */
			Blizzard = 4, Wowhead = 5,
		};

		public enum Series {
			Other = 0,
			WowheadWeekly,
			EconomyWrapup,
			TaliesinEvitel,
		};

		// TODO: add all sorts of error checking for conversions

		// Assumes `url` is a valid Wowhead news URL.
		public static string UrlToId(string url) {
			Match match = Regex.Match(url, @"wowhead\.com\/news=(\d+)");
			return match.Groups[1].Value;
		}

		public static string IdToUrl(string id) {
			return @"https://www.wowhead.com/news=" + id;
		}

		private string _id;

		public string id {
			get => _id;
			set => _id = value;
		}
		public string url {
			get => IdToUrl(_id);
			set => _id = UrlToId(value);
		}
		public DateTime time { get; set; }
		public Category category { get => ParseCategory(); }
		public Series series { get => ParseSeries(); }
		public bool hasSpoiler { get => ParseSpoiler(); }

		public Article(string id, DateTime time) {
			this.id = id;
			this.time = time;
		}

		private Category ParseCategory() {
			HtmlDocument doc = new HtmlWeb().Load(url);

			string xpath =
				@"//div[@id='main-contents']" +
				@"/div[@id='news-post-" + id + @"']";
			HtmlNode node = doc.DocumentNode.SelectSingleNode(xpath);

			int category = node.GetAttributeValue("data-type", 1);
			return (Category)category;
		}

		private Series ParseSeries() {
			HtmlDocument doc = new HtmlWeb().Load(url);

			string xpath = @"//head/title";
			HtmlNode node = doc.DocumentNode.SelectSingleNode(xpath);

			string title = node.InnerText;

			// Check if Wowhead Weekly
			if (Regex.IsMatch(title, @"^Wowhead Weekly #\d+"))
				return Series.WowheadWeekly;

			// Check if Economy Wrapup
			if (Regex.IsMatch(title, @"^Wowhead Economy Weekly Wrap-Up \d+"))
				return Series.EconomyWrapup;

			// Check if Taliesin & Evitel
			if (Regex.IsMatch(title, @"^The Weekly Reset by Taliesin and Evitel"))
				return Series.TaliesinEvitel;

			return Series.Other;
		}

		private bool ParseSpoiler() {
			HtmlDocument doc = new HtmlWeb().Load(url);

			string xpath = @"//head/title";
			HtmlNode node = doc.DocumentNode.SelectSingleNode(xpath);

			string title = node.InnerText.ToLower();

			if (title.Contains("spoilers") && !title.Contains("no spoilers"))
				return true;
			else
				return false;
		}
	}
}
