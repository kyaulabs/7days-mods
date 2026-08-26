using HarmonyLib.Tools;
using JetBrains.Annotations;

namespace TfpHarmony;

[JetBrains.Annotations.UsedImplicitly]
public class ModInit : IModApi
{
	public void InitMod(Mod _modInstance)
	{
		initHarmony();
		PreserveCheckPatch.Enable();
	}

	private void initHarmony()
	{
		Logger.ChannelFilter = Logger.LogChannel.Warn | Logger.LogChannel.Error | Logger.LogChannel.Debug;
		string launchArgument = GameUtils.GetLaunchArgument("debugharmony");
		if (launchArgument != null)
		{
			Logger.ChannelFilter |= Logger.LogChannel.Info;
			if (launchArgument == "verbose")
			{
				Logger.ChannelFilter |= Logger.LogChannel.IL;
			}
		}
		Logger.MessageReceived += delegate(object _, Logger.LogEventArgs _args)
		{
			switch (_args.LogChannel)
			{
			case Logger.LogChannel.Warn:
				Log.Warning("[MODS][Harmony] " + _args.Message);
				break;
			case Logger.LogChannel.Error:
				Log.Error("[MODS][Harmony] " + _args.Message);
				break;
			case Logger.LogChannel.None:
			case Logger.LogChannel.Info:
			case Logger.LogChannel.IL:
			case Logger.LogChannel.Debug:
			case Logger.LogChannel.All:
				Log.Out("[MODS][Harmony](" + EnumUtils.ToStringCached<Logger.LogChannel>(_args.LogChannel) + ") " + _args.Message);
				break;
			default:
				Log.Error($"[MODS][Harmony] Unknown Harmony log channel ({(int)_args.LogChannel}): {_args.Message}");
				break;
			}
		};
		Log.Out("[MODS]       [Harmony] Init done");
	}
}
