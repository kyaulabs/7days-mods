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
            new Harmony("DSZipline.Server").PatchAll(Assembly.GetExecutingAssembly());
            Log.Out("[DSZipline] V0.3.6 movement spike (server) initialized.");
        }
    }
}
