using System.Reflection;
using HarmonyLib;

namespace VanillaPlus
{
    public class ModInit : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            CrateGuardConfig.Load(_modInstance.Path + "/CrateGuardConfig.xml");
            var harmony = new Harmony("VanillaPlus.CrateGuards");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Out("[Vanilla+] Crate Guards initialized: trigger {0}m, {1} zombies, group '{2}'",
                CrateGuardConfig.Instance.TriggerDistance,
                CrateGuardConfig.Instance.ZombieCount,
                CrateGuardConfig.Instance.EntityGroup);
        }
    }
}
