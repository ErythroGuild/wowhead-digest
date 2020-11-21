using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using DSharpPlus;
using DSharpPlus.Entities;

using static wowhead_digest.Article;

namespace wowhead_digest {
	class Settings {
		public enum PostFrequency { Daily, Weekly }

		public DiscordChannel ch_news;
		public DiscordChannel ch_logs;

		public PostFrequency postFrequency;
		public bool doShowAutoSpoilers;
		public Dictionary<Category, bool> doShowCategory;
		public Dictionary<Series, bool> doShowSeries;

		public HashSet<Article> articles_hidden;
		public HashSet<Article> articles_shown;
		public HashSet<Article> articles_spoilered;
		public HashSet<Article> articles_unspoilered;

		private const string delim_key = ": ";
		private const string delim_list = ", ";

		private const string key_ch_news			= "ch_news";
		private const string key_ch_logs			= "ch_logs";
		private const string key_postFrequency		= "post_freq";
		private const string key_doShowAutoSpoilers	= "do_show_auto_spoilers";
		private const string key_doShowCategory		= "do_show_category";
		private const string key_doShowSeries		= "do_show_series";
		private const string key_articles_hidden	= "articles_hidden";
		private const string key_articles_shown		= "articles_shown";
		private const string key_articles_spoilered	= "articles_spoilered";
		private const string key_articles_unspoilered = "articles_unspoilered";

		public static Settings Default() {
			return new Settings() {
				ch_news = null,
				ch_logs = null,
				postFrequency = PostFrequency.Daily,
				doShowAutoSpoilers = false,
				doShowCategory = {
					{ Category.Live,		true  },
					{ Category.PTR,			true  },
					{ Category.Beta,		true  },
					{ Category.Classic,		false },
					{ Category.Warcraft3,	false },
					{ Category.Diablo,		false },
					//{ Category.Overwatch,	false },
					{ Category.Blizzard,	true  },
					{ Category.Wowhead,		false },
				},
				doShowSeries = {
					{ Series.Other,				true  },
					{ Series.WowheadWeekly,		false },
					{ Series.EconomyWrapup,		false },
					{ Series.TaliesinEvitel,	false },
				},
				articles_hidden = new HashSet<Article>(),
				articles_shown = new HashSet<Article>(),
				articles_spoilered = new HashSet<Article>(),
				articles_unspoilered = new HashSet<Article>()
			};
		}

		private static List<string> ParseList(string data) {
			// must consist of a `[...]` list if  this function is called
			data = data[1..^1];
			return new List<string>(data.Split(delim_list));
		}

		private static void AddKey(ref string data, string key, string val) {
			data += "\t" + key + delim_key + val + "\n";
		}

		private static void AddKey(ref string data, string key, List<string> vals) {
			data += "\t" + key + delim_key;
			data += "[";

			bool isFirstVal = true;
			foreach (string val in vals) {
				if (!isFirstVal)
					data += delim_list;
				else
					isFirstVal = false;
				data += val;
			}

			data += "]\n";
		}

		// Hide default constructor.
		private Settings() {}

		public static async Task<Settings> Load(string data, DiscordClient client) {
			Settings s = new Settings();

			// Read + preprocess data.
			Dictionary<string, string> data_buf = new Dictionary<string, string>();

			StringReader reader = new StringReader(data);
			while (reader.Peek() != -1) {
				string line = reader.ReadLine().Trim();
				string[] line_split = line.Split(delim_key, 2);
				data_buf.Add(line_split[0], line_split[1]);
			}

			// Convert data.
			Dictionary<T, bool> StringsToEntries<T>(List<string> data) {
				Dictionary<T, bool> entries = new Dictionary<T, bool>();
				foreach (string entry in data) {
					string[] entry_buf = entry.Split(delim_key);
					T key = (T) Enum.Parse(typeof(T), entry_buf[0]);
					bool val = Convert.ToBoolean(entry_buf[1]);
					entries.Add(key, val);
				}
				return entries;
			}

			HashSet<Article> StringsToArticles(List<string> data) {
				HashSet<Article> articles = new HashSet<Article>();
				foreach (string article in data) {
					articles.Add(FromString(article));
				}
				return articles;
			}

			foreach (string key in data_buf.Keys) {
				string val = data_buf[key];
				switch (key) {
				case key_ch_news:
					s.ch_news = await client.GetChannelAsync(Convert.ToUInt64(val));
					break;
				case key_ch_logs:
					s.ch_logs = await client.GetChannelAsync(Convert.ToUInt64(val));
					break;
				case key_postFrequency:
					s.postFrequency =
						(PostFrequency) Enum.Parse(typeof(PostFrequency), val);
					break;
				case key_doShowAutoSpoilers:
					s.doShowAutoSpoilers = Convert.ToBoolean(val);
					break;

				case key_doShowCategory:
					s.doShowCategory = StringsToEntries<Category>(ParseList(val));
					break;
				case key_doShowSeries:
					s.doShowSeries = StringsToEntries<Series>(ParseList(val));
					break;

				case key_articles_hidden:
					s.articles_hidden = StringsToArticles(ParseList(val));
					break;
				case key_articles_shown:
					s.articles_shown = StringsToArticles(ParseList(val));
					break;
				case key_articles_spoilered:
					s.articles_spoilered = StringsToArticles(ParseList(val));
					break;
				case key_articles_unspoilered:
					s.articles_unspoilered = StringsToArticles(ParseList(val));
					break;
				}
			}

			return s;
		}

		public string Save() {
			string data = "";

			void AddVal(string key, string val) { AddKey(ref data, key, val); }
			void AddVals(string key, List<string> vals) { AddKey(ref data, key, vals); }

			AddVal(key_ch_news, ch_news.Id.ToString());
			AddVal(key_ch_logs, ch_logs.Id.ToString());
			AddVal(key_postFrequency, postFrequency.ToString());
			AddVal(key_doShowAutoSpoilers, doShowAutoSpoilers.ToString());

			List<string> EntriesToStrings<T>(Dictionary<T, bool> entries) {
				List<string> strings = new List<string>();
				foreach (T key in entries.Keys) {
					string entry = key.ToString() + delim_list + entries[key].ToString();
					strings.Add(entry);
				}
				return strings;
			}

			AddVals(key_doShowCategory, EntriesToStrings(doShowCategory));
			AddVals(key_doShowCategory, EntriesToStrings(doShowSeries));

			List<string> ArticlesToStrings(ISet<Article> articles) {
				List<string> data = new List<string>();
				foreach (Article article in articles) {
					data.Add(article.ToString());
				}
				return data;
			}

			AddVals(key_articles_hidden, ArticlesToStrings(articles_hidden));
			AddVals(key_articles_shown, ArticlesToStrings(articles_shown));
			AddVals(key_articles_spoilered, ArticlesToStrings(articles_spoilered));
			AddVals(key_articles_unspoilered, ArticlesToStrings(articles_unspoilered));

			return data;
		}
	}
}
