using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DSWaterDouse
{
    /// <summary>
    /// Runtime config for the Water Douse mod (DouseConfig.xml). Loaded by BOTH the
    /// server and client assemblies at mod init.
    /// </summary>
    public class DouseConfig
    {
        public static DouseConfig Instance = new DouseConfig();

        /// <summary>Meters of scent radius removed per douse with regular (non-pure) water.</summary>
        public float DefaultMetersRemoved = 25f;

        /// <summary>Hard cap for any single douse. Also the server-side anti-cheat clamp.</summary>
        public float MaxMetersRemoved = 100f;

        /// <summary>Sound group played when dousing (any sound from sounds.xml).</summary>
        public string SoundName = "bucketpour_concrete";

        /// <summary>Log every accepted douse to the server log.</summary>
        public bool DebugLogging = false;

        /// <summary>
        /// Refund the item's empty container (the Eat action's Create_item, e.g.
        /// drinkJarEmpty) when dousing, mirroring the vanilla drink's jar refund.
        /// The douse action itself skips the item action, so without this every
        /// douse destroys the jar. Note: the vanilla drink only refunds per the
        /// game's JarRefund sandbox chance (default 60%); the douse refunds
        /// unconditionally when this is enabled.
        /// </summary>
        public bool RefundEmptyJar = true;

        /// <summary>Item property (items.xml): meters of scent removed by this item.</summary>
        public const string PropMeters = "DouseSmellMeters";

        /// <summary>Item property (items.xml): this item washes off ALL scent (pure water).</summary>
        public const string PropFullClear = "DouseSmellFull";

        public static void Load(string path)
        {
            var cfg = new DouseConfig();
            try
            {
                if (File.Exists(path))
                {
                    var root = XDocument.Load(path).Root;
                    if (root != null)
                    {
                        cfg.DefaultMetersRemoved = GetFloat(root, "DefaultMetersRemoved", cfg.DefaultMetersRemoved);
                        cfg.MaxMetersRemoved = GetFloat(root, "MaxMetersRemoved", cfg.MaxMetersRemoved);
                        cfg.SoundName = GetString(root, "SoundName", cfg.SoundName);
                        cfg.DebugLogging = GetBool(root, "DebugLogging", cfg.DebugLogging);
                        cfg.RefundEmptyJar = GetBool(root, "RefundEmptyJar", cfg.RefundEmptyJar);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("[DSDouse] Failed to load DouseConfig.xml: " + e);
            }
            Instance = cfg;
            Log.Out("[DSDouse] Config loaded: default meters " + cfg.DefaultMetersRemoved +
                    ", max meters " + cfg.MaxMetersRemoved + ", sound " + cfg.SoundName +
                    ", debug " + cfg.DebugLogging + ", jar refund " + cfg.RefundEmptyJar);
        }

        // ---- item interpretation (shared by client UI and server validation) ----

        /// <summary>True if the item carries a douse property (gets the Douse context entry).</summary>
        public static bool IsDouseable(ItemClass ic)
        {
            return ic != null && ic.Properties != null &&
                   (ic.Properties.Contains(PropMeters) || ic.Properties.Contains(PropFullClear));
        }

        /// <summary>True if this item washes off ALL scent (pure water).</summary>
        public static bool IsFullClear(ItemClass ic)
        {
            return ic != null && ic.Properties != null && ic.Properties.GetBool(PropFullClear);
        }

        /// <summary>Meters of scent this item removes (per-item override or config default).</summary>
        public static float MetersFor(ItemClass ic)
        {
            if (ic != null && ic.Properties != null && ic.Properties.Contains(PropMeters))
            {
                float v = ic.Properties.GetFloat(PropMeters);
                if (v > 0f) return v;
            }
            return Instance.DefaultMetersRemoved;
        }

        // ---- xml helpers (Descendants: values may live under nested groups) ----

        private static float GetFloat(XElement root, string name, float def)
        {
            var el = root.Descendants(name).FirstOrDefault();
            if (el == null || string.IsNullOrEmpty(el.Value)) return def;
            return float.TryParse(el.Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        private static bool GetBool(XElement root, string name, bool def)
        {
            var el = root.Descendants(name).FirstOrDefault();
            if (el == null || string.IsNullOrEmpty(el.Value)) return def;
            return bool.TryParse(el.Value, out var v) ? v : def;
        }

        private static string GetString(XElement root, string name, string def)
        {
            var el = root.Descendants(name).FirstOrDefault();
            if (el == null || string.IsNullOrEmpty(el.Value)) return def;
            return el.Value.Trim();
        }
    }
}
