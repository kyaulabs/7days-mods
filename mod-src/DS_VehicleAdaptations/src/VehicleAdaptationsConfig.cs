using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace VehicleAdaptations
{
    public sealed class VehicleAdaptationsConfig
    {
        public static VehicleAdaptationsConfig Instance = new VehicleAdaptationsConfig();

        public float IgnitionDamagePercent = 75f;
        public float FireFuseSeconds = 5f;
        public float RespawnPlayerClearRadius = 32f;
        public string FireParticle = "campfire";
        public bool DebugLogging;

        public static void Load(string path)
        {
            var config = new VehicleAdaptationsConfig();
            try
            {
                if (File.Exists(path))
                {
                    XElement root = XDocument.Load(path).Root;
                    if (root != null)
                    {
                        config.IgnitionDamagePercent = Clamp(
                            GetFloat(root, "IgnitionDamagePercent", config.IgnitionDamagePercent), 1f, 100f);
                        config.FireFuseSeconds = Clamp(
                            GetFloat(root, "FireFuseSeconds", config.FireFuseSeconds), 0.5f, 60f);
                        config.RespawnPlayerClearRadius = Clamp(
                            GetFloat(root, "RespawnPlayerClearRadius", config.RespawnPlayerClearRadius), 0f, 256f);
                        config.FireParticle = GetString(root, "FireParticle", config.FireParticle);
                        config.DebugLogging = GetBool(root, "DebugLogging", config.DebugLogging);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("[VehicleAdaptations] Failed to load config: " + e);
            }

            Instance = config;
            Log.Out("[VehicleAdaptations] Config: ignite at " +
                    config.IgnitionDamagePercent.ToString("0.#", CultureInfo.InvariantCulture) +
                    "% damage, fuse " + config.FireFuseSeconds.ToString("0.#", CultureInfo.InvariantCulture) +
                    " s, respawn clear radius " +
                    config.RespawnPlayerClearRadius.ToString("0.#", CultureInfo.InvariantCulture) + " m.");
        }

        private static float GetFloat(XElement root, string name, float fallback)
        {
            XElement element = root.Descendants(name).FirstOrDefault();
            if (element == null) return fallback;
            return float.TryParse(element.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : fallback;
        }

        private static string GetString(XElement root, string name, string fallback)
        {
            string value = root.Descendants(name).FirstOrDefault()?.Value?.Trim();
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private static bool GetBool(XElement root, string name, bool fallback)
        {
            XElement element = root.Descendants(name).FirstOrDefault();
            return element != null && bool.TryParse(element.Value, out bool value) ? value : fallback;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
