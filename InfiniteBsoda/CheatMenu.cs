using System;
using System.Collections.Generic;
using System.IO;
using BaldiInfiniteBsoda.Ripper;
using UnityEngine;

namespace BaldiInfiniteBsoda
{
	/// <summary>
	/// Unified in-game menu (AssetStudio style):
	///   top menu bar (File / Options / Export / Filter Types / View / Language / About),
	///   left tabs (Cheats / Assets / Classes + search),
	///   right Preview | Dump,
	///   bottom status bar + progress.
	/// Opened/closed with Delete. Cheats are paused while this GUI is open.
	/// </summary>
	internal static class CheatMenu
	{
		public static bool Visible;

		private static Rect _win = new Rect(0, 0, 1060, 700);
		private static bool _winInit;
		private static int _tab;                 // 0=cheats 1=assets 2=classes
		private static int _rightTab;            // 0=preview 1=dump
		private static string _search = "";
		private static Vector2 _listScroll, _detailScroll, _cheatScroll;
		private static int _selected = -1;
		private static readonly HashSet<int> _exportSel = new HashSet<int>();
		private static string _dest = "";
		private static string _openMenu;
		private static Texture2D _previewTex;
		private static string _previewInfo = "";
		private static bool _previewDirty;
		private static GUIStyle _titleStyle, _rowStyle, _wrapStyle, _barStyle, _warnStyle;
		private static BaldiInfiniteBsodaPlugin _plugin;
		private static Texture2D _winBg, _titleBg;

		// cursor state saved while the menu is open
		private static CursorLockMode _savedLock = CursorLockMode.None;
		private static bool _savedVisible;

		// game EventSystem state saved while the menu is open (blocks clicks behind the window)
		private static readonly Dictionary<UnityEngine.EventSystems.EventSystem, bool> _esStates =
			new Dictionary<UnityEngine.EventSystems.EventSystem, bool>();

		// scene canvases disabled while the menu is open (their overlay text draws above IMGUI)
		private static readonly List<Canvas> _canvasOff = new List<Canvas>();

		private static string[] TabLabels => new[]
		{
			L.C("Cheats", "Hileler"),
			L.C("Assets", "Varlıklar"),
			L.C("Classes", "Sınıflar"),
		};

		private static string[] RightTabLabels => new[]
		{
			L.C("Preview", "Önizleme"),
			L.C("Dump", "Döküm"),
		};

		// ---------------------------------------------------------- show / hide

		public static void Toggle()
		{
			if (Visible) Hide();
			else Show();
		}

		public static void Show()
		{
			if (Visible) return;
			Visible = true;
			try
			{
				_savedLock = Cursor.lockState;
				_savedVisible = Cursor.visible;
			}
			catch { }
			ReleaseCursor();
			ModConsole.Add(L.C("Menu opened (Delete) — game keeps running, close it for cheats.",
				"Menü açıldı (Delete) — oyun çalışmaya devam eder, hileler için kapat."));
			if (string.IsNullOrEmpty(_dest))
			{
				try
				{
					_dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
						"BaldiRip_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
				}
				catch { _dest = "BaldiRip"; }
			}
			if (AssetRipper.Entries.Count == 0 && !AssetRipper.Busy)
				AssetRipper.Status = L.C("Ready — use File -> 'Scan Game Data'",
					"Hazır — dosya menüsünden 'Oyun Data'sını Tara' ile başla");
		}

		public static void Hide()
		{
			if (!Visible) return;
			Visible = false;
			_openMenu = null;
			DestroyPreview();
			try
			{
				Cursor.lockState = _savedLock;
				Cursor.visible = _savedVisible;
			}
			catch { }
			RestoreGameUi();
			ModConsole.Add(L.C("Menu closed — cheats are active again.",
				"Menü kapatıldı — hileler yeniden aktif."));
		}

		/// <summary>Menü açıkken imleç her karede serbest bırakılır (oyun tekrar kilitleyemez).</summary>
		private static void ReleaseCursor()
		{
			try
			{
				if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
				if (!Cursor.visible) Cursor.visible = true;
			}
			catch { }
		}

		/// <summary>Menü açıkken oyunun uGUI tıklamaları arkaplandaki butonlara gitmesin.</summary>
		private static void DisableGameUi()
		{
			try
			{
				var es = UnityEngine.EventSystems.EventSystem.current;
				if (es == null) return;
				if (!_esStates.ContainsKey(es)) _esStates[es] = es.enabled;
				if (es.enabled) es.enabled = false;
			}
			catch { }
		}

		private static void RestoreGameUi()
		{
			try
			{
				foreach (var kv in _esStates)
					if (kv.Key != null) kv.Key.enabled = kv.Value;
			}
			catch { }
			_esStates.Clear();

			try
			{
				foreach (var c in _canvasOff)
					if (c != null) c.enabled = true;
			}
			catch { }
			_canvasOff.Clear();
		}

		/// <summary>Oyunun uGUI canvas'ları IMGUI'mizin üstünde çizilir -> menü açıkken kapat.</summary>
		private static void DisableGameCanvases()
		{
			try
			{
				var all = Resources.FindObjectsOfTypeAll<Canvas>();
				for (int i = 0; i < all.Length; i++)
				{
					var c = all[i];
					if (c == null || !c.enabled) continue;
					if (!c.gameObject.scene.IsValid()) continue;   // prefab/asset -> dokunma
					if (!_canvasOff.Contains(c)) _canvasOff.Add(c);
					c.enabled = false;
				}
			}
			catch { }
		}

		// ------------------------------------------------------------ OnGUI hook

		public static void Draw(BaldiInfiniteBsodaPlugin plugin)
		{
			if (!Visible) return;
			_plugin = plugin;
			ReleaseCursor();
			DisableGameUi();
			DisableGameCanvases();
			InitStyles();
			if (!_winInit)
			{
				_win.x = Mathf.Max(8, (Screen.width - _win.width) * 0.5f);
				_win.y = Mathf.Max(8, (Screen.height - _win.height) * 0.5f);
				_winInit = true;
			}
			// Pencere her zaman ekranda kalsın (sürüklemeyle kaybolsa bile geri alınır).
			_win.x = Mathf.Clamp(_win.x, 0, Mathf.Max(0, Screen.width - _win.width));
			_win.y = Mathf.Clamp(_win.y, 0, Mathf.Max(0, Screen.height - _win.height));
			_win = GUI.Window(0x5147, _win, id => DrawWindow(plugin), BaldiInfiniteBsodaPlugin.PluginName);
		}

		private static void InitStyles()
		{
			if (_titleStyle != null) return;
			_winBg = MakeTex(new Color(0.11f, 0.13f, 0.17f, 1f));
			_titleBg = MakeTex(new Color(0.16f, 0.22f, 0.40f, 1f));
			_titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 12 };
			_rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
			_wrapStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11, richText = false };
			_barStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleLeft, fontSize = 11, padding = new RectOffset(6, 6, 0, 0) };
			_warnStyle = new GUIStyle(GUI.skin.box)
			{
				wordWrap = true,
				fontSize = 12,
				fontStyle = FontStyle.Bold,
				alignment = TextAnchor.MiddleCenter,
				normal = { textColor = new Color(1f, 0.9f, 0.3f) },
				padding = new RectOffset(8, 8, 5, 5),
			};
		}

		private static void DrawWindow(BaldiInfiniteBsodaPlugin plugin)
		{
			// Oyunun kendi menü harfleri pencereden okunmasın: opak arkaplan + başlık.
			GUI.DrawTexture(new Rect(0, 0, _win.width, _win.height), _winBg);
			GUI.DrawTexture(new Rect(0, 0, _win.width, 22), _titleBg);
			GUI.Label(new Rect(8, 2, _win.width - 60, 20), BaldiInfiniteBsodaPlugin.PluginName, _titleStyle);

			GUILayout.BeginArea(new Rect(6, 26, _win.width - 12, _win.height - 32));

			DrawMenuBar(plugin);
			DrawWarning();
			DrawBody(plugin);
			DrawStatusBar();

			GUILayout.EndArea();
			GUI.DragWindow(new Rect(0, 0, _win.width, 22));
		}

		// ----------------------------------------------------------- warning bar

		private static void DrawWarning()
		{
			GUILayout.Space(3);
			var prev = GUI.color;
			GUI.color = new Color(1f, 0.75f, 0.15f, 1f);
			GUILayout.Box(L.GuiWarning, _warnStyle, GUILayout.Height(40));
			GUI.color = prev;
		}

		// ------------------------------------------------------------ menu bar

		private static void DrawMenuBar(BaldiInfiniteBsodaPlugin plugin)
		{
			GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.Height(22));
			DrawMenu(L.C("File", "Dosya"), new[]
			{
				(L.C("Scan Game Data", "Oyun Data'sını Tara"), (Action)(() =>
				{
					if (AssetRipper.Busy) { AssetRipper.Status = L.C("Busy: export running", "Meşgul: export sürüyor"); return; }
					_selected = -1;
					DestroyPreview();
					AssetRipper.ExportLog.Clear();
					var sw = System.Diagnostics.Stopwatch.StartNew();
					string r = AssetRipper.Scan();
					sw.Stop();
					AssetRipper.Status = $"{L.C("Scan", "Tarama")}: {r} ({sw.ElapsedMilliseconds} ms)";
					AssetRipper.AddLog(L.C("Scan: ", "Tarama: ") + r);
					_autoSelect();
				})),
				(L.C("Clear List", "Listeyi Temizle"), (Action)(() =>
				{
					if (AssetRipper.Busy) return;
					AssetRipper.Entries.Clear();
					AssetRipper.Status = L.C("List cleared", "Liste temizlendi");
					_selected = -1;
					DestroyPreview();
				})),
			});
			DrawMenu(L.C("Options", "Seçenekler"), new[]
			{
				(L.C("Select All (export)", "Tümünü Seç (export)"), (Action)(() =>
				{
					foreach (var kv in ClassCounts()) _exportSel.Add(kv.Key);
				})),
				(L.C("Deselect All", "Tümünü Kaldır"), (Action)(() => _exportSel.Clear())),
			});
			DrawMenu("Export", new[]
			{
				(L.C("Export Selected Types", "Seçili tipleri export et"), (Action)(() => StartExport(plugin))),
				(L.C("Cancel", "İptal et"), (Action)(() =>
				{
					if (AssetRipper.Busy) { AssetRipper.CancelRequested = true; AssetRipper.Status = L.C("Cancel requested...", "İptal istendi..."); }
				})),
				(L.C("Open Destination Folder", "Hedef klasörü aç"), (Action)(() =>
				{
					try { System.Diagnostics.Process.Start(_dest); }
					catch (Exception ex) { AssetRipper.Status = L.C("Cannot open: ", "Açılamadı: ") + ex.Message; }
				})),
			});
			DrawMenu(L.C("Filter Types", "Tip Filtrele"), new[]
			{
				("AudioClip", (Action)(() => Only(83))),
				("Texture2D", (Action)(() => Only(28))),
				("TextAsset", (Action)(() => Only(49))),
				(L.C("Script / MonoScript", "Script / MonoScript"), (Action)(() => Only(115))),
			});
			DrawMenu(L.C("View", "Görünüm"), new[]
			{
				(L.C("Console (F10)", "Konsol (F10)"), (Action)(() => ModConsole.Visible = !ModConsole.Visible)),
				(L.C("Close Menu (Delete)", "Menüyü Kapat (Delete)"), (Action)(Hide)),
			});
			DrawMenu(L.C("Language", "Dil"), new[]
			{
				("English", (Action)(() => plugin?.SetLanguage(false))),
				("Türkçe", (Action)(() => plugin?.SetLanguage(true))),
			});
			DrawMenu(L.C("About", "Hakkında"), new[]
			{
				($"{BaldiInfiniteBsodaPlugin.PluginName} v{BaldiInfiniteBsodaPlugin.PluginVersion}", (Action)(() => { })),
			});
			GUILayout.FlexibleSpace();
			GUILayout.Label(AssetRipper.Busy ? L.C("EXPORT RUNNING", "EXPORT ÇALIŞIYOR") : L.C("ready", "hazır"),
				_rowStyle, GUILayout.Width(130));
			GUILayout.EndHorizontal();

			// open menu panel
			if (_openMenu != null && _menuItems.TryGetValue(_openMenu, out var items))
			{
				GUILayout.BeginVertical(GUI.skin.box, GUILayout.Height(items.Length * 20 + 8));
				foreach (var it in items)
				{
					if (GUILayout.Button(it.name, GUILayout.Height(19)))
					{
						_openMenu = null;
						it.act();
						break;
					}
				}
				GUILayout.EndVertical();
			}
		}

		private static readonly Dictionary<string, (string name, Action act)[]> _menuItems =
			new Dictionary<string, (string, Action)[]>();

		private static void DrawMenu(string title, (string name, Action act)[] items)
		{
			_menuItems[title] = items;
			string label = _openMenu == title ? title + " ▾" : title;
			if (GUILayout.Button(label, GUI.skin.label, GUILayout.Width(86)))
				_openMenu = _openMenu == title ? null : title;
		}

		private static void Only(int classId)
		{
			_exportSel.Clear();
			_exportSel.Add(classId);
			_tab = 2;
		}

		// --------------------------------------------------------------- body

		private static void DrawBody(BaldiInfiniteBsodaPlugin plugin)
		{
			GUILayout.BeginHorizontal();

			// ---- LEFT ----
			GUILayout.BeginVertical(GUILayout.Width(360));
			_tab = GUILayout.Toolbar(_tab, TabLabels, GUILayout.Height(22));
			GUILayout.Space(4);
			switch (_tab)
			{
				case 0: DrawCheatsTab(plugin); break;
				case 1: DrawAssetsTab(); break;
				case 2: DrawClassesTab(); break;
			}
			GUILayout.EndVertical();

			GUILayout.Space(6);

			// ---- RIGHT ----
			GUILayout.BeginVertical();
			_rightTab = GUILayout.Toolbar(_rightTab, RightTabLabels, GUILayout.Height(20));
			GUILayout.Space(3);
			if (_rightTab == 0) DrawPreview(); else DrawDump();
			GUILayout.EndVertical();

			GUILayout.EndHorizontal();
		}

		// -------------------------------------------------------------- cheats

		private static void DrawCheatsTab(BaldiInfiniteBsodaPlugin plugin)
		{
			_cheatScroll = GUILayout.BeginScrollView(_cheatScroll);
			GUILayout.Label(L.C("Cheat status (hotkeys work too)",
				"Hile durumları (kısayol tuşları da çalışır)"), _wrapStyle);

			GUILayout.Space(4);
			CheatRow(L.C("Infinite BSODA", "Sonsuz BSODA"), "F6", plugin.BsodaEnabled, plugin.ToggleBsoda);
			CheatRow(L.C("Infinite Stamina", "Sonsuz Stamina"), "F7", plugin.StaminaEnabled, plugin.ToggleStamina);
			CheatRow("Finale Mode", "F8", FinaleModeCheat.IsActive, plugin.ToggleFinale,
				FinaleModeCheat.IsSpooped ? L.C("spooped", "spooped") : null);
			string godLabel = plugin.GodmodeDuration <= 0f
				? L.C("Godmode (infinite)", "Godmode (süresiz)")
				: $"Godmode ({plugin.GodmodeDuration:0}s)";
			CheatRow(godLabel, "F9", Godmode.Active,
				plugin.ToggleGodmode,
				!Godmode.Active ? null
					: Godmode.Infinite ? "∞"
					: string.Format(L.C("{0}s left", "kalan {0} sn"), Mathf.CeilToInt(Godmode.Remaining)));

			// ------------------------------------------------ yeni hileler --
			GUILayout.Space(6);
			GUILayout.Label(L.C("=== EXTRA CHEATS ===", "=== EK HİLELER ==="), _titleStyle);

			CheatRow(L.C("Speed Hack", "Hız Hilesi"), "F5", ExtraCheats.SpeedActive, plugin.ToggleSpeed,
				ExtraCheats.SpeedActive ? "x" + ExtraCheats.SpeedMultiplier.ToString("0.0") : null);
			CheatRow(L.C("Slow Motion", "Yavaş Çekim"), "F11", ExtraCheats.SlowMoActive, plugin.ToggleSlowMo,
				ExtraCheats.SlowMoActive ? ExtraCheats.SlowMoScale.ToString("0.00") + "x" : null);
			CheatRow(L.C("All Items", "Tüm Eşyalar"), "F12", ExtraCheats.ItemsActive, plugin.ToggleItems,
				"id " + ExtraCheats.ItemId);
			CheatRow(L.C("NPC Freeze", "NPC Dondurma"), "T", ExtraCheats.NpcFreezeActive, plugin.ToggleNpcFreeze);
			CheatRow(L.C("No Jumpscare", "Jumpscare Yok"), "J", ExtraCheats.NoJumpscareActive, plugin.ToggleNoJumpscare);
			CheatRow(L.C("Noclip (walls)", "Duvar Geçme"), "N", ExtraCheats.NoclipActive, plugin.ToggleNoclip);
			CheatRow(L.C("Wallhack (ESP)", "Duvar Hilesi"), "H", ExtraCheats.WallhackActive, plugin.ToggleWallhack);
			CheatRow(L.C("Calm Baldi", "Sakin Baldi"), "C", ExtraCheats.CalmBaldiActive, plugin.ToggleCalmBaldi);
			CheatRow(L.C("High Jump / Low Gravity", "Yüksek Atlama"), "V", ExtraCheats.HighJumpActive, plugin.ToggleHighJump,
				ExtraCheats.HighJumpActive ? "x" + ExtraCheats.JumpMultiplier.ToString("0.0") : null);
			CheatRow(L.C("Fly Mode (Space/Ctrl)", "Uçma Modu"), "B", ExtraCheats.FlyActive, plugin.ToggleFly);
			CheatRow(L.C("Hide All Enemies", "Tüm Düşmanları Gizle"), "X", ExtraCheats.EnemiesHidden, plugin.ToggleEnemiesHidden);
			CheatRow(L.C("Auto Collect Notebooks", "Notebook Otomatik Topla"), "M", ExtraCheats.AutoNotebooksActive, plugin.ToggleAutoNotebooks);
			CheatRow(L.C("Debug Mode", "Debug Modu"), "F4", ExtraCheats.DebugModeActive, plugin.ToggleDebugMode);
			CheatRow(L.C("Endless Mode", "Sonsuz Mod"), "Y", ExtraCheats.EndlessModeActive, plugin.ToggleEndlessMode);

			GUILayout.BeginHorizontal();
			GUILayout.Label(L.C("Reset guilt / timer (R):", "Suçluluk / süre sıfırla (R):"), _rowStyle, GUILayout.Width(180));
			GUILayout.Label("[R]", _rowStyle, GUILayout.Width(46));
			var prevR = GUI.color;
			GUI.color = new Color(0.5f, 0.8f, 1f);
			if (GUILayout.Button(L.C("RESET", "SIFIRLA"), GUILayout.Width(110))) ExtraCheats.ResetGuiltAndTimer();
			GUI.color = prevR;
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
			GUILayout.Label(L.C("Unlock doors / exits:", "Kapı / çıkış aç:"), _rowStyle, GUILayout.Width(180));
			GUILayout.Label("[P]", _rowStyle, GUILayout.Width(46));
			var prevP = GUI.color;
			GUI.color = new Color(0.5f, 0.8f, 1f);
			if (GUILayout.Button(L.C("UNLOCK", "AÇ"), GUILayout.Width(110))) ExtraCheats.UnlockAllDoorsAndExits();
			GUI.color = prevP;
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
			GUILayout.Label(L.C("Teleport forward (G):", "İleri ışınlan (G):"), _rowStyle, GUILayout.Width(180));
			GUILayout.Label("[" + "G" + "]", _rowStyle, GUILayout.Width(46));
			var prevTel = GUI.color;
			GUI.color = new Color(0.5f, 0.8f, 1f);
			if (GUILayout.Button(L.C("TELEPORT", "IŞINLA"), GUILayout.Width(110)))
				ExtraCheats.TeleportForward();
			GUI.color = prevTel;
			GUILayout.EndHorizontal();

			// Ayar slider'ları (değerler config'e yazılır).
			GUILayout.Space(8);
			GUILayout.Label(L.C("=== SETTINGS ===", "=== AYARLAR ==="), _titleStyle);
			SliderRow(L.C("Speed", "Hız"), ExtraCheats.SpeedMultiplier, 1f, 10f,
				v => plugin.SetSpeed(v), "x" + ExtraCheats.SpeedMultiplier.ToString("0.0"));
			SliderRow(L.C("SlowMo", "Yavaşlık"), ExtraCheats.SlowMoScale, 0.1f, 1f,
				v => plugin.SetSlowMo(v), ExtraCheats.SlowMoScale.ToString("0.00") + "x");
			SliderRow(L.C("Item ID", "Eşya ID"), ExtraCheats.ItemId, 0, 20,
				v => plugin.SetItemId(Mathf.RoundToInt(v)), ExtraCheats.ItemId.ToString());
			SliderRow(L.C("FOV", "Görüş Açısı"), ExtraCheats.FieldOfView, 40f, 130f,
				v => plugin.SetFov(v), ExtraCheats.FieldOfView.ToString("0"));
			SliderRow(L.C("Audio Pitch", "Ses Perdesi"), ExtraCheats.AudioPitch, 0.25f, 2.5f,
				v => plugin.SetAudioPitch(v), ExtraCheats.AudioPitch.ToString("0.00"));
			SliderRow(L.C("Notebooks", "Defter"), ExtraCheats.GetNotebooks(), 0, 99,
				v => plugin.SetNotebooks(Mathf.RoundToInt(v)), ExtraCheats.GetNotebooks().ToString());
			SliderRow(L.C("Jump Power", "Atlama Gücü"), ExtraCheats.JumpMultiplier, 1f, 8f,
				v => plugin.SetJumpMultiplier(v), "x" + ExtraCheats.JumpMultiplier.ToString("0.0"));
			SliderRow(L.C("Fly Speed", "Uçma Hızı"), ExtraCheats.FlySpeed, 1f, 40f,
				v => plugin.SetFlySpeed(v), ExtraCheats.FlySpeed.ToString("0"));

			GUILayout.Space(8);
			GUILayout.Label(L.C("Last action:", "Son işlem:"), _titleStyle);
			GUILayout.Box(plugin.LastMessage ?? "(none)", _wrapStyle, GUILayout.MinHeight(46));

			GUILayout.Space(8);
			GUILayout.Label(L.C("Asset Ripper:", "Asset Ripper:"), _titleStyle);
			GUILayout.Label(L.C("Tab 'Assets' -> scan, 'Classes' -> pick types to export, " +
			                    "Export menu -> 'Export Selected Types'. Destination: desktop.",
			                    "Sekme 'Varlıklar' -> tara, 'Sınıflar' -> dışa aktarılacak tipleri seç, " +
			                    "Export menüsü -> 'Seçili tipleri export et'. Hedef: masaüstü."), _wrapStyle);
			GUILayout.EndScrollView();
		}

		private static void CheatRow(string label, string key, bool state, Action toggle, string extra = null)
		{
			GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.Height(30));
			GUILayout.Label(label, _rowStyle, GUILayout.Width(180));
			GUILayout.Label("[" + key + "]", _rowStyle, GUILayout.Width(46));
			string st = L.OnOff(state);
			if (extra != null && state) st += " · " + extra;
			var prev = GUI.color;
			GUI.color = state ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.6f, 0.6f);
			if (GUILayout.Button(st, GUILayout.Width(110))) toggle();
			GUI.color = prev;
			GUILayout.EndHorizontal();
		}

		/// <summary>Ek hile ayarları için etiket + slider + değer satırı.</summary>
		private static void SliderRow(string label, float value, float min, float max, Action<float> onChanged, string display)
		{
			GUILayout.BeginHorizontal();
			GUILayout.Label(label, _rowStyle, GUILayout.Width(120));
			float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(180));
			if (Mathf.Abs(v - value) > 0.001f) onChanged(v);
			GUILayout.Label(display, _rowStyle, GUILayout.Width(60));
			GUILayout.EndHorizontal();
		}

		// ---------------------------------------------------------- kategori filtresi

		private static int _catFilter; // 0=All, 1..7 = ClassId.CatX + 1

		private static string[] CatLabels => new[]
		{
			L.C("All", "Tümü"),
			"Audio", "Texture", "Text", "Animation", "Model", "Script",
			L.C("Other", "Diğer"),
		};

		private static string CatName(int cat)
		{
			switch (cat)
			{
				case ClassId.CatAudio: return "Audio";
				case ClassId.CatTexture: return "Texture";
				case ClassId.CatText: return "Text";
				case ClassId.CatAnimation: return "Animation";
				case ClassId.CatModel: return "Model";
				case ClassId.CatScript: return "Script";
				default: return L.C("Other", "Diğer");
			}
		}

		// -------------------------------------------------------------- assets

		private static void DrawAssetsTab()
		{
			// Kategori toolbarı: varlıklar Audio/Texture/Text/Animation/Model/Script/Other olarak gruplanır.
			_catFilter = GUILayout.Toolbar(_catFilter, CatLabels, GUILayout.Height(20));
			GUILayout.Space(3);
			GUILayout.BeginHorizontal();
			GUILayout.Label(L.C("Search:", "Ara:"), GUILayout.Width(52));
			_search = GUILayout.TextField(_search ?? "");
			GUILayout.EndHorizontal();

			int total = AssetRipper.Entries.Count;
			if (total == 0)
			{
				GUILayout.Label(L.C("List empty — menu File -> \"Scan Game Data\"",
				      "Liste boş — dosya menüsü -> \"Oyun Data'sını Tara\""), _wrapStyle);
			}
			else
			{
				string catInfo = _catFilter == 0 ? L.C("all categories", "tüm kategoriler") : CatName(_catFilter - 1);
				GUILayout.Label(string.Format(L.C("{0} objects ({1}) — click to preview",
					"{0} nesne ({1}) — önizleme için tıkla"), total, catInfo), _wrapStyle);
			}

			_listScroll = GUILayout.BeginScrollView(_listScroll);
			if (total > 0)
			{
				string q = (_search ?? "").Trim().ToLowerInvariant();
				int shown = 0;
				int matchCount = 0;
				for (int i = 0; i < total; i++)
				{
					var e = AssetRipper.Entries[i];
					if (_catFilter > 0 && ClassId.Category(e.ClassId) != _catFilter - 1) continue;
					if (q.Length > 0 &&
					    e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
					    e.ClassName.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
						continue;
					matchCount++;
					if (shown >= 3000) continue;   // liste çok büyürse çizimi sınırla ama sayımı sürdür

					shown++;
					bool sel = _selected == i;
					string text = "[" + e.ClassName + "]  " + e.Name;
					if (text.Length > 60) text = text.Substring(0, 57) + "...";
					var prev = GUI.backgroundColor;
					if (sel) GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
					if (GUILayout.Button(text, sel ? GUI.skin.box : GUI.skin.label, GUILayout.Height(16)))
					{
						_selected = i;
						_previewDirty = true;
					}
					GUI.backgroundColor = prev;
				}
				if (matchCount == 0) GUILayout.Label(L.C("no match", "eşleşme yok"), _wrapStyle);
				else if (matchCount > shown)
					GUILayout.Label(string.Format(L.C("(+{0} more not drawn)", "(+{0} tane daha çizilmedi)"),
						matchCount - shown), _wrapStyle);
			}
			GUILayout.EndScrollView();
		}

		// ------------------------------------------------------------- classes

		private static List<KeyValuePair<int, int>> ClassCounts()
		{
			var counts = new Dictionary<int, int>();
			foreach (var e in AssetRipper.Entries)
			{
				counts.TryGetValue(e.ClassId, out int c);
				counts[e.ClassId] = c + 1;
			}
			var list = new List<KeyValuePair<int, int>>(counts);
			list.Sort((a, b) =>
			{
				bool ea = ClassId.Exportable.Contains(a.Key), eb = ClassId.Exportable.Contains(b.Key);
				if (ea != eb) return ea ? -1 : 1;
				return b.Value - a.Value;
			});
			return list;
		}

		private static void DrawClassesTab()
		{
			GUILayout.Label(L.C("Pick the types to export (each type gets its own folder):",
				"Dışa aktarılacak tipleri seç (her tip kendi klasörüne yazılır):"), _wrapStyle);
			GUILayout.Space(3);

			var counts = ClassCounts();
			GUILayout.BeginHorizontal();
			if (GUILayout.Button(L.C("All", "Tümü"))) { foreach (var kv in counts) _exportSel.Add(kv.Key); }
			if (GUILayout.Button(L.C("None", "Temizle"))) _exportSel.Clear();
			GUILayout.EndHorizontal();

			_listScroll = GUILayout.BeginScrollView(_listScroll);
			if (counts.Count == 0)
				GUILayout.Label(L.C("No classes — scan first", "Sınıf yok — önce tara"), _wrapStyle);
			foreach (var kv in counts)
			{
				GUILayout.BeginHorizontal();
				bool on = _exportSel.Contains(kv.Key);
				bool now = GUILayout.Toggle(on, GUIContent.none, GUI.skin.toggle, GUILayout.Width(18));
				if (now != on) { if (now) _exportSel.Add(kv.Key); else _exportSel.Remove(kv.Key); }
				GUILayout.Label($"[{CatName(ClassId.Category(kv.Key))}] {ClassId.Name(kv.Key)} ({kv.Value})", _rowStyle);
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
			GUILayout.EndScrollView();

			GUILayout.Space(4);
			GUILayout.BeginHorizontal();
			GUILayout.Label(L.C("Dest:", "Hedef:"), GUILayout.Width(44));
			_dest = GUILayout.TextField(_dest ?? "");
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal();
			GUI.enabled = !AssetRipper.Busy && _exportSel.Count > 0 && AssetRipper.Entries.Count > 0;
			if (GUILayout.Button(string.Format(L.C("EXPORT ({0} types)", "EXPORT ({0} tip)"), _exportSel.Count),
				GUILayout.Height(26)))
				StartExport(null);
			GUI.enabled = true;
			GUI.enabled = AssetRipper.Busy;
			if (GUILayout.Button(L.C("Cancel", "İptal"), GUILayout.Height(26), GUILayout.Width(70)))
				AssetRipper.CancelRequested = true;
			GUI.enabled = true;
			GUILayout.EndHorizontal();
		}

		private static void StartExport(BaldiInfiniteBsodaPlugin plugin)
		{
			if (AssetRipper.Busy) { AssetRipper.Status = L.C("Export already running", "Zaten export çalışıyor"); return; }
			if (_exportSel.Count == 0) { AssetRipper.Status = L.C("No types selected (Classes tab)", "Hiç tip seçilmedi (Sınıflar sekmesi)"); return; }
			if (string.IsNullOrEmpty(_dest)) { AssetRipper.Status = L.C("Destination folder is empty", "Hedef klasör boş"); return; }
			try { Directory.CreateDirectory(_dest); }
			catch (Exception ex) { AssetRipper.Status = L.C("Cannot create destination: ", "Hedef oluşturulamadı: ") + ex.Message; return; }

			AssetRipper.ExportLog.Clear();
			if (AssetRipper.StartExport(_dest, new HashSet<int>(_exportSel)))
			{
				AssetRipper.AddLog(L.C("Export started -> ", "Export başladı -> ") + _dest);
				plugin?.SetMessage(L.C("Asset export started -> ", "Asset export başladı -> ") + _dest);
			}
			else AssetRipper.Status = L.C("Could not start (empty list / running)", "Başlatılamadı (liste boş / çalışıyor)");
		}

		// --------------------------------------------------------------- right

		private static void DrawPreview()
		{
			if (_selected < 0 || _selected >= AssetRipper.Entries.Count)
			{
				GUILayout.Label(L.C("Select an asset on the left to preview.\n\n" +
				                    "Texture2D -> image\nAudioClip -> info (OGG export)\nTextAsset -> text\n" +
				                    "Others -> summary",
				                    "Önizleme için soldan bir varlık seç.\n\n" +
				                    "Texture2D -> görsel\nAudioClip -> bilgi (OGG export)\nTextAsset -> metin\n" +
				                    "Diğer -> özet bilgi"), _wrapStyle);
				return;
			}
			var e = AssetRipper.Entries[_selected];

			if (_previewDirty)
			{
				BuildPreview(e);
				_previewDirty = false;
			}

			if (_previewTex != null)
			{
				float maxW = _win.width - 400, maxH = _win.height - 300;
				float s = Mathf.Min(maxW / _previewTex.width, maxH / _previewTex.height, 1f);
				if (s <= 0f) s = 1f;
				float w = _previewTex.width * s, h = _previewTex.height * s;
				GUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
				Rect prevRect = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
				GUI.Box(prevRect, GUIContent.none);
				GUI.DrawTexture(new Rect(prevRect.x + 1, prevRect.y + 1, prevRect.width - 2, prevRect.height - 2),
					_previewTex, ScaleMode.StretchToFill, false);
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
			if (!string.IsNullOrEmpty(_previewInfo))
			{
				GUILayout.Space(4);
				_detailScroll = GUILayout.BeginScrollView(_detailScroll);
				GUILayout.Label(_previewInfo, _wrapStyle);
				GUILayout.EndScrollView();
			}
		}

		private static void BuildPreview(AssetEntry e)
		{
			DestroyPreview();
			try
			{
				switch (e.ClassId)
				{
					case 28:   // Texture2D
					{
						var t = AssetClass.ReadTexture2D(e.SF, e.Info);
						_previewInfo = $"Texture2D\n{L.C("name", "ad")}: {t.Name}\n" +
						               $"{L.C("size", "boyut")}: {t.Width}x{t.Height}\n" +
						               $"{L.C("format", "format")}: {PngEncoder.FormatName(t.Format)}\n" +
						               $"mip: {t.MipCount}\n" +
						               $"{L.C("source", "kaynak")}: {(t.Streaming ? t.StreamPath + " +" + t.StreamOffset + " +" + t.StreamSize : "inline " + t.InlineSize)}";
						if (t.Width > 0 && t.Height > 0)
						{
							byte[] raw = ReadEntryBytes(e, t.Streaming, t.StreamPath, t.StreamOffset, t.StreamSize, t.InlinePos, t.InlineSize);
							byte[] rgba = raw == null ? null : PngEncoder.DecodeToRgba(raw, t.Width, t.Height, t.Format);
							if (rgba != null)
							{
								var tex = new Texture2D(t.Width, t.Height, TextureFormat.RGBA32, false);
								tex.LoadRawTextureData(rgba);
								tex.Apply(false, false);
								_previewTex = tex;
							}
							else _previewInfo += "\n\n" + L.C("preview: format not supported (export writes .rawtex)",
								"önizleme: format desteklenmiyor (export .rawtex olarak yazılır)");
						}
						break;
					}
					case 83:   // AudioClip
					{
						var a = AssetClass.ReadAudioClip(e.SF, e.Info);
						_previewInfo = $"AudioClip\n{L.C("name", "ad")}: {a.Name}\n" +
						               $"{L.C("channels", "kanal")}: {a.Channels}  {L.C("freq", "frekans")}: {a.Frequency}\n" +
						               $"{L.C("length", "süre")}: {a.Length:0.00}s  {L.C("format", "format")}: {(a.CompressionFormat == 1 ? "Vorbis (FSB5)" : a.CompressionFormat.ToString())}\n" +
						               $"{L.C("source", "kaynak")}: {a.Source}  offset: {a.Offset}  {L.C("size", "boyut")}: {a.Size}\n\n" +
						               L.C("Exported as OGG.", "Export edilince OGG olarak yazılır.");
						break;
					}
					case 49:   // TextAsset
					{
						var t = AssetClass.ReadTextAsset(e.SF, e.Info);
						int n = Math.Min(t.Bytes.Length, 4000);
						string head = System.Text.Encoding.UTF8.GetString(t.Bytes, 0, n);
						_previewInfo = $"TextAsset\n{L.C("name", "ad")}: {t.Name}\n" +
						               $"{L.C("size", "boyut")}: {t.Bytes.Length} B\n\n{head}";
						break;
					}
					default:
					{
						_previewInfo = $"{e.ClassName}\n{L.C("name", "ad")}: {e.Name}\npathID: {e.Info.PathId}\n" +
						               $"{L.C("file", "dosya")}: {e.SF.FileName}\n" +
						               $"{L.C("size", "boyut")}: {e.Info.Size} B\n\n" +
						               L.C("(no visual preview for this type — see the Dump tab)",
						                   "(bu tip için görsel önizleme yok — Döküm sekmesine bak)");
						break;
					}
				}
			}
			catch (Exception ex)
			{
				_previewInfo = L.C("Preview error: ", "Önizleme hatası: ") + ex.Message;
			}
		}

		private static void DrawDump()
		{
			if (_selected < 0 || _selected >= AssetRipper.Entries.Count)
			{
				GUILayout.Label(L.C("Select an asset to dump.", "Döküm için bir varlık seç."), _wrapStyle);
				return;
			}
			var e = AssetRipper.Entries[_selected];
			_detailScroll = GUILayout.BeginScrollView(_detailScroll);
			GUILayout.Label($"class: {e.ClassName} (id {e.ClassId})\n" +
			                $"{L.C("name", "ad")}: {e.Name}\npathID: {e.Info.PathId}\n" +
			                $"{L.C("file", "dosya")}: {e.SF.FileName}\n" +
			                $"offset: {e.Info.Start}  {L.C("size", "boyut")}: {e.Info.Size}\nunity: {e.SF.UnityVersion}",
				_wrapStyle);
			GUILayout.Space(6);
			GUILayout.Label(L.C("First 256 bytes (hex):", "İlk 256 byte (hex):"), _titleStyle);
			GUILayout.Label(HexDump(e.SF.Data, (int)e.Info.Start, Math.Min(256, (int)e.Info.Size)), _wrapStyle);
			GUILayout.EndScrollView();
		}

		private static string HexDump(byte[] b, int off, int len)
		{
			var sb = new System.Text.StringBuilder();
			int end = Math.Min(off + len, b.Length);
			for (int i = off; i < end; i += 16)
			{
				sb.Append((i - off).ToString("X4")).Append(": ");
				for (int j = 0; j < 16 && i + j < end; j++) sb.Append(b[i + j].ToString("X2")).Append(' ');
				sb.Append(' ');
				for (int j = 0; j < 16 && i + j < end; j++)
				{
					byte c = b[i + j];
					sb.Append(c >= 32 && c < 127 ? (char)c : '.');
				}
				sb.Append('\n');
			}
			return sb.ToString();
		}

		// ---------------------------------------------------------- status bar

		private static void DrawStatusBar()
		{
			GUILayout.Space(4);
			GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.Height(24));
			string status = AssetRipper.Status ?? L.C("Ready", "Hazır");
			if (AssetRipper.Busy && AssetRipper.ProgressTotal > 0)
			{
				float p = (float)AssetRipper.Progress / AssetRipper.ProgressTotal;
				status += $"  [{AssetRipper.Progress}/{AssetRipper.ProgressTotal}]";
				Rect r = GUILayoutUtility.GetRect(160, 14, GUILayout.Width(160));
				GUI.Box(r, "");
				GUI.Box(new Rect(r.x, r.y, r.width * p, r.height), "");
			}
			GUILayout.Label(status, _barStyle, GUILayout.MinWidth(0));
			GUILayout.FlexibleSpace();
			if (!string.IsNullOrEmpty(AssetRipper.LastResult))
				GUILayout.Label(AssetRipper.LastResult, _barStyle, GUILayout.Width(300));
			GUILayout.EndHorizontal();
		}

		// ----------------------------------------------------------- helpers

		private static void _autoSelect()
		{
			for (int i = 0; i < AssetRipper.Entries.Count; i++)
				if (AssetRipper.Entries[i].ClassId == 28)
				{
					_selected = i;
					_previewDirty = true;
					return;
				}
		}

		private static void DestroyPreview()
		{
			if (_previewTex != null)
			{
				try { UnityEngine.Object.Destroy(_previewTex); } catch { }
				_previewTex = null;
			}
		}

		private static Texture2D MakeTex(Color c)
		{
			var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
			t.SetPixel(0, 0, c);
			t.Apply();
			return t;
		}

		/// <summary>Copy of the ripper data reader (used on the GUI thread).</summary>
		private static byte[] ReadEntryBytes(AssetEntry e, bool streaming, string sourcePath,
			long offset, long size, int inlinePos, int inlineSize)
		{
			if (streaming)
			{
				if (string.IsNullOrEmpty(sourcePath) || size <= 0) return null;
				string full = Path.IsPathRooted(sourcePath) ? sourcePath : Path.Combine(AssetRipper.DataDir ?? "", sourcePath);
				if (!File.Exists(full)) return null;
				using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Write))
				{
					if (offset < 0 || offset >= fs.Length) return null;
					long len = Math.Min(size, fs.Length - offset);
					if (len <= 0 || len > int.MaxValue) return null;
					var buf = new byte[len];
					fs.Seek(offset, SeekOrigin.Begin);
					int read = 0;
					while (read < buf.Length)
					{
						int n = fs.Read(buf, read, buf.Length - read);
						if (n <= 0) break;
						read += n;
					}
					if (read != buf.Length)
					{
						var t2 = new byte[read];
						Array.Copy(buf, t2, read);
						return t2;
					}
					return buf;
				}
			}
			if (inlineSize <= 0) return null;
			var src = e.SF.Data;
			if (inlinePos < 0 || inlinePos + inlineSize > src.Length) return null;
			var v = new byte[inlineSize];
			Array.Copy(src, inlinePos, v, 0, inlineSize);
			return v;
		}
	}
}
