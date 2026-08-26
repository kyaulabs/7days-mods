using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using JetBrains.Annotations;
using UnityEngine;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.Default | DebuggableAttribute.DebuggingModes.DisableOptimizations | DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints | DebuggableAttribute.DebuggingModes.EnableEditAndContinue)]
[assembly: AssemblyTitle("CommandExtensions")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("The Fun Pimps LLC")]
[assembly: AssemblyProduct("")]
[assembly: AssemblyCopyright("The Fun Pimps LLC")]
[assembly: AssemblyTrademark("")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: AssemblyVersion("0.0.0.0")]
namespace CommandExtensions
{
	[UsedImplicitly]
	public class ModApi : IModApi
	{
		public void InitMod(Mod _modInstance)
		{
		}
	}
	public static class ChatHelpers
	{
		public static void SendMessage(ClientInfo _receiver, ClientInfo _sender, string _message)
		{
			string text;
			if (_sender != null)
			{
				PrivateMessageConnections.SetLastPMSender(_sender, _receiver);
				text = _sender.playerName;
				_receiver.SendPackage(NetPackageManager.GetPackage<NetPackageChat>().Setup(EChatType.Whisper, _sender.entityId, _message, null, EMessageSender.SenderIdAsPlayer, GeneratedTextManager.BbCodeSupportMode.SupportedAndAddEscapes));
			}
			else
			{
				text = "Server";
				_receiver.SendPackage(NetPackageManager.GetPackage<NetPackageChat>().Setup(EChatType.Whisper, -1, _message, null, EMessageSender.Server, GeneratedTextManager.BbCodeSupportMode.Supported));
			}
			string playerName = _receiver.playerName;
			playerName = ((playerName != null) ? ("\"" + playerName + "\"") : "unknownName");
			SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Message to player " + playerName + " sent with sender \"" + text + "\"");
		}
	}
	public static class PrivateMessageConnections
	{
		private static readonly Dictionary<PlatformUserIdentifierAbs, PlatformUserIdentifierAbs> senderOfLastPM = new Dictionary<PlatformUserIdentifierAbs, PlatformUserIdentifierAbs>();

		public static void SetLastPMSender(ClientInfo _sender, ClientInfo _receiver)
		{
			senderOfLastPM[_receiver.InternalId] = _sender.InternalId;
		}

		public static ClientInfo GetLastPMSenderForPlayer(ClientInfo _player)
		{
			if (!senderOfLastPM.TryGetValue(_player.InternalId, out var value))
			{
				return null;
			}
			return SingletonMonoBehaviour<ConnectionManager>.Instance.Clients.ForUserId(value);
		}
	}
}
namespace CommandExtensions.Commands
{
	[UsedImplicitly]
	public class TestLogSpam : ConsoleCmdAbstract
	{
		private Coroutine spamCoroutine;

		private WaitForSeconds waitObj;

		public override bool AllowedInMainMenu => true;

		public override bool IsExecuteOnClient => true;

		public override string[] getCommands()
		{
			return new string[1] { "tls" };
		}

		public override string getDescription()
		{
			return "Spams the log with until stopped";
		}

		public override string getHelp()
		{
			return "\r\n\t\t\t|Usage:\r\n\t\t\t|  1. tls <N> ['second']\r\n\t\t\t|  2. tls stop\r\n\t\t\t|1. Start spamming with N messages per frame - or per second if the second argument is the word 'second'\r\n\t\t\t|2. Stop spamming\r\n\t\t\t".Unindent();
		}

		public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
		{
			int result;
			if (_params.Count != 1 && _params.Count != 2)
			{
				SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"Wrong number of arguments, expected 1 or 2, found {_params.Count}.");
			}
			else if (_params[0].EqualsCaseInsensitive("stop"))
			{
				if (spamCoroutine == null)
				{
					SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Not spamming.");
					return;
				}
				ThreadManager.StopCoroutine(spamCoroutine);
				spamCoroutine = null;
				SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Spam stopped.");
			}
			else if (!int.TryParse(_params[0], out result))
			{
				SingletonMonoBehaviour<SdtdConsole>.Instance.Output("The given spam number is not a valid integer");
			}
			else
			{
				bool flag = _params.Count > 1 && _params[1] == "second";
				waitObj = (flag ? new WaitForSeconds(1f) : null);
				SingletonMonoBehaviour<SdtdConsole>.Instance.Output(string.Format("Started spamming {0} messages per {1}", result, flag ? "second" : "frame"));
				spamCoroutine = ThreadManager.StartCoroutine(SpamCo(result));
			}
		}

		private IEnumerator SpamCo(int _count)
		{
			do
			{
				for (int i = 0; i < _count; i++)
				{
					Log.Out("This is a spam log message.");
				}
				yield return waitObj;
			}
			while (spamCoroutine != null);
		}
	}
	[UsedImplicitly]
	public class ConsoleCmdException : ConsoleCmdAbstract
	{
		public override bool AllowedInMainMenu => true;

		public override string[] getCommands()
		{
			return new string[1] { "exception" };
		}

		public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
		{
			Log.Out("Test info");
			Log.Warning("Test warning");
			Log.Error("Test error");
			throw new Exception("Test exception");
		}

		public override string getDescription()
		{
			return "Throw an exception / log messages";
		}
	}
	[UsedImplicitly]
	public class Give : ConsoleCmdAbstract
	{
		public override string getDescription()
		{
			return "give an item to a player (entity id or name)";
		}

		public override string getHelp()
		{
			return "Give an item to a player by dropping it in front of that player\nUsage:\n   give <name / entity id> <item name> <amount>\n   give <name / entity id> <item name> <amount> <quality>\nEither pass the full name of a player or his entity id (given by e.g. \"lpi\").\nItem name has to be the exact name of an item as listed by \"listitems\".\nAmount is the number of instances of this item to drop (as a single stack).\nQuality is the quality of the dropped items for items that have a quality.";
		}

		public override string[] getCommands()
		{
			return new string[2]
			{
				"give",
				string.Empty
			};
		}

		public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
		{
			if (_params.Count != 3 && _params.Count != 4)
			{
				SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"Wrong number of arguments, expected 3 or 4, found {_params.Count}.");
				return;
			}
			ClientInfo clientInfo = ConsoleHelper.ParseParamIdOrName(_params[0]);
			if (clientInfo == null)
			{
				SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Playername or entity id not found.");
				return;
			}
			ItemValue item = ItemClass.GetItem(_params[1], _caseInsensitive: true);
			if (item.type == ItemValue.None.type)
			{
				SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Item not found.");
				return;
			}
			item = new ItemValue(item.type, _bCreateDefaultParts: true);
			if (!int.TryParse(_params[2], out var result) || result <= 0)
			{
				SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Amount is not an integer or not greater than zero.");
				return;
			}
			ushort result2 = 6;
			if (_params.Count == 4 && (!ushort.TryParse(_params[3], out result2) || result2 > 6))
			{
				SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Quality is not an integer or not greater than zero.");
				return;
			}
			if (ItemClass.list[item.type].HasSubItems)
			{
				for (int i = 0; i < item.Modifications.Length; i++)
				{
					ItemValue itemValue = item.Modifications[i];
					itemValue.Quality = result2;
					item.Modifications[i] = itemValue;
				}
			}
			else if (ItemClass.list[item.type].HasQuality)
			{
				item.Quality = result2;
			}
			EntityPlayer entityPlayer = GameManager.Instance.World.Players.dict[clientInfo.entityId];
			ItemStack itemStack = new ItemStack(item, result);
			GameManager.Instance.ItemDropServer(itemStack, entityPlayer.GetPosition(), Vector3.zero);
			SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Dropped item");
		}
	}
	[UsedImplicitly]
	public class ListItems : ConsoleCmdAbstract
	{
		public override string getDescription()
		{
			return "lists all items that contain the given substring";
		}

		public override string[] getCommands()
		{
			return new string[2] { "listitems", "li" };
		}

		public override string getHelp()
		{
			return "List all available item names\nUsage:\n   1. listitems <searchString>\n   2. listitems *\n1. List only names that contain the given string.\n2. List all names.";
		}

		public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
		{
			if (_params.Count != 1 || _params[0].Length == 0)
			{
				SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Usage: listitems <searchString>");
				return;
			}
			int count = ItemClass.ItemNames.Count;
			bool flag = _params[0].Trim().Equals("*");
			int num = 0;
			for (int i = 0; i < count; i++)
			{
				string text = ItemClass.ItemNames[i];
				if (flag || text.IndexOf(_params[0], StringComparison.OrdinalIgnoreCase) >= 0)
				{
					SingletonMonoBehaviour<SdtdConsole>.Instance.Output("    " + text);
					num++;
				}
			}
			SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"Listed {num} matching items.");
		}
	}
	[UsedImplicitly]
	public class Reply : ConsoleCmdAbstract
	{
		public override string getDescription()
		{
			return "send a message to  the player who last sent you a PM";
		}

		public override string getHelp()
		{
			return "Usage:\n   reply <message>\nSend the given message to the user you last received a PM from.";
		}

		public override string[] getCommands()
		{
			return new string[2] { "reply", "re" };
		}

		private void RunInternal(ClientInfo _sender, List<string> _params)
		{
			if (_params.Count < 1)
			{
				SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Usage: reply <message>");
				return;
			}
			string message = _params[0];
			ClientInfo lastPMSenderForPlayer = PrivateMessageConnections.GetLastPMSenderForPlayer(_sender);
			if (lastPMSenderForPlayer != null)
			{
				ChatHelpers.SendMessage(lastPMSenderForPlayer, _sender, message);
			}
			else
			{
				SingletonMonoBehaviour<SdtdConsole>.Instance.Output("You have not received a PM so far or sender of last received PM is no longer online.");
			}
		}

		public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
		{
			if (_senderInfo.RemoteClientInfo == null)
			{
				Log.Out("Command \"reply\" can only be used on clients!");
			}
			else
			{
				RunInternal(_senderInfo.RemoteClientInfo, _params);
			}
		}
	}
	[UsedImplicitly]
	public class SayToPlayer : ConsoleCmdAbstract
	{
		public override string getDescription()
		{
			return "send a message to a single player";
		}

		public override string getHelp()
		{
			return "Usage:\n   pm <player name / steam id / entity id> <message>\nSend a PM to the player given by the player name or entity id (as given by e.g. \"lpi\").";
		}

		public override string[] getCommands()
		{
			return new string[2] { "sayplayer", "pm" };
		}

		private void RunInternal(ClientInfo _sender, List<string> _params)
		{
			if (_params.Count < 2)
			{
				SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Usage: sayplayer <playername|entityid> <message>");
				return;
			}
			string message = _params[1];
			ClientInfo clientInfo = ConsoleHelper.ParseParamIdOrName(_params[0]);
			if (clientInfo != null)
			{
				ChatHelpers.SendMessage(clientInfo, _sender, message);
			}
			else
			{
				SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Playername or entity ID not found.");
			}
		}

		public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
		{
			RunInternal(_senderInfo.RemoteClientInfo, _params);
		}
	}
}
