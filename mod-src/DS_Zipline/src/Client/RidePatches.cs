using HarmonyLib;

namespace DSZipline
{
    // EntityPlayerLocal.MoveByInput must keep running because it also forwards
    // mouse/controller look deltas to vp_FPCamera. Suppress only the motor's
    // physics step; the rider postfix supplies the authoritative rail position.
    [HarmonyPatch(typeof(vp_FPController), nameof(vp_FPController.FixedUpdate))]
    public static class SuppressControllerFixedMovePatch
    {
        public static bool Prefix(vp_FPController __instance)
        {
            return !ZiplineRider.Controls(__instance.localPlayer);
        }
    }

    [HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.Update))]
    public static class UpdateRidePatch
    {
        public static void Postfix(EntityPlayerLocal __instance)
        {
            ZiplineRider.Tick(__instance);
        }
    }

    [HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.SetDead))]
    public static class StopRideOnDeathPatch
    {
        public static void Prefix(EntityPlayerLocal __instance)
        {
            ZiplineRider.Stop("death");
        }
    }
}
