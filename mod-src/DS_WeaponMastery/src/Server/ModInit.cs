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
            Log.Out("[DSWM] Weapon Mastery (server) initializing...");
            DsConfig.Load(_modInstance.Path + "/DSConfig.xml");
            ResetTracker.Init(_modInstance.Path);
            NetPackageDSWMUseXp.Handler = UseXp.OnReportedUse;
            var harmony = new Harmony("DSWeaponMastery.Server");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Out("[DSWM] Weapon Mastery (server) initialized, patches applied.");
        }
    }
}
