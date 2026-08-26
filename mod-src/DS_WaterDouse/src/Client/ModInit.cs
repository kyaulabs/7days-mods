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
            Log.Out("[DSDouse] Water Douse (client) initializing...");
            DouseConfig.Load(_modInstance.Path + "/DouseConfig.xml");
            var harmony = new Harmony("DSWaterDouse.Client");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Out("[DSDouse] Water Douse (client) initialized, patches applied.");
        }
    }
}
