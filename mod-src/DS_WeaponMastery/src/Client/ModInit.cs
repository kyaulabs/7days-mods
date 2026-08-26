using System.Reflection;
using HarmonyLib;
using UnityEngine.Scripting;

namespace DSWeaponMastery
{
    [Preserve]
    public class ModInit : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            Log.Out("[DSWM] Weapon Mastery (client) initializing...");
            DsConfig.Load(_modInstance.Path + "/DSConfig.xml");
            var harmony = new Harmony("DSWeaponMastery.Client");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Out("[DSWM] Weapon Mastery (client) initialized, patches applied.");
        }
    }
}
