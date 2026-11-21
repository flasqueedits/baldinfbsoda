using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using BaldiInfiniteBsoda.Ripper;

namespace BaldiInfiniteBsoda
{
	/// <summary>
	/// Baldi's Basics Classic - Cheat Pack (BepInEx 5 + Harmony).
	///
	/// Hileler:
	///   1) Sonsuz Eşya      - alınan her eşya kendi slotunda kalıcı (ID/slot farketmez;
	///                        boş slotlara hiçbir şey eklenmez)
	///   2) Sonsuz Stamina   - stamina hiç bitmez (koşu sonsuz)
	///   3) Finale Mode      - tüm notebooklar toplanmış gibi finale mode'a geçiş
	///   4) Godmode (süreli) - NPC'ler dokunamaz; üstte COMIC PRO geri sayım
	///
	/// Sürüm-bağımsız: oyun sınıfları/alanları runtime'da reflection ile bulunur.
	/// </summary>
	[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
	public class BaldiInfiniteBsodaPlugin : BaseUnityPlugin
	{
		public const string PluginGuid = "com.mod.baldi.infinitebsoda";
		public const string PluginName = "ULTIMATE BALDI CHEAT!";
		public const string PluginVersion = "2.5.0";

		internal static ManualLogSource Log;
		internal static BaldiInfiniteBsodaPlugin Instance;

		private Harmony _harmony;

		// Config
		private ConfigEntry<bool> _bsodaEnabled;
		private ConfigEntry<bool> _staminaEnabled;
		// ESKİ: _forceAllSlots (tüm slotları BSODA yapardı) — kaldırıldı.
		// YENİ: alınan her eşya kalıcıdır; boş slotlara hiçbir şey konmaz.
		private ConfigEntry<float> _godmodeDuration;
		private ConfigEntry<KeyCode> _keyBsoda;
		private ConfigEntry<KeyCode> _keyStamina;
		private ConfigEntry<KeyCode> _keyFinale;
		private ConfigEntry<KeyCode> _keyGodmode;
		private ConfigEntry<KeyCode> _keyMenu;
		private ConfigEntry<KeyCode> _keyConsole;
		private ConfigEntry<string> _language;
		private ConfigEntry<bool> _devAutoExport;

		// Yeni hile config'leri
		private ConfigEntry<float> _speedMultiplier;
		private ConfigEntry<float> _slowMoScale;
		private ConfigEntry<int> _itemId;
		private ConfigEntry<float> _fov;
		private ConfigEntry<float> _audioPitch;
		private ConfigEntry<int> _notebooks;

		// Yeni hotkey config'leri
		private ConfigEntry<KeyCode> _keySpeed;
		private ConfigEntry<KeyCode> _keySlowMo;
		private ConfigEntry<KeyCode> _keyItems;
		private ConfigEntry<KeyCode> _keyNpcFreeze;
		private ConfigEntry<KeyCode> _keyJumpscare;
		private ConfigEntry<KeyCode> _keyNoclip;
		private ConfigEntry<KeyCode> _keyTeleport;
		private ConfigEntry<KeyCode> _keyWallhack;
		private ConfigEntry<KeyCode> _keyCalm;

		// Yeni hile config'leri (2.4.0)
		private ConfigEntry<KeyCode> _keyHighJump;
		private ConfigEntry<KeyCode> _keyFly;
		private ConfigEntry<KeyCode> _keyEnemies;
		private ConfigEntry<KeyCode> _keyNotebooks;
		private ConfigEntry<KeyCode> _keyReset;
		private ConfigEntry<KeyCode> _keyDoors;
		private ConfigEntry<KeyCode> _keyExit;
		private ConfigEntry<KeyCode> _keyDebug;
		private ConfigEntry<KeyCode> _keyEndless;
		private ConfigEntry<float> _jumpMultiplier;
		private ConfigEntry<float> _flySpeed;

		// Durum
		private bool _infiniteStamina;

		private void Awake()
		{
			Log = Logger;

			_bsodaEnabled = Config.Bind("1_BSDOA", "Enabled", true,
				"Sonsuz Eşya: alınan her eşya envanterde kalıcı olur (hangi slot/ID olursa olsun)");
			_staminaEnabled = Config.Bind("2_Stamina", "StartEnabled", false, "Oyun başında sonsuz stamina açık mı");
			_godmodeDuration = Config.Bind("4_Godmode", "DurationSeconds", 0f,
				"Godmode süresi (saniye). 0 = SÜRESIZ (infinite, önerilen)");

			_keyBsoda = Config.Bind("Keys", "Bsoda", KeyCode.F6, "Sonsuz Eşya tuşu (alınan eşyalar kalıcı)");
			_keyStamina = Config.Bind("Keys", "Stamina", KeyCode.F7, "Sonsuz stamina tuşu");
			_keyFinale = Config.Bind("Keys", "Finale", KeyCode.F8, "Finale mode tuşu");
			_keyGodmode = Config.Bind("Keys", "Godmode", KeyCode.F9, "Godmode hotkey");
			_keyMenu = Config.Bind("Keys", "Menu", KeyCode.Delete, "Cheat & Asset Ripper menu key");
			_keyConsole = Config.Bind("Keys", "Console", KeyCode.F10, "In-game console overlay key");

			// Yeni hile tuşları (F5, F11, F12 + harfler; F6-F10 dolu).
			_keySpeed = Config.Bind("Keys", "Speed", KeyCode.F5, "Speed hack hotkey");
			_keySlowMo = Config.Bind("Keys", "SlowMotion", KeyCode.F11, "Slow motion hotkey");
			_keyItems = Config.Bind("Keys", "AllItems", KeyCode.F12, "All items hotkey");
			_keyNpcFreeze = Config.Bind("Keys", "NpcFreeze", KeyCode.T, "NPC freeze hotkey");
			_keyJumpscare = Config.Bind("Keys", "NoJumpscare", KeyCode.J, "No jumpscare hotkey");
			_keyNoclip = Config.Bind("Keys", "Noclip", KeyCode.N, "Noclip hotkey");
			_keyTeleport = Config.Bind("Keys", "Teleport", KeyCode.G, "Teleport forward hotkey");
			_keyWallhack = Config.Bind("Keys", "Wallhack", KeyCode.H, "Wallhack hotkey");
			_keyCalm = Config.Bind("Keys", "CalmBaldi", KeyCode.C, "Calm Baldi hotkey");

			// Yeni hile tuşları (2.4.0)
			_keyHighJump = Config.Bind("Keys", "HighJump", KeyCode.V, "High jump / low gravity hotkey");
			_keyFly = Config.Bind("Keys", "Fly", KeyCode.B, "Fly mode hotkey (Space up / Ctrl down)");
			_keyEnemies = Config.Bind("Keys", "HideEnemies", KeyCode.X, "Hide all enemies hotkey");
			_keyNotebooks = Config.Bind("Keys", "AutoNotebooks", KeyCode.M, "Auto collect notebooks hotkey");
			_keyReset = Config.Bind("Keys", "ResetGuilt", KeyCode.R, "Reset guilt / timer hotkey");
			_keyDoors = Config.Bind("Keys", "UnlockDoors", KeyCode.P, "Unlock all doors + exits hotkey");
			_keyExit = Config.Bind("Keys", "ExitTeleport", KeyCode.K, "Teleport to exit hotkey");
			_keyDebug = Config.Bind("Keys", "DebugMode", KeyCode.F4, "Debug mode hotkey");
			_keyEndless = Config.Bind("Keys", "EndlessMode", KeyCode.Y, "Endless mode hotkey");

			_jumpMultiplier = Config.Bind("6_Extra", "JumpMultiplier", 3f, "Jump/gravity multiplier (1-8)");
			_flySpeed = Config.Bind("6_Extra", "FlySpeed", 12f, "Fly movement speed");

			// Yeni hile değerleri
			_speedMultiplier = Config.Bind("6_Extra", "SpeedMultiplier", 3f, "Speed hack multiplier (1-10)");
			_slowMoScale = Config.Bind("6_Extra", "SlowMoScale", 0.35f, "Slow motion time scale (0.1-1)");
			_itemId = Config.Bind("6_Extra", "ItemId", 4, "Item id for 'all items' (4=BSODA)");
			_fov = Config.Bind("6_Extra", "FieldOfView", 90f, "Camera field of view");
			_audioPitch = Config.Bind("6_Extra", "AudioPitch", 1f, "Audio pitch");
			_notebooks = Config.Bind("6_Extra", "Notebooks", 20, "Notebook count to set");
			_language = Config.Bind("General", "Language", "en", "GUI language: en / tr");

			// Geliştirici test kancası: SADECE "Warning" sahnesinde açılışta scan+export yapar.
			_devAutoExport = Config.Bind("5_Dev", "AutoExportOnStart", false,
				"DEV: 'Warning' sahnesinde açılışta otomatik asset scan + export (test için)");

			L.Turkish = _language.Value != null &&
			            _language.Value.Trim().StartsWith("tr", StringComparison.OrdinalIgnoreCase);

			Instance = this;

			_harmony = new Harmony(PluginGuid);
			_harmony.PatchAll();

			_infiniteStamina = _staminaEnabled.Value;

			// Ek hile değerlerini static sınıfa yükle.
			ExtraCheats.SpeedMultiplier = Mathf.Clamp(_speedMultiplier.Value, 1f, 10f);
			ExtraCheats.SlowMoScale = Mathf.Clamp(_slowMoScale.Value, 0.1f, 1f);
			ExtraCheats.ItemId = Mathf.Clamp(_itemId.Value, 0, 20);
			ExtraCheats.FieldOfView = Mathf.Clamp(_fov.Value, 40f, 130f);
			ExtraCheats.AudioPitch = Mathf.Clamp(_audioPitch.Value, 0.25f, 2.5f);
			ExtraCheats.NotebookCount = Mathf.Max(0, _notebooks.Value);
			ExtraCheats.JumpMultiplier = Mathf.Clamp(_jumpMultiplier.Value, 1f, 8f);
			ExtraCheats.FlySpeed = Mathf.Clamp(_flySpeed.Value, 1f, 40f);

			// Her sahne değişiminde log + ilk sahnede açılış splash'i başlat.
			UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

			ModConsole.Add($"{PluginName} v{PluginVersion} loaded — Delete: menu, F10: console, F6-F9: cheats");
			LogBoth($"{PluginName} v{PluginVersion} loaded (Delete menu / F10 console / F6-F9 cheats, godmode: " +
			        (_godmodeDuration.Value <= 0f ? "INFINITE" : _godmodeDuration.Value + "s") + ")");
		}

		private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
			UnityEngine.SceneManagement.LoadSceneMode mode)
		{
			LogBoth(L.C("Scene loaded: ", "Sahne yüklendi: ") + scene.name + " (" + mode + ")");
			ExtraCheats.OnSceneChanged(); // yeni sahnede NPC/oyuncu cache'lerini tazele
			// Splash kaldırıldı — açılışta yazı gösterilmez.
		}

		/// <summary>DEV: Warning sahnesinde tüm tipleri tarayıp masaüstüne export eder.</summary>
		private IEnumerator DevAutoExport()
		{
			yield return new WaitForSeconds(3f); // splash + sahne oturursun

			LogBoth("[DEV] Scanning game data...");
			string scan = AssetRipper.Scan();
			LogBoth("[DEV] Scan result: " + scan);

			if (AssetRipper.Entries.Count == 0)
			{
				LogBoth("[DEV] No entries — export aborted.");
				yield break;
			}

			// Tüm sınıf tiplerini seç.
			var ids = new HashSet<int>();
			foreach (var e in AssetRipper.Entries) ids.Add(e.ClassId);
			LogBoth("[DEV] Selected class types: " + ids.Count);

			string dest = System.IO.Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BaldiRip_AutoTest");
			try { System.IO.Directory.CreateDirectory(dest); }
			catch (Exception ex) { LogBoth("[DEV] Cannot create dest: " + ex.Message); yield break; }

			LogBoth("[DEV] Export starting -> " + dest);
			if (!AssetRipper.StartExport(dest, ids))
			{
				LogBoth("[DEV] StartExport failed (busy or empty).");
				yield break;
			}

			// Export bitene kadar bekle (en fazla ~3 dk).
			float deadline = Time.realtimeSinceStartup + 180f;
			while (AssetRipper.Busy && Time.realtimeSinceStartup < deadline)
				yield return new WaitForSeconds(0.5f);

			LogBoth("[DEV] Export result: " + AssetRipper.LastResult);
			LogBoth("[DEV] Exported files on disk: " +
			        (System.IO.Directory.Exists(dest)
				        ? System.IO.Directory.GetFiles(dest, "*", System.IO.SearchOption.AllDirectories).Length.ToString()
				        : "0 (folder missing)"));
		}

		private void OnDestroy()
		{
			LogBoth(L.C("Plugin unloading — closing menu, deactivating godmode.",
				"Plugin kaldırılıyor — menü kapatılıyor, godmode kapatılıyor."));
			CheatMenu.Hide();
			Godmode.Deactivate();
			ExtraCheats.DeactivateAll();
			try { UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
			if (_harmony != null) _harmony.UnpatchSelf();
		}

		/// <summary>Her önemli olayı BepInEx loguna VE oyun içi konsol overlay'ine yazar.</summary>
		internal static void LogBoth(string msg)
		{
			if (string.IsNullOrEmpty(msg)) return;
			try { Log?.LogInfo(msg); } catch { }
			ModConsole.Add(msg);
		}

		private bool _devAutoExportStarted;

		private void Update()
		{
			// Oyun nesnelerini (GameController/Player) cache'le.
			GameAccess.EnsureResolved();

			// DEV test: sceneLoaded kaçırılmış olabilir -> aktif sahneyi doğrudan kontrol et.
			// SADECE "Warning" sahnesinde, bir kez çalışır.
			if (!_devAutoExportStarted && _devAutoExport != null && _devAutoExport.Value &&
			    string.Equals(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
				    "Warning", StringComparison.OrdinalIgnoreCase))
			{
				_devAutoExportStarted = true;
				LogBoth("[DEV] Auto-export requested (active scene=Warning) — starting coroutine.");
				StartCoroutine(DevAutoExport());
			}

			// Menü ve konsol tuşları her zaman çalışır.
			if (KeyDown(_keyMenu.Value))
			{
				CheatMenu.Toggle();
				LogBoth(CheatMenu.Visible
					? L.C("Menu OPEN (Delete) — game keeps running; close it for cheats.",
						"Menü AÇILDI (Delete) — oyun çalışmaya devam eder; hileler için kapat.")
					: L.C("Menu CLOSED (Delete) — cheats active again.",
						"Menü KAPANDI (Delete) — hileler yeniden aktif."));
			}
			if (KeyDown(_keyConsole.Value))
			{
				ModConsole.Visible = !ModConsole.Visible;
				LogBoth(ModConsole.Visible
					? L.C("Console overlay ON (F10)", "Konsol overlay AÇIK (F10)")
					: L.C("Console overlay OFF (F10)", "Konsol overlay KAPALI (F10)"));
			}

			// Menü açıkken oyun içi hile döngüsü durur (oyun kendisi DONMAZ, sadece hileler bekler).
			if (CheatMenu.Visible) return;

			HandleKeys();
			HandleBsoda();
			if (_infiniteStamina) InfiniteStamina.Apply();
			Godmode.Tick();

			// Ek hilelerin her kare uygulanması.
			ExtraCheats.ApplySpeed();
			ExtraCheats.ApplySlowMo();
			ExtraCheats.ApplyItems();
			ExtraCheats.ApplyNpcFreeze();
			ExtraCheats.ApplyNoJumpscare();
			ExtraCheats.ApplyNoclip();
			ExtraCheats.ApplyWallhack();
			ExtraCheats.ApplyCalmBaldi();
			ExtraCheats.ApplyFov();
			ExtraCheats.ApplyFly();
			ExtraCheats.ApplyAutoNotebooks();
		}

		private void OnGUI()
		{
			// Main menu vb. yerlerde oyunun kendi IMGUI'si bizim ÜSTÜMÜZE çizilebilir ->
			// derinliği çok düşük yaparak her zaman en üste çizilmesini sağla.
			int oldDepth = GUI.depth;
			GUI.depth = -10000;
			ModConsole.Draw();       // Better Among Us tarzı konsol overlay'i (oyunu dondurmaz)
			CheatMenu.Draw(this);
			GUI.depth = oldDepth;
		}

		private void HandleKeys()
		{
			if (KeyDown(_keyBsoda.Value)) { LogBoth("Hotkey F6/Bsoda pressed"); ToggleBsoda(); }
			if (KeyDown(_keyStamina.Value)) { LogBoth("Hotkey F7/Stamina pressed"); ToggleStamina(); }
			if (KeyDown(_keyFinale.Value)) { LogBoth("Hotkey F8/Finale pressed"); ToggleFinale(); }
			if (KeyDown(_keyGodmode.Value)) { LogBoth("Hotkey F9/Godmode pressed"); ToggleGodmode(); }

			// Yeni hileler
			if (KeyDown(_keySpeed.Value)) ToggleSpeed();
			if (KeyDown(_keySlowMo.Value)) ToggleSlowMo();
			if (KeyDown(_keyItems.Value)) ToggleItems();
			if (KeyDown(_keyNpcFreeze.Value)) ToggleNpcFreeze();
			if (KeyDown(_keyJumpscare.Value)) ToggleNoJumpscare();
			if (KeyDown(_keyNoclip.Value)) ToggleNoclip();
			if (KeyDown(_keyTeleport.Value)) ExtraCheats.TeleportForward();
			if (KeyDown(_keyWallhack.Value)) ToggleWallhack();
			if (KeyDown(_keyCalm.Value)) ToggleCalmBaldi();

			// Yeni hileler (2.4.0)
			if (KeyDown(_keyHighJump.Value)) ToggleHighJump();
			if (KeyDown(_keyFly.Value)) ToggleFly();
			if (KeyDown(_keyEnemies.Value)) ToggleEnemiesHidden();
			if (KeyDown(_keyNotebooks.Value)) ToggleAutoNotebooks();
			if (KeyDown(_keyReset.Value)) ExtraCheats.ResetGuiltAndTimer();
			if (KeyDown(_keyDoors.Value)) ExtraCheats.UnlockAllDoorsAndExits();
			if (KeyDown(_keyExit.Value)) ExtraCheats.TeleportToExit();
			if (KeyDown(_keyDebug.Value)) ToggleDebugMode();
			if (KeyDown(_keyEndless.Value)) ToggleEndlessMode();
		}

		// Splash tamamen kaldırıldı (kullanıcı isteği): açılışta yazı gösterilmez.

		// ------------------------------------------- GUI + hotkey ortak ac-kapa API --

		public bool BsodaEnabled => _bsodaEnabled.Value;
		public bool StaminaEnabled => _infiniteStamina;
		public float GodmodeDuration => _godmodeDuration.Value;
		public string LastMessage { get; private set; } = "(none)";

		/// <summary>GUI dilini anında değiştirir + config'e yazar.</summary>
		public void SetLanguage(bool turkish)
		{
			L.Turkish = turkish;
			try { _language.Value = turkish ? "tr" : "en"; } catch { }
			LogBoth(turkish ? "Dil: Türkçe (config'e kaydedildi)" : "Language: English (saved to config)");
		}

		public void SetMessage(string m)
		{
			if (string.IsNullOrEmpty(m)) return;
			LastMessage = m;
			LogBoth(m);
		}

		public void ToggleBsoda()
		{
			_bsodaEnabled.Value = !_bsodaEnabled.Value;
			SetMessage(L.C("Infinite Items (picked up items stay): ", "Sonsuz Eşya (alınanlar kalıcı): ") + L.OnOff(_bsodaEnabled.Value));
		}

		public void ToggleStamina()
		{
			_infiniteStamina = !_infiniteStamina;
			SetMessage(L.C("Infinite Stamina: ", "Sonsuz Stamina: ") + L.OnOff(_infiniteStamina));
		}

		public void ToggleFinale()
		{
			switch (FinaleModeCheat.Activate(out int finaleThreshold))
			{
				case FinaleModeCheat.Result.NoGame:
					SetMessage(L.C("Finale Mode: game scene not ready yet (GameController not found), not started.",
						"Finale Mode: oyun sahnesi henüz hazır değil (GameController bulunamadı), başlatılmadı."));
					break;
				case FinaleModeCheat.Result.AlreadyActive:
					SetMessage(finaleThreshold > 0
						? string.Format(L.C("Finale Mode already active (threshold: {0} notebooks) — not restarted.",
							"Finale Mode zaten aktif (eşik: {0} notebook) — tekrar çalıştırılmadı."), finaleThreshold)
						: L.C("Finale Mode already active — not restarted.",
							"Finale Mode zaten aktif — tekrar çalıştırılmadı."));
					break;
				default:
					SetMessage(string.Format(L.C("SPOOP MODE + FINALE activated (all {0} notebooks counted as collected).",
						"SPOOP MODE + FINALE aktive edildi (tüm {0} notebook toplanmış sayıldı)."), finaleThreshold));
					break;
			}
		}

		public void ToggleGodmode()
		{
			if (Godmode.Active)
			{
				Godmode.Deactivate();   // log'u Godmode.Deactivate yazar
			}
			else
			{
				Godmode.Activate(_godmodeDuration.Value); // log'u Godmode.Activate yazar (süresiz/süreli)
			}
		}

		// ---------------------------------------------------------- yeni hileler --

		public void ToggleSpeed()
		{
			ExtraCheats.SpeedMultiplier = Mathf.Clamp(_speedMultiplier.Value, 1f, 10f);
			ExtraCheats.ToggleSpeed();
			if (ExtraCheats.SpeedActive) SetMessage(L.C("Speed: ", "Hız: ") + "x" + ExtraCheats.SpeedMultiplier.ToString("0.0"));
		}

		public void ToggleSlowMo()
		{
			ExtraCheats.SlowMoScale = Mathf.Clamp(_slowMoScale.Value, 0.1f, 1f);
			ExtraCheats.ToggleSlowMo();
		}

		public void ToggleItems()
		{
			ExtraCheats.ItemId = Mathf.Clamp(_itemId.Value, 0, 20);
			ExtraCheats.ToggleItems();
		}

		public void ToggleNpcFreeze() => ExtraCheats.ToggleNpcFreeze();
		public void ToggleNoJumpscare() => ExtraCheats.ToggleNoJumpscare();
		public void ToggleNoclip() => ExtraCheats.ToggleNoclip();
		public void ToggleWallhack() => ExtraCheats.ToggleWallhack();
		public void ToggleCalmBaldi() => ExtraCheats.ToggleCalmBaldi();

		// Yeni hileler (2.4.0)
		public void ToggleHighJump() { ExtraCheats.JumpMultiplier = Mathf.Clamp(_jumpMultiplier.Value, 1f, 8f); ExtraCheats.ToggleHighJump(); }
		public void ToggleFly() { ExtraCheats.FlySpeed = Mathf.Clamp(_flySpeed.Value, 1f, 40f); ExtraCheats.ToggleFly(); }
		public void ToggleEnemiesHidden() => ExtraCheats.ToggleEnemiesHidden();
		public void ToggleAutoNotebooks() => ExtraCheats.ToggleAutoNotebooks();
		public void ToggleDebugMode() => ExtraCheats.ToggleDebugMode();
		public void ToggleEndlessMode() => ExtraCheats.ToggleEndlessMode();

		public void SetJumpMultiplier(float v) { _jumpMultiplier.Value = Mathf.Clamp(v, 1f, 8f); ExtraCheats.SetJumpMultiplier(_jumpMultiplier.Value); }
		public void SetFlySpeed(float v) { _flySpeed.Value = Mathf.Clamp(v, 1f, 40f); ExtraCheats.FlySpeed = _flySpeed.Value; }

		/// <summary>GUI slider'dan ayarları günceller + config'e yazar.</summary>
		public void SetSpeed(float v) { _speedMultiplier.Value = Mathf.Clamp(v, 1f, 10f); ExtraCheats.SpeedMultiplier = _speedMultiplier.Value; }
		public void SetSlowMo(float v) { _slowMoScale.Value = Mathf.Clamp(v, 0.1f, 1f); ExtraCheats.SlowMoScale = _slowMoScale.Value; if (ExtraCheats.SlowMoActive) Time.timeScale = _slowMoScale.Value; }
		public void SetItemId(int v) { _itemId.Value = Mathf.Clamp(v, 0, 20); ExtraCheats.ItemId = _itemId.Value; }
		public void SetFov(float v) { _fov.Value = Mathf.Clamp(v, 40f, 130f); ExtraCheats.SetFov(_fov.Value); }
		public void SetAudioPitch(float v) { _audioPitch.Value = Mathf.Clamp(v, 0.25f, 2.5f); ExtraCheats.SetAudioPitch(_audioPitch.Value); }
		public void SetNotebooks(int v) { _notebooks.Value = Mathf.Max(0, v); ExtraCheats.SetNotebooks(_notebooks.Value); }

		// ---------------------------------------------------------- Sonsuz Eşya --

		/// <summary>
		/// Son görülen envanter durumu (slot başına eşya ID'si). 0 = boş slot.
		/// Oyun bir eşyayı kullandığında/cıkardığında slotu 0'a çeker; biz buradan geri yazarız.
		/// Boş slotları ASLA doldurmayız — sadece kullanıcımn gerçekten aldığı eşyalar kalıcıdır.
		/// </summary>
		private int[] _itemSnapshot;

		private void HandleBsoda()
		{
			if (!_bsodaEnabled.Value || !GameAccess.HasGameController) return;

			int[] items = GameAccess.GetItemArray();
			if (items == null || items.Length == 0) return;

			// İlk çağrı veya slot sayısı değiştiyse (sahne değişimi) anlık görüntüyü al.
			if (_itemSnapshot == null || _itemSnapshot.Length != items.Length)
			{
				_itemSnapshot = (int[])items.Clone();
				return;
			}

			// 1) Yeni alınan eşyaları kaydet (snapshot boş, oyun dolu).
			// 2) Oyunun boşalttığı slotları geri yükle (snapshot dolu, oyun boş).
			//    NOT: Oyun eşyayı bilerek başka bir ID'ye çevirirse (ör. item swapping), o yeni
			//    ID'yi de geçerli kabul edip snapshot'ı güncelliyoruz — zorla eski ID'ye döndürmüyoruz.
			bool changed = false;
			var restored = new System.Text.StringBuilder();
			for (int i = 0; i < items.Length; i++)
			{
				if (items[i] == 0 && _itemSnapshot[i] != 0)
				{
					// Oyun eşyayı kullandı/çıkardı -> aynı slotta aynı eşyayı geri koy.
					items[i] = _itemSnapshot[i];
					changed = true;
					restored.Append(i).Append(':').Append(_itemSnapshot[i]).Append(' ');
				}
				else if (items[i] != 0 && _itemSnapshot[i] != items[i])
				{
					// Yeni/degisen eşya -> kalıcı olarak kaydet.
					_itemSnapshot[i] = items[i];
					BaldiInfiniteBsodaPlugin.LogBoth(
						L.C("Item saved to slot ", "Eşya kaydedildi (slot ") + i +
						L.C(": id ", " -> id ") + items[i] + ")");
				}
			}

			if (changed)
			{
				RefreshSlots();
				LogBoth(L.C("Items restored: ", "Eşyalar geri yüklendi: ") + restored.ToString().TrimEnd());
			}
		}

		/// <summary>
		/// HUD slot ikonlarını envanterdeki GERÇEK eşya ID'lerine göre tazeler.
		/// (Eskisi sabit BSODA ikonunu tüm slotlara koyardı — artık her slot kendi eşyasını gösterir.)
		/// </summary>
		private void RefreshSlots()
		{
			if (_itemSnapshot == null) return;
			var icons = GameAccess.GetIconArray();
			var slots = GameAccess.GetSlotArray();
			if (icons == null || slots == null) return;
			int n = System.Math.Min(slots.Length, _itemSnapshot.Length);
			for (int i = 0; i < n; i++)
			{
				int id = _itemSnapshot[i];
				if (id < 0 || id >= icons.Length) continue; // boş slot veya bilinmeyen ID -> ikon değiştirme
				object tex = icons.GetValue(id);
				if (tex != null) GameAccess.SetSlotTexture(slots.GetValue(i), tex);
			}
		}

		// ---------------------------------------------------------- yardımcılar --

		// Tuş başına cooldown: aynı tuşun çift algılanmasını (ghost/focus) engeller.
		private readonly System.Collections.Generic.Dictionary<KeyCode, float> _hotkeyCooldown =
			new System.Collections.Generic.Dictionary<KeyCode, float>();

		private bool KeyDown(KeyCode key, float cooldown = 0.25f)
		{
			try
			{
				if (key == KeyCode.None || !Input.GetKeyDown(key)) return false;
				if (_hotkeyCooldown.TryGetValue(key, out float last) && Time.unscaledTime - last < cooldown)
					return false; // çok kısa sürede tekrarlandı -> yut
				_hotkeyCooldown[key] = Time.unscaledTime;
				return true;
			}
			catch { return false; }
		}
	}
}
