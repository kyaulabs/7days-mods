using System;
using System.Xml.Linq;

namespace VanillaPlus
{
    /// <summary>
    /// Runtime config for the supply crate guard horde, read from
    /// CrateGuardConfig.xml at the mod root.
    /// </summary>
    public class CrateGuardConfig
    {
        public static CrateGuardConfig Instance = new CrateGuardConfig();

        public float TriggerDistance = 12f;   // meters; triggers when any alive player is closer
        public int ZombieCount = 5;           // zombies spawned around the crate (1..50)
        public string EntityGroup = "ZombiesAll";

        public static void Load(string path)
        {
            var cfg = new CrateGuardConfig();
            try
            {
                var doc = XDocument.Load(path);
                foreach (var p in doc.Root.Elements("property"))
                {
                    string name = (string)p.Attribute("name");
                    string value = (string)p.Attribute("value");
                    if (value == null) continue;
                    if (name == "TriggerDistance" && float.TryParse(value, out float d) && d > 0f)
                        cfg.TriggerDistance = d;
                    else if (name == "ZombieCount" && int.TryParse(value, out int c) && c > 0 && c <= 50)
                        cfg.ZombieCount = c;
                    else if (name == "EntityGroup" && !string.IsNullOrWhiteSpace(value))
                        cfg.EntityGroup = value.Trim();
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Vanilla+] CrateGuardConfig load failed ({0}), using defaults", e.Message);
            }
            Instance = cfg;
        }
    }
}
