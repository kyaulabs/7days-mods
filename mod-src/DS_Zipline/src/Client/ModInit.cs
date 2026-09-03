using System.Reflection;
using HarmonyLib;
using UnityEngine.Scripting;

namespace DSZipline
{
    [Preserve]
    public class ModInit : IModApi
    {
        public void InitMod(Mod modInstance)
        {
            Log.Out("[DSZipline] V0.3.5 movement spike (client) initializing...");
            ZiplineRideBridge.StartRide = ZiplineRider.TryStart;
            new Harmony("DSZipline.Client").PatchAll(Assembly.GetExecutingAssembly());
            ZiplineArt.Initialize(modInstance.Path);
            Log.Out("[DSZipline] Client spike initialized.");
        }
    }
}
