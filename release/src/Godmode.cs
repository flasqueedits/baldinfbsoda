using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BaldiInfiniteBsoda
{
	/// <summary>
	/// Godmode: NPC'ler (Baldi, Playtime, Principal, Bully, ...) oyuncuya dokunamaz.
	///
	/// Nasıl çalışır (sürüm-bağımsız):
	///  - PlayerScript.OnTriggerEnter / OnTriggerStay / OnTriggerExit metodlarına
	///    Harmony PREFIX eklenir. Godmode aktifse prefix false dönüp orijinali
	///    hiç çalıştırmaz (Baldi game-over veremez, Playtime ip atamaz, ...).
	///  - Ek güvence: gameOver=false ve guilt=0 her karede zorlanır.
	///
	/// Süre: config ile ayarlanabilir; 0 = SÜRESİZ (varsayılan). Süresizken HUD'da
	/// "GODMODE ∞" gösterilir; süreliyken COMIC PRO fontuyla geri sayım görünür.
	/// </summary>
	internal static class Godmode
	{
		// PlayerScript zarar metodları (farklı adaylar; sürüm bağımsız).
		private static readonly string[] TriggerEnterMethods = { "OnTriggerEnter" };
		private static readonly string[] TriggerStayMethods = { "OnTriggerStay" };
		private static readonly string[] TriggerExitMethods = { "OnTriggerExit" };

		private static readonly string[] GameOverField = { "gameOver" };
		private static readonly string[] GuiltField = { "guilt" };

		public static bool Active { get; private set; }

		/// <summary>true = süre sınırı yok (süresiz godmode).</summary>
		public static bool Infinite { get; private set; }

		public static float Duration { get; private set; } = 60f;

		public static float Remaining => _remaining;
		private static float _remaining;

		private static Harmony _harmony;
		private static bool _patchesApplied;

		// ---- Geri sayım HUD (COMIC PRO) ----
		private static GameObject _hudRoot;
		private static TextMeshProUGUI _label;

		/// <summary>Godmode'ı açar. durationSeconds &lt;= 0 ise SÜRESİZ mod.</summary>
		public static void Activate(float durationSeconds)
		{
			Infinite = durationSeconds <= 0f;
			Duration = Infinite ? 0f : durationSeconds;
			_remaining = Infinite ? float.MaxValue : Duration;
			Active = true;

			ApplyPatches();
			EnsureHud();
			UpdateHudText();

			BaldiInfiniteBsodaPlugin.LogBoth(Infinite
				? L.C("Godmode ON (infinite) — NPCs cannot touch you until you leave the game.",
					"Godmode AÇIK (süresiz) — oyundan çıkana kadar NPC'ler dokunamaz.")
				: string.Format(L.C("Godmode ON ({0}s) — NPCs cannot touch you.",
					"Godmode AÇIK ({0} sn) — NPC'ler dokunamaz."), Duration.ToString("0")));
		}

		public static void Deactivate()
		{
			if (!Active) return;
			Active = false;
			Infinite = false;
			if (_hudRoot != null) _hudRoot.SetActive(false);
			// Yama'ları geri al (FPS'e yük olmasın).
			RemovePatches();
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Godmode OFF", "Godmode KAPANDI"));
		}

		/// <summary>Her karede çağrılır: geri sayımı ilerletir + ek güvenceleri uygular.</summary>
		public static void Tick()
		{
			if (!Active) return;

			// Oyundan çıkınca / sahne değişince GameController yok olur -> godmode + HUD gider.
			if (!GameAccess.HasGameController)
			{
				Deactivate();
				BaldiInfiniteBsodaPlugin.LogBoth(L.C("GameController gone -> godmode auto-off",
					"GameController yok oldu -> godmode otomatik kapatıldı"));
				return;
			}

			if (!Infinite)
			{
				_remaining -= Time.unscaledDeltaTime;
				UpdateHudText();

				if (_remaining <= 0f)
				{
					Deactivate();
					BaldiInfiniteBsodaPlugin.LogBoth(L.C("Godmode expired — NPCs are back to normal",
						"Godmode süresi doldu — NPC etkileşimi normale döndü"));
					return;
				}
			}

			// Ek güvence: oyun "gameOver" veya "guilt" yoluyla zarar veremez.
			if (GameAccess.HasPlayer)
			{
				GameAccess.SetBool(GameAccess.Player, GameOverField, false);
				GameAccess.SetFloat(GameAccess.Player, GuiltField, 0f);
			}
		}

		// ------------------------------------------------------------- yamalar --

		private static void ApplyPatches()
		{
			if (_patchesApplied) return;

			Type playerType = FindPlayerType();
			if (playerType == null) return;

			_harmony = new Harmony("com.mod.baldi.infinitebsoda.godmode");
			HarmonyMethod prefix = new HarmonyMethod(typeof(GodmodePrefix), nameof(GodmodePrefix.Prefix));

			TryPatch(playerType, TriggerEnterMethods, prefix);
			TryPatch(playerType, TriggerStayMethods, prefix);
			TryPatch(playerType, TriggerExitMethods, prefix);

			_patchesApplied = true;
		}

		private static void TryPatch(Type type, string[] names, HarmonyMethod prefix)
		{
			foreach (string n in names)
			{
				MethodInfo m = AccessTools.Method(type, n);
				if (m == null) continue;
				try { _harmony.Patch(m, prefix: prefix); }
				catch (Exception e) { BaldiInfiniteBsodaPlugin.Log?.LogDebug($"Patch atlanamadı {type.Name}.{n}: {e.Message}"); }
			}
		}

		private static void RemovePatches()
		{
			if (_harmony == null) return;
			try { _harmony.UnpatchSelf(); }
			catch { }
			_patchesApplied = false;
		}

		private static Type FindPlayerType()
		{
			string[] cands = { "PlayerScript", "PlayerMovement", "PlayerFunctions" };
			foreach (string c in cands)
			{
				Type t = AccessTools.TypeByName(c);
				if (t != null && (t.GetMethod("OnTriggerEnter", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) != null
					|| t.GetMethod("OnTriggerStay", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) != null))
					return t;
			}
			foreach (string c in cands)
			{
				Type t = AccessTools.TypeByName(c);
				if (t != null) return t;
			}
			return null;
		}

		// --------------------------------------------------------------- HUD --

		private static void EnsureHud()
		{
			if (_hudRoot != null)
			{
				_hudRoot.SetActive(true);
				ApplyGameFont();
				return;
			}

			_hudRoot = new GameObject("InfBsoda_GodmodeHUD");
			UnityEngine.Object.DontDestroyOnLoad(_hudRoot);

			Canvas canvas = _hudRoot.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 9999; // en üstte

			GameObject textGo = new GameObject("Label");
			textGo.transform.SetParent(_hudRoot.transform, false);

			_label = textGo.AddComponent<TextMeshProUGUI>();
			_label.fontSize = 40f;
			_label.alignment = TextAlignmentOptions.Center;
			_label.enableWordWrapping = false;
			_label.raycastTarget = false;

			// Üst-ortala
			RectTransform rt = _label.rectTransform;
			rt.anchorMin = new Vector2(0.5f, 1f);
			rt.anchorMax = new Vector2(0.5f, 1f);
			rt.pivot = new Vector2(0.5f, 1f);
			rt.anchoredPosition = new Vector2(0f, -12f);
			rt.sizeDelta = new Vector2(800f, 70f);

			ApplyGameFont();
		}

		/// <summary>Oyunun kendi COMIC PRO fontunu (notebookCount/itemText TMP text'inden) uygular.</summary>
		private static void ApplyGameFont()
		{
			if (_label == null) return;
			try
			{
				object fontAsset = GameAccess.GetGameFontAsset(); // TMP_FontAsset
				if (fontAsset is TMP_FontAsset fa)
				{
					_label.font = fa;
				}
				else
				{
					// Yedek: TMP ayarlarındaki varsayılan font (oyunun default'u = COMIC PRO).
					if (TMP_Settings.defaultFontAsset != null)
						_label.font = TMP_Settings.defaultFontAsset;
				}
			}
			catch (Exception e)
			{
				BaldiInfiniteBsodaPlugin.Log?.LogDebug($"Font uygulanamadı: {e.Message}");
			}
		}

		private static void UpdateHudText()
		{
			if (_label == null) return;
			if (Infinite)
			{
				_label.text = "GODMODE  ∞";
			}
			else
			{
				int total = Mathf.Max(0, Mathf.CeilToInt(_remaining));
				int mm = total / 60;
				int ss = total % 60;
				_label.text = $"GODMODE  {mm}:{ss:00}";
			}
			if (_label.color.a < 1f) _label.color = Color.white;
		}

		/// <summary>Godmode prefix'i: aktifse orijinal trigger metodunu bloklar.</summary>
		internal static class GodmodePrefix
		{
			public static bool Prefix()
			{
				// true = orijinal çalışsın; false = blokla (godmode aktifken NPC dokunamaz).
				return !Active;
			}
		}
	}
}
