using System.Reflection;
using HarmonyLib;
using UnityEngine.Scripting;

namespace DSWaterDouse
{
    [Preserve]
    public class ModInit : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            Log.Out("[DSDouse] Water Douse (server) initializing...");
            DouseConfig.Load(_modInstance.Path + "/DouseConfig.xml");
            NetPackageDSDouse.Handler = (player, meters, fullClear) => DouseApply.Apply(player, meters, fullClear, validateItems: true);
            var harmony = new Harmony("DSWaterDouse.Server");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Out("[DSDouse] Water Douse (server) initialized.");
        }
    }
}
