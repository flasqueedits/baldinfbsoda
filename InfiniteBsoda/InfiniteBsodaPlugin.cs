using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BaldiInfiniteBsoda
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class InfiniteBsodaPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mod.baldi.infinitebsoda";
        public const string PluginName = "Infinite BSODA";
        public const string PluginVersion = "1.0.0";
        private const int BsodaId = 4;

        private Harmony _harmony;
        private GameControllerScript _gc;
        private FieldInfo _texField;
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<KeyCode> _toggleKey;

        private void Awake()
        {
            _enabled = Config.Bind("General", "Enabled", true, "Sonsuz BSODA acik/kapali");
            _toggleKey = Config.Bind("General", "ToggleKey", KeyCode.F6, "Acma/kapama tusu");
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Logger.LogInfo(PluginName + " v" + PluginVersion + " yuklendi.");
        }

        private void OnDestroy()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey.Value))
            {
                _enabled.Value = !_enabled.Value;
                Logger.LogInfo("Infinite BSODA: " + (_enabled.Value ? "ACIK" : "KAPALI"));
            }
            if (!_enabled.Value) return;

            if (_gc == null)
            {
                _gc = FindObjectOfType<GameControllerScript>();
                if (_gc == null) return;
                ResolveTextureField();
            }

            int[] items = _gc.item;
            if (items == null) return;

            RawImage[] slots = _gc.itemSlot;
            Texture[] tex = _texField != null ? _texField.GetValue(_gc) as Texture[] : null;
            Texture bsoda = (tex != null && BsodaId < tex.Length) ? tex[BsodaId] : null;

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] != BsodaId) items[i] = BsodaId;

                if (bsoda != null && slots != null && i < slots.Length &&
                    slots[i] != null && slots[i].texture != bsoda)
                    slots[i].texture = bsoda;
            }
        }

        // Oyun surumune gore alan adi "useableItems" veya "itemTextures" olabilir.
        private void ResolveTextureField()
        {
            const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type t = typeof(GameControllerScript);
            _texField = t.GetField("useableItems", f) ?? t.GetField("itemTextures", f);
            if (_texField == null)
                Logger.LogWarning("Esya doku dizisi bulunamadi; HUD ikonlari guncellenmeyecek.");
        }
    }
}
