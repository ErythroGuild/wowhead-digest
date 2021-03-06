﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

using HtmlAgilityPack;

namespace WowheadDigest {
	class Article {
		public class ArticleTimeComparer : IComparer<Article> {
			public int Compare(Article x, Article y) {
				return x.time.CompareTo(y.time);
			}
		}

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

		private const string delim = "@";

		public static bool operator ==(Article x, Article y) {
			return x.id == y.id;
		}
		public static bool operator !=(Article x, Article y) {
			return x.id != y.id;
		}

		// TODO: add all sorts of error checking for conversions

		// Assumes `url` is a valid Wowhead news URL.
		public static string UrlToId(string url) {
			Match match = Regex.Match(url, @"wowhead\.com\/news=(\d+)");
			return match.Groups[1].Value;
		}

		public static string IdToUrl(string id) {
			return @"https://www.wowhead.com/news=" + id;
		}

		public static Article FromString(string data) {
			string[] data_buf = data.Split(delim, 2);
			string id = data_buf[0];
			DateTime time = DateTime.ParseExact(data_buf[1], "s", null);
			return new Article(id, time);
		}

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
		public string title { get => GetTitle(); }
		public string thumbnail { get => GetThumbnail(); }

		private string _id;

		public Article(string id, DateTime time) {
			this.id = id;
			this.time = time;
		}

		public override bool Equals(object obj) {
			if (obj is Article)
				return id == ((Article) obj).id;
			else
				return false;
		}

		public override int GetHashCode() {
			return id.GetHashCode();
		}

		public override string ToString() {
			return id.ToString() + "@" + time.ToString("s");
		}

		private Category ParseCategory() {
			HtmlDocument doc = new HtmlWeb().Load(url);

			string xpath =
				@"//div[@id='main-contents']" +
				@"/div[@id='news-post-" + id + @"']";
			HtmlNode node = doc.DocumentNode.SelectSingleNode(xpath);

			int category = node.GetAttributeValue("data-type", 1);
			return (Category) category;
		}

		private Series ParseSeries() {
			string text = title;

			// Check if Wowhead Weekly
			if (Regex.IsMatch(text, @"^Wowhead Weekly #\d+"))
				return Series.WowheadWeekly;

			// Check if Economy Wrapup
			if (Regex.IsMatch(text, @"^Wowhead Economy Weekly Wrap-Up \d+"))
				return Series.EconomyWrapup;

			// Check if Taliesin & Evitel
			if (Regex.IsMatch(text, @"^The Weekly Reset by Taliesin and Evitel"))
				return Series.TaliesinEvitel;

			return Series.Other;
		}

		private bool ParseSpoiler() {
			string text = title.ToLower();

			if (text.Contains("spoilers") && !text.Contains("no spoilers"))
				return true;
			else
				return false;
		}

		private string GetTitle() {
			HtmlDocument doc = new HtmlWeb().Load(url);

			string xpath = @"//head/meta[@property='og:title']";
			HtmlNode node = doc.DocumentNode.SelectSingleNode(xpath);
			string title = node.GetAttributeValue("content", null);

			return WebUtility.HtmlDecode(title);
		}

		private string GetThumbnail() {
			HtmlDocument doc = new HtmlWeb().Load(url);

			string xpath = @"//head/meta[@property='og:image']";
			HtmlNode node = doc.DocumentNode.SelectSingleNode(xpath);

			return node.GetAttributeValue("content", null);
		}
	}
}
