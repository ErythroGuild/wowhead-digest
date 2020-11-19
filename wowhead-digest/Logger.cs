using System;
using System.Collections.Generic;

namespace Puck {
	class Logger {
		public enum Type {
			Debug	= 0,
			Info	= 1,
			Warning	= 2,
			Error	= 3,
		};

		static readonly Dictionary<Type, ConsoleColor> type_color =
			new Dictionary<Type, ConsoleColor> {
				{ Type.Debug,   ConsoleColor.DarkGray },
				{ Type.Info,    ConsoleColor.Gray },
				{ Type.Warning, ConsoleColor.Yellow },
				{ Type.Error,   ConsoleColor.Red },
		};
		const string indent_str = "  ";



		// A fancy struct wrapping settings for each logged line.
		class Message {
			public ulong? parent = null;
			public Type type = Type.Info;
			public int indent = 0;
			public string data = "";
		}

		// Settings for the Logger.
		public bool show_timestamp = true;
		public Type type_minimum = Type.Debug;

		// Members used for internal logic.
		Message? message_prev = null;	// used to set indent level

		// The base function for logging.
		// All convenience functions are based on this.
		public void Log(
			string s,
			Type type = Type.Info,
			int indent = 0,
			ulong? parent = null
		) {
			// Skip printing if importance is below minimum.
			if ((int)type < (int)type_minimum)
				{ return; }

			// Add extra padding line if previous parent differs.
			if (message_prev != null) {
				if (message_prev.parent != parent) {
					Console.WriteLine();
				}
			}

			// Optional timestamp.
			if (show_timestamp) {
				Console.ForegroundColor = type_color[Type.Debug];
				Console.Write(DateTime.Now.ToString(@"HH\:mm\:ss") + " ");
				Console.ResetColor();
			}

			// Indent message.
			for (int i=0; i<indent; i++) {
				Console.Write(indent_str);
			}

			// Set color.
			switch (type) {
			case Type.Debug:
			case Type.Info:
				Console.ForegroundColor = type_color[type];
				break;
			case Type.Warning:
				Console.ForegroundColor = type_color[type];
				Console.Write("Warning: ");
				Console.ResetColor();
				break;
			case Type.Error:
				Console.ForegroundColor = type_color[type];
				Console.Write("Error: ");
				Console.ResetColor();
				break;
			}

			// Print message (finally).
			Console.Write(s + "\n");

			// Cleanup.
			Console.ResetColor();
			Message message = new Message {
				parent = parent,
				type = type,
				indent = indent,
				data = s,
			};
			message_prev = message;
		}

		// Syntactic sugar: convenience functions.
		public void Debug(string s, int indent = 0, ulong? parent = null)
			{ Log(s, Type.Debug, indent, parent); }
		public void Info(string s, int indent = 0, ulong? parent = null)
			{ Log(s, Type.Info, indent, parent); }
		public void Warning(string s, int indent = 0, ulong? parent = null)
			{ Log(s, Type.Warning, indent, parent); }
		public void Error(string s, int indent = 0, ulong? parent = null)
			{ Log(s, Type.Error, indent, parent); }

		// Alias for Console.WriteLine().
		public void NewLine() { Console.WriteLine(); }
	}
}
