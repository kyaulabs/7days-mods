using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace DSWeaponMastery
{
    /// <summary>
    /// ProgressionValue level is serialized as a byte in vanilla, which caps levels at 255.
    /// We bump the format version to 2 and write level as UInt16 (max 65535) so weapon
    /// crafting skills can reach 600. Old v1 saves and network data are still read correctly.
    /// This patch must run on BOTH server (writes/reads saves + sends to clients) and
    /// client (reads from server, writes its own progression updates).
    /// </summary>
    [HarmonyPatch(typeof(ProgressionValue))]
    public static class PatchProgressionValueSerialization
    {
        private static readonly FieldInfo FName = AccessTools.Field(typeof(ProgressionValue), "name");
        private static readonly FieldInfo FLevel = AccessTools.Field(typeof(ProgressionValue), "level");
        private static readonly FieldInfo FCost = AccessTools.Field(typeof(ProgressionValue), "costForNextLevel");

        [HarmonyPatch("Read")]
        [HarmonyPrefix]
        public static bool Read(ProgressionValue __instance, BinaryReader _reader)
        {
            byte version = _reader.ReadByte();
            string name = _reader.ReadString();
            int level = version >= 2 ? (int)_reader.ReadUInt16() : (int)_reader.ReadByte();
            int cost = _reader.ReadInt32();
            FName.SetValue(__instance, name);
            FLevel.SetValue(__instance, level);
            FCost.SetValue(__instance, cost);
            return false;
        }

        [HarmonyPatch("Write")]
        [HarmonyPrefix]
        public static bool Write(ProgressionValue __instance, BinaryWriter _writer, bool _IsNetwork)
        {
            _writer.Write((byte)2);
            _writer.Write((string)FName.GetValue(__instance));
            int level = (int)FLevel.GetValue(__instance);
            _writer.Write((ushort)Math.Max(0, Math.Min(65535, level)));
            _writer.Write((int)FCost.GetValue(__instance));
            return false;
        }
    }
}
