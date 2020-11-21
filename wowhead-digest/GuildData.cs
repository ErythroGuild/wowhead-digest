using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using DSharpPlus;
using DSharpPlus.Entities;

namespace wowhead_digest {
	class GuildData {
		public Settings settings;
		public List<Digest> digests;

		public async void ImportDigests(string data, DiscordClient client) {
			digests = new List<Digest>();

			StreamReader reader = new StreamReader(data);
			while (reader.Peek() != -1) {
				string line = reader.ReadLine();
				if (line.StartsWith("- ")) {
					string data_digest = line + "\n";
					while (reader.Peek() != -1) {
						line = reader.ReadLine();
						if (line.StartsWith("\t")) {
							data_digest += line + "\n";
						} else {
							Digest digest = await Digest.FromString(
								data_digest,
								client,
								settings.articles_spoilered,
								settings.articles_unspoilered);
							digests.Add(digest);
						}
					}
				}
			}
		}

		public string ExportDigests() {
			string data = "";
			foreach (Digest digest in digests) {
				data += digest.ToString() + "\n";
			}
			return data;
		}
	}
}
