using UnityEngine;

namespace DSWaterDouse
{
    /// <summary>
    /// Applies a douse on the dousing player's own client and reports it to the
    /// server.
    ///
    /// Dedicated server: the local stealth state is client-computed
    /// (PlayerStealth.SmellTickClient), so the radius cut is mirrored here for the
    /// local struct; the server applies the authoritative cut (zombie AI + "N M"
    /// display) via NetPackageDSDouse.
    ///
    /// SP / listen-server host: the client world IS the server world and there is no
    /// net package channel (offline mode), so the shared DouseApply runs directly
    /// in-process instead — exactly one application. (On a listen host the item was
    /// already consumed from the shared inventory, hence validateItems: false.)
    /// </summary>
    public static class DouseClient
    {
        public static float ApplyDouse(EntityPlayerLocal player, bool fullClear, float meters)
        {
            // Current scent for feedback. Use the "smell" cvar: the authoritative
            // value on both dedicated and SP.
            float current = player.Buffs.GetCustomVar("smell");

            if (SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                // SP / listen host: apply in-process (no package channel in offline
                // mode; the item was already consumed, so skip the item validation).
                DouseApply.Apply(player, meters, fullClear, validateItems: false);
            }
            else
            {
                // Dedicated server (or remote player on a listen server): mirror the
                // cut on the local client struct, then let the server-side handler
                // apply its authoritative cut (validates the item, clamps).
                if (player.world.IsRemote())
                {
                    ref PlayerStealth stealth = ref player.Stealth;
                    StealthAccess.ReduceRadius(ref stealth, fullClear, meters);
                    // Force an immediate target recompute + re-send: the last target
                    // we sent still includes the (now washed-off) eating smell, and
                    // without this the server would keep re-growing the aura from the
                    // stale target.
                    StealthAccess.SetSmellUpdateItemsTicks(ref stealth, 0);
                }
                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                    NetPackageManager.GetPackage<NetPackageDSDouse>().Setup(meters, fullClear));
            }

            if (fullClear) return Mathf.Max(0f, current);
            return Mathf.Min(meters, Mathf.Max(0f, current));
        }
    }
}
