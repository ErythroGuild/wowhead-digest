using System;
using System.Collections.Generic;
using System.Text;

namespace wowhead_digest {
	class GuildData {
		public Settings settings;
		public Dictionary<DateTime, Digest> digests;
		public List<string> ids_hidden;
		public List<string> ids_shown;
		public List<string> ids_spoiler;
		public List<string> ids_unspoiler;
	}
}
