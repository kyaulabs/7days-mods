using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine.Scripting;

namespace TfpHarmony;

public static class PreserveCheckPatch
{
	private class GetTypePatch
	{
		private static Assembly mainGameAssembly = typeof(GameManager).Assembly;

		[HarmonyPatch(typeof(Type), "GetType", new Type[] { typeof(string) })]
		[HarmonyPostfix]
		private static void Type_GetType_Prefix(Type __result, string typeName)
		{
			if (!(__result == null) && __result.Assembly == mainGameAssembly && Attribute.GetCustomAttribute(__result, typeof(PreserveAttribute)) == null)
			{
				Log.Warning("Type:" + __result.AssemblyQualifiedName + " is missing the UnityEngine.Scripting.Preserve attribute, this will fail on consoles");
			}
		}
	}

	private const string HarmonyID = "TFP_7DTD_Harmony";

	public static void Enable()
	{
		Harmony harmony = new Harmony("TFP_7DTD_Harmony");
		harmony.PatchAll(typeof(GetTypePatch));
	}
}
