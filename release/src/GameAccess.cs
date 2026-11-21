using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BaldiInfiniteBsoda
{
	/// <summary>
	/// Sürüm-bağımsız oyun erişim katmanı.
	///
	/// Amaç: oyunun hangi Unity sürümünde / class yapısında derlendiğini bilmeden,
	/// ilgili sınıf, alan ve metodları runtime'da reflection ile bulmak.
	/// Örn: stamine alanı "stamina" (PlayerScript) veya "staminaMax" (PlayerMovement)
	/// olabilir; "itemTextures" veya "useableItems" olabilir; hepsini dener.
	///
	/// Tüm bulgular cache'lenir; her karede reflection aranmaz.
	/// </summary>
	internal static class GameAccess
	{
		// Aday isimler: farklı sürümlerdeki muhtemel adlar.
		private static readonly string[] GameControllers =
			{ "GameControllerScript", "GameController", "GameManagerScript", "GameManager" };

		private static readonly string[] Players =
			{ "PlayerScript", "PlayerMovement", "PlayerFunctions" };

		private static readonly string[] StaminaFields = { "stamina" };
		private static readonly string[] StaminaMaxFields = { "maxStamina", "staminaMax", "max_stamina" };
		private static readonly string[] StaminaBarFields = { "staminaBar", "staminaSlider" };

		private static readonly string[] ItemArrayFields = { "item", "items", "inventory" };
		private static readonly string[] ItemSlotFields = { "itemSlot", "itemSlots", "slots" };
		private static readonly string[] IconArrayFields = { "itemTextures", "useableItems", "itemIcons", "icons" };

		private static readonly string[] NotebooksFields = { "notebooks", "notebookCount", "booksCollected" };
		private static readonly string[] FinaleModeFields = { "finaleMode", "finale" };

		// Bulunan tipler / üyeler (null = bulunamadı).
		private static Type _gcType;
		private static Type _playerType;

		private static bool _gcFound;
		private static bool _playerFound;

		// Alan/member cache: "TypeName.MemberName" -> FieldInfo / MethodInfo
		private static readonly Dictionary<string, FieldInfo> FieldCache = new Dictionary<string, FieldInfo>();
		private static readonly Dictionary<string, MethodInfo> MethodCache = new Dictionary<string, MethodInfo>();

		private static MonoBehaviour _gcInstance;
		private static MonoBehaviour _playerInstance;

		/// <summary>İlk çağrıda tipleri ve instance'ları çözer; sonraki çağlarda cache.</summary>
		public static void EnsureResolved()
		{
			if (!_gcFound && _gcType == null)
			{
				_gcType = FindType(GameControllers);
				_gcFound = _gcType != null;
			}
			if (!_playerFound && _playerType == null)
			{
				_playerType = FindType(Players);
				_playerFound = _playerType != null;
			}

			// Instance'lar sahne değişiminde yok olabilir; null ise yeniden bul.
			if (_gcInstance == null && _gcType != null)
				_gcInstance = UnityEngine.Object.FindObjectOfType(_gcType) as MonoBehaviour;
			if (_playerInstance == null && _playerType != null)
				_playerInstance = UnityEngine.Object.FindObjectOfType(_playerType) as MonoBehaviour;
		}

		public static MonoBehaviour Gc => _gcInstance;
		public static MonoBehaviour Player => _playerInstance;
		public static bool HasGameController => _gcInstance != null;
		public static bool HasPlayer => _playerInstance != null;

		/// <summary>Sahnedeki GameController instance'ını zorla tazele (yeni sahne için).</summary>
		public static void RefreshInstances()
		{
			_gcInstance = null;
			_playerInstance = null;
			EnsureResolved();
		}

		// ---------------------------------------------------------- tip / üye --

		private static Type FindType(string[] candidates)
		{
			foreach (string name in candidates)
			{
				Type t = AccessTools.TypeByName(name);
				if (t != null) return t;
			}
			// Son çare: yüklü tüm assembly'lerde ara.
			foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				foreach (string name in candidates)
				{
					try
					{
						Type t = asm.GetType(name, false);
						if (t != null) return t;
					}
					catch { /* bozuk assembly'yi atla */ }
				}
			}
			return null;
		}

		private static FieldInfo GetField(Type type, string[] names)
		{
			if (type == null) return null;
			foreach (string name in names)
			{
				string key = type.Name + "." + name;
				if (FieldCache.TryGetValue(key, out FieldInfo cached)) return cached;

				FieldInfo f = AccessTools.Field(type, name);
				FieldCache[key] = f;
				if (f != null) return f;
			}
			return null;
		}

		private static MethodInfo GetMethod(Type type, string[] names)
		{
			if (type == null) return null;
			foreach (string name in names)
			{
				string key = type.Name + "." + name;
				if (MethodCache.TryGetValue(key, out MethodInfo cached)) return cached;

				MethodInfo m = AccessTools.Method(type, name);
				MethodCache[key] = m;
				if (m != null) return m;
			}
			return null;
		}

		// ------------------------------------------------------- alan okuma --

		public static float GetFloat(MonoBehaviour target, string[] names, float fallback = 0f)
		{
			if (target == null) return fallback;
			FieldInfo f = GetField(target.GetType(), names);
			if (f == null) return fallback;
			object v = f.GetValue(target);
			return v is float fl ? fl : (v is int i ? i : fallback);
		}

		public static void SetFloat(MonoBehaviour target, string[] names, float value)
		{
			if (target == null) return;
			FieldInfo f = GetField(target.GetType(), names);
			if (f == null) return;
			try
			{
				if (f.FieldType == typeof(float)) f.SetValue(target, value);
				else if (f.FieldType == typeof(int)) f.SetValue(target, Mathf.RoundToInt(value));
			}
			catch { /* salt-readonly / uyumsuz tipte yut */ }
		}

		public static int GetInt(MonoBehaviour target, string[] names, int fallback = 0)
		{
			if (target == null) return fallback;
			FieldInfo f = GetField(target.GetType(), names);
			if (f == null) return fallback;
			object v = f.GetValue(target);
			return v is int i ? i : (v is float fl ? Mathf.RoundToInt(fl) : fallback);
		}

		public static void SetInt(MonoBehaviour target, string[] names, int value)
		{
			if (target == null) return;
			FieldInfo f = GetField(target.GetType(), names);
			if (f == null) return;
			try
			{
				if (f.FieldType == typeof(int)) f.SetValue(target, value);
				else if (f.FieldType == typeof(float)) f.SetValue(target, (float)value);
			}
			catch { }
		}

		public static bool HasField(MonoBehaviour target, string[] names)
		{
			if (target == null) return false;
			return GetField(target.GetType(), names) != null;
		}

		public static void SetBool(MonoBehaviour target, string[] names, bool value)
		{
			if (target == null) return;
			FieldInfo f = GetField(target.GetType(), names);
			if (f == null || f.FieldType != typeof(bool)) return;
			try { f.SetValue(target, value); }
			catch { }
		}

		public static bool GetBool(MonoBehaviour target, string[] names, bool fallback = false)
		{
			if (target == null) return fallback;
			FieldInfo f = GetField(target.GetType(), names);
			if (f == null) return fallback;
			object v = f.GetValue(target);
			return v is bool b ? b : fallback;
		}

		/// <summary>TMP_Text / UI text'in ".text" değerini okur (finale eşiği tespiti için).</summary>
		public static string GetText(MonoBehaviour target, string[] names, string fallback = null)
		{
			if (target == null) return fallback;
			FieldInfo f = GetField(target.GetType(), names);
			if (f == null) return fallback;
			object txt = f.GetValue(target);
			if (txt == null) return fallback;
			PropertyInfo p = txt.GetType().GetProperty("text");
			if (p == null) return fallback;
			try { return p.GetValue(txt, null) as string ?? fallback; }
			catch { return fallback; }
		}

		/// <summary>String alan yazar (ör. mode = "story").</summary>
		public static void SetMode(MonoBehaviour target, string[] names, string value)
		{
			if (target == null) return;
			FieldInfo f = GetField(target.GetType(), names);
			if (f == null || f.FieldType != typeof(string)) return;
			try { f.SetValue(target, value); }
			catch { }
		}

		// ------------------------------------------------------- alan dizileri --

		/// <summary>Oyuncunun envanter dizisini (int[]) döner; bulunamazsa null.</summary>
		public static int[] GetItemArray()
		{
			if (_gcInstance == null) return null;
			FieldInfo f = GetField(_gcInstance.GetType(), ItemArrayFields);
			return f?.GetValue(_gcInstance) as int[];
		}

		/// <summary>HUD slot RawImage[] dizisini döner (itemSlot).</summary>
		public static Array GetSlotArray()
		{
			if (_gcInstance == null) return null;
			FieldInfo f = GetField(_gcInstance.GetType(), ItemSlotFields);
			return f?.GetValue(_gcInstance) as Array;
		}

		/// <summary>Eşya ikonu Texture[] dizisini döner (itemTextures/useableItems).</summary>
		public static Array GetIconArray()
		{
			if (_gcInstance == null) return null;
			FieldInfo f = GetField(_gcInstance.GetType(), IconArrayFields);
			return f?.GetValue(_gcInstance) as Array;
		}

		/// <summary>Slot elemanının "texture" alanına değer atar (RawImage.texture).</summary>
		public static void SetSlotTexture(object slotObj, object texture)
		{
			if (slotObj == null || texture == null) return;
			Type t = slotObj.GetType();
			FieldInfo f = GetField(t, new[] { "texture", "m_Texture" });
			if (f != null)
			{
				try { if (f.FieldType.IsInstanceOfType(texture)) f.SetValue(slotObj, texture); }
				catch { }
				return;
			}
			PropertyInfo p = AccessTools.Property(t, "texture");
			if (p != null && p.CanWrite)
			{
				try { if (p.PropertyType.IsInstanceOfType(texture)) p.SetValue(slotObj, texture, null); }
				catch { }
			}
		}

		// ---------------------------------------------------------- metodlar --

		/// <summary>GameController metodunu çağırır (CollectNotebook, ActivateFinaleMode, ...).</summary>
		public static bool InvokeGc(string[] names, params object[] args)
		{
			if (_gcInstance == null) return false;
			MethodInfo m = GetMethod(_gcInstance.GetType(), names);
			if (m == null) return false;
			try { m.Invoke(_gcInstance, args); return true; }
			catch (Exception e)
			{
				BaldiInfiniteBsodaPlugin.Log?.LogWarning($"Çağrı başarısız ({names[0]}): {e.InnerException?.Message ?? e.Message}");
				return false;
			}
		}

		/// <summary>String alanına doğrudan yazar (guiltType, mode vb.).</summary>
		public static void SetTextValue(MonoBehaviour target, string[] names, string value)
		{
			if (target == null) return;
			FieldInfo f = GetField(target.GetType(), names);
			if (f != null && f.FieldType == typeof(string)) f.SetValue(target, value);
		}

		/// <summary>Verilen MonoBehaviour üzerinde parametresiz metot çağırır (UnlockDoor, Raise vb.).</summary>
		public static bool InvokeOn(MonoBehaviour target, string[] names)
		{
			if (target == null) return false;
			MethodInfo m = GetMethod(target.GetType(), names);
			if (m == null || m.GetParameters().Length != 0) return false;
			try { m.Invoke(target, null); return true; }
			catch { return false; }
		}

		/// <summary>Player metodunu çağırır (ResetGuilt, ...).</summary>
		public static bool InvokePlayer(string[] names, params object[] args)
		{
			if (_playerInstance == null) return false;
			MethodInfo m = GetMethod(_playerInstance.GetType(), names);
			if (m == null) return false;
			try { m.Invoke(_playerInstance, args); return true; }
			catch { return false; }
		}

		// ---------------------------------------------------------- alan adları (diğer katmanlar için) --

		public static string[] StaminaField => StaminaFields;
		public static string[] StaminaMaxField => StaminaMaxFields;
		public static string[] StaminaBarField => StaminaBarFields;
		public static string[] NotebooksField => NotebooksFields;
		public static string[] FinaleModeField => FinaleModeFields;

		/// <summary>Stamine barı (Slider) nesnesini döner; yoksa null. UI katmanı için.</summary>
		public static object GetStaminaBar()
		{
			if (_playerInstance == null) return null;
			FieldInfo f = GetField(_playerInstance.GetType(), StaminaBarFields);
			return f?.GetValue(_playerInstance);
		}

		/// <summary>Oyunun kendi TMP fontunu (COMIC PRO) bir TMP_Text örneğinden alır.</summary>
		public static object GetGameFontAsset()
		{
			if (_gcInstance == null) return null;
			// notebookCount / itemText gibi oyunun kendi TMP text bileşenlerinden fontu "kopyala".
			string[] textFields = { "notebookCount", "itemText", "warning", "highScoreText" };
			foreach (string name in textFields)
			{
				FieldInfo f = GetField(_gcInstance.GetType(), new[] { name });
				if (f == null) continue;
				object txt = f.GetValue(_gcInstance);
				if (txt == null) continue;
				// TMP_Text.font özelliğini reflection ile oku.
				PropertyInfo fontProp = txt.GetType().GetProperty("font");
				if (fontProp == null) continue;
				object font = fontProp.GetValue(txt, null);
				if (font != null) return font;
			}
			return null;
		}
	}
}
