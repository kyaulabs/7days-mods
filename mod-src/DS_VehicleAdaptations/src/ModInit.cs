using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine.Scripting;

namespace VehicleAdaptations
{
    [Preserve]
    public sealed class ModInit : IModApi
    {
        public void InitMod(Mod modInstance)
        {
            VehicleAdaptationsConfig.Load(
                Path.Combine(modInstance.Path, "VehicleAdaptationsConfig.xml"));

            new Harmony("VehicleAdaptations").PatchAll(Assembly.GetExecutingAssembly());
            VehicleRespawnManager.RegisterEvents();
            Log.Out("[VehicleAdaptations] V1.0.0 initialized.");
        }
    }
}
