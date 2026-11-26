using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace BaldiInfiniteBsoda
{
	/// <summary>
	/// Ek hile paketi — hepsi runtime reflection ile, Assembly-CSharp'e compile-time bağımlılık YOK.
	///
	/// Gerçek oyun alanları (Assembly-CSharp.dll'den çıkarıldı):
	///   PlayerScript: walkSpeed, runSpeed, slowSpeed, playerSpeed, cc (CharacterController),
	///                 jumpRopeScreen, guilt, gameOver, moveDirection
	///   GameControllerScript: notebooks, item[], itemTextures[], cullingMask, camera,
	///                         gameOverDelay, baldiScrpt
	///   BaldiScript: agent (NavMeshAgent), flash (Image), cameraAnimator, hudAnimator,
	///                baldiAnger, baldiTempAnger, timeToAnger, antiHearing
	///   PlaytimeScript / PrincipalScript / BullyScript: agent / cc / enemy bileşenleri
	/// </summary>
	internal static class ExtraCheats
	{
		// ---------------------------------------------------------- speed --
		private static readonly string[] SpeedFields =
			{ "walkSpeed", "runSpeed", "slowSpeed", "playerSpeed", "moveSpeed", "speed" };

		private static readonly Dictionary<string, float> _speedBackup = new Dictionary<string, float>();

		public static bool SpeedActive { get; private set; }
		public static float SpeedMultiplier = 3f;

		public static void ToggleSpeed()
		{
			SpeedActive = !SpeedActive;
			if (!SpeedActive) RestoreSpeed();
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Speed hack: ", "Hız hilesi: ") +
				L.OnOff(SpeedActive) + (SpeedActive ? " x" + SpeedMultiplier.ToString("0.0") : ""));
		}

		public static void ApplySpeed()
		{
			if (!SpeedActive || !GameAccess.HasPlayer) return;
			var p = GameAccess.Player;
			var t = p.GetType();
			foreach (string name in SpeedFields)
			{
				FieldInfo f = AccessTools.Field(t, name);
				if (f == null) continue;
				if (f.FieldType != typeof(float)) continue;
				if (!_speedBackup.ContainsKey(name)) _speedBackup[name] = (float)f.GetValue(p);
				// Her karede orijinal değer x çarpan (birikmesin).
				f.SetValue(p, _speedBackup[name] * SpeedMultiplier);
			}
		}

		private static void RestoreSpeed()
		{
			if (!GameAccess.HasPlayer) return;
			var p = GameAccess.Player;
			var t = p.GetType();
			foreach (var kv in _speedBackup)
			{
				FieldInfo f = AccessTools.Field(t, kv.Key);
				if (f != null && f.FieldType == typeof(float)) f.SetValue(p, kv.Value);
			}
			_speedBackup.Clear();
		}

		// ---------------------------------------------------------- slow motion --
		public static bool SlowMoActive { get; private set; }
		public static float SlowMoScale = 0.35f;

		public static void ToggleSlowMo()
		{
			SlowMoActive = !SlowMoActive;
			Time.timeScale = SlowMoActive ? SlowMoScale : 1f;
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Slow motion: ", "Yavaş çekim: ") +
				L.OnOff(SlowMoActive) + (SlowMoActive ? " (" + SlowMoScale.ToString("0.00") + "x)" : ""));
		}

		public static void ApplySlowMo()
		{
			if (!SlowMoActive) return;
			Time.timeScale = SlowMoScale; // oyun/sahne değişse de sabit kalsın
		}

		// ---------------------------------------------------------- item spam --
		public static bool ItemsActive { get; private set; }
		public static int ItemId = 4; // 4 = BSODA

		public static void ToggleItems()
		{
			ItemsActive = !ItemsActive;
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("All items: ", "Tüm eşyalar: ") + L.OnOff(ItemsActive) +
				(ItemsActive ? " (id " + ItemId + ")" : ""));
		}

		public static void ApplyItems()
		{
			if (!ItemsActive || !GameAccess.HasGameController) return;
			int[] items = GameAccess.GetItemArray();
			if (items == null || items.Length == 0) return;

			bool changed = false;
			for (int i = 0; i < items.Length; i++)
				if (items[i] != ItemId) { items[i] = ItemId; changed = true; }
			if (!changed) return;

			// HUD ikonlarını da güncelle (itemTextures[id] varsa).
			try
			{
				Array icons = GameAccess.GetIconArray();
				Array slots = GameAccess.GetSlotArray();
				if (icons != null && slots != null && ItemId >= 0 && ItemId < icons.Length)
				{
					object tex = icons.GetValue(ItemId);
					for (int i = 0; i < slots.Length; i++)
						if (tex != null) GameAccess.SetSlotTexture(slots.GetValue(i), tex);
				}
			}
			catch { }
		}

		// ---------------------------------------------------------- NPC freeze --
		private static readonly string[] NpcTypes =
			{ "BaldiScript", "PlaytimeScript", "PrincipalScript", "BullyScript", "FirstPrizeScript", "CraftersScript" };

		private static readonly List<MonoBehaviour> _frozenNpcs = new List<MonoBehaviour>();
		private static readonly List<float> _frozenSpeeds = new List<float>();

		public static bool NpcFreezeActive { get; private set; }

		public static void ToggleNpcFreeze()
		{
			NpcFreezeActive = !NpcFreezeActive;
			if (!NpcFreezeActive) RestoreNpcs();
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("NPC freeze: ", "NPC dondurma: ") + L.OnOff(NpcFreezeActive));
		}

		public static void ApplyNpcFreeze()
		{
			if (!NpcFreezeActive) return;
			if (_frozenNpcs.Count == 0)
			{
				foreach (string tn in NpcTypes)
				{
					Type t = AccessTools.TypeByName(tn);
					if (t == null) continue;
					foreach (object obj in UnityEngine.Object.FindObjectsOfType(t))
					{
						var mb = obj as MonoBehaviour;
						if (mb == null || _frozenNpcs.Contains(mb)) continue;
						_frozenNpcs.Add(mb);

						// NavMeshAgent varsa hızını 0 yap (eski değeri sakla).
						FieldInfo agentField = AccessTools.Field(t, "agent");
						if (agentField != null)
						{
							var agent = agentField.GetValue(mb) as NavMeshAgent;
							if (agent != null)
							{
								_frozenSpeeds.Add(agent.speed);
								agent.speed = 0f;
								agent.ResetPath();
							}
						}
						// Baldi özel: kovalamayı kapat, öfkeyi sıfırla.
						SetFloatField(mb, "baldiAnger", 0f);
						SetFloatField(mb, "baldiTempAnger", 0f);
						SetFloatField(mb, "timeToAnger", 99999f);
						SetBoolField(mb, "canChase", false);
						SetBoolField(mb, "antiHearing", true);
						SetFloatField(mb, "antiHearingTime", 99999f);
					}
				}
			}

			// Her karede güvence: ajanlar durmaya devam etsin.
			int idx = 0;
			foreach (var mb in _frozenNpcs)
			{
				if (mb == null) continue;
				FieldInfo agentField = AccessTools.Field(mb.GetType(), "agent");
				var agent = agentField?.GetValue(mb) as NavMeshAgent;
				if (agent != null) agent.speed = 0f;
				SetFloatField(mb, "baldiAnger", 0f);
				SetFloatField(mb, "baldiTempAnger", 0f);
				SetBoolField(mb, "canChase", false);
				idx++;
			}
		}

		private static void RestoreNpcs()
		{
			int i = 0;
			foreach (var mb in _frozenNpcs)
			{
				if (mb == null) continue;
				FieldInfo agentField = AccessTools.Field(mb.GetType(), "agent");
				var agent = agentField?.GetValue(mb) as NavMeshAgent;
				if (agent != null && i < _frozenSpeeds.Count) agent.speed = _frozenSpeeds[i];
				SetBoolField(mb, "canChase", true);
				i++;
			}
			_frozenNpcs.Clear();
			_frozenSpeeds.Clear();
		}

		// ---------------------------------------------------------- no jumpscare --
		public static bool NoJumpscareActive { get; private set; }

		public static void ToggleNoJumpscare()
		{
			NoJumpscareActive = !NoJumpscareActive;
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("No jumpscare: ", "Jumpscare yok: ") + L.OnOff(NoJumpscareActive));
		}

		public static void ApplyNoJumpscare()
		{
			if (!NoJumpscareActive) return;
			// Baldi'nin jumpscare görsel/animasyon bileşenlerini kapat.
			Type baldiType = AccessTools.TypeByName("BaldiScript");
			if (baldiType != null)
			{
				foreach (object obj in UnityEngine.Object.FindObjectsOfType(baldiType))
				{
					var mb = obj as MonoBehaviour;
					if (mb == null) continue;
					SetObjectActive(mb, "flash", false);          // parlak Flaş görseli
					SetObjectActive(mb, "cameraAnimator", false); // kamera yakınlaştırma
					SetObjectActive(mb, "hudAnimator", false);    // HUD kırmızı flaş
				}
			}
			// Oyuncu tarafı: oyunu asla game-over'a sokma.
			if (GameAccess.HasPlayer)
			{
				GameAccess.SetBool(GameAccess.Player, new[] { "gameOver" }, false);
				GameAccess.SetFloat(GameAccess.Player, new[] { "guilt" }, 0f);
			}
		}

		// ---------------------------------------------------------- noclip --
		public static bool NoclipActive { get; private set; }

		public static void ToggleNoclip()
		{
			NoclipActive = !NoclipActive;
			if (GameAccess.HasPlayer)
			{
				var cc = GameAccess.Player.GetComponent<CharacterController>();
				if (cc != null) cc.enabled = !NoclipActive;
			}
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Noclip: ", "Duvar geçme: ") + L.OnOff(NoclipActive));
		}

		/// <summary>Noclip açıkken yer çekimini engelle (karakter düşmesin).</summary>
		public static void ApplyNoclip()
		{
			if (!NoclipActive || !GameAccess.HasPlayer) return;
			var cc = GameAccess.Player.GetComponent<CharacterController>();
			if (cc != null) cc.enabled = false;
		}

		// ---------------------------------------------------------- teleport --
		public static int Teleports;

		public static void TeleportForward(float meters = 12f)
		{
			if (!GameAccess.HasPlayer) return;
			Transform tr = GameAccess.Player.transform;
			Vector3 dir = tr.forward;
			dir.y = 0f;
			if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
			tr.position += dir.normalized * meters;
			Teleports++;
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Teleported forward ", "İleri ışınlandı ") +
				meters.ToString("0") + L.C(" m (#", " m (#") + Teleports + ")");
		}

		// ---------------------------------------------------------- FOV --
		public static float FieldOfView = 90f;

		public static void ApplyFov()
		{
			var cam = Camera.main;
			if (cam != null && Mathf.Abs(cam.fieldOfView - FieldOfView) > 0.1f)
				cam.fieldOfView = FieldOfView;
		}

		public static void SetFov(float value)
		{
			FieldOfView = Mathf.Clamp(value, 40f, 130f);
			ApplyFov();
		}

		// ---------------------------------------------------------- wallhack (ESP) --
		public static bool WallhackActive { get; private set; }
		private static readonly List<Renderer> _espRenderers = new List<Renderer>();

		public static void ToggleWallhack()
		{
			WallhackActive = !WallhackActive;
			if (!WallhackActive) RestoreWallhack();
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Wallhack (see NPCs through walls): ", "Duvar hilesi (NPC'ler duvar arkadan görünür): ") + L.OnOff(WallhackActive));
		}

		public static void ApplyWallhack()
		{
			if (!WallhackActive) return;
			if (_espRenderers.Count == 0)
			{
				foreach (string tn in NpcTypes)
				{
					Type t = AccessTools.TypeByName(tn);
					if (t == null) continue;
					foreach (object obj in UnityEngine.Object.FindObjectsOfType(t))
					{
						var mb = obj as MonoBehaviour;
						if (mb == null) continue;
						foreach (var r in mb.GetComponentsInChildren<Renderer>(true))
						{
							if (r == null || _espRenderers.Contains(r)) continue;
							try
							{
								// ZTest = 8 (Always) -> duvarlarin arkasinda da cizilir.
								var mat = r.material; // klonlanir, oyunun orijinali bozulmaz
								if (mat != null && mat.HasProperty("_ZTest")) mat.SetFloat("_ZTest", 8f);
								_espRenderers.Add(r);
							}
							catch { }
						}
					}
				}
			}
		}

		private static void RestoreWallhack()
		{
			foreach (var r in _espRenderers)
			{
				if (r == null) continue;
				try
				{
					var mat = r.material;
					if (mat != null && mat.HasProperty("_ZTest")) mat.SetFloat("_ZTest", 4f); // LEqual
				}
				catch { }
			}
			_espRenderers.Clear();
		}

		// ---------------------------------------------------------- notebooks --
		public static int NotebookCount = 20;

		public static void SetNotebooks(int value)
		{
			if (!GameAccess.HasGameController) return;
			NotebookCount = Mathf.Max(0, value);
			GameAccess.SetInt(GameAccess.Gc, GameAccess.NotebooksField, NotebookCount);
			GameAccess.InvokeGc(new[] { "UpdateNotebookCount" });
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Notebooks set to ", "Notebook sayısı ayarlandı: ") + NotebookCount);
		}

		public static int GetNotebooks()
		{
			return GameAccess.HasGameController
				? GameAccess.GetInt(GameAccess.Gc, GameAccess.NotebooksField, 0)
				: NotebookCount;
		}

		// ---------------------------------------------------------- audio pitch --
		public static float AudioPitch = 1f;

		public static void SetAudioPitch(float value)
		{
			AudioPitch = Mathf.Clamp(value, 0.25f, 2.5f);
			foreach (var src in UnityEngine.Object.FindObjectsOfType<AudioSource>())
				try { src.pitch = AudioPitch; } catch { }
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Audio pitch: ", "Ses perdesi: ") + AudioPitch.ToString("0.00"));
		}

		// ---------------------------------------------------------- Baldi calm --
		public static bool CalmBaldiActive { get; private set; }

		public static void ToggleCalmBaldi()
		{
			CalmBaldiActive = !CalmBaldiActive;
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Calm Baldi (never angry): ", "Sakin Baldi (asla kızmaz): ") + L.OnOff(CalmBaldiActive));
		}

		public static void ApplyCalmBaldi()
		{
			if (!CalmBaldiActive) return;
			Type baldiType = AccessTools.TypeByName("BaldiScript");
			if (baldiType == null) return;
			foreach (object obj in UnityEngine.Object.FindObjectsOfType(baldiType))
			{
				var mb = obj as MonoBehaviour;
				if (mb == null) continue;
				SetFloatField(mb, "baldiAnger", 0f);
				SetFloatField(mb, "baldiTempAnger", 0f);
				SetFloatField(mb, "timeToAnger", 99999f);
				SetFloatField(mb, "angerRate", 0f);
				SetBoolField(mb, "canChase", false);
			}
		}

		// ---------------------------------------------------------- yardımcılar --
		private static void SetFloatField(object target, string name, float value)
		{
			if (target == null) return;
			try { FieldInfo f = AccessTools.Field(target.GetType(), name); if (f != null && f.FieldType == typeof(float)) f.SetValue(target, value); }
			catch { }
		}

		private static void SetBoolField(object target, string name, bool value)
		{
			if (target == null) return;
			try { FieldInfo f = AccessTools.Field(target.GetType(), name); if (f != null && f.FieldType == typeof(bool)) f.SetValue(target, value); }
			catch { }
		}

		private static void SetObjectActive(object target, string fieldName, bool active)
		{
			if (target == null) return;
			try
			{
				FieldInfo f = AccessTools.Field(target.GetType(), fieldName);
				if (f == null) return;
				object o = f.GetValue(target);
				// Animator/Image/AudioSource gibi bileşenler Behaviour'dan gelir (enabled özelliği orada).
				var behaviour = o as Behaviour;
				if (behaviour != null) { behaviour.enabled = active; return; }
				var go = o as GameObject;
				if (go != null) go.SetActive(active);
			}
			catch { }
		}

		// ======================================================================
		// YENİ HİLELER (2.4.0)
		// ======================================================================

		// ---------------------------------------------------------- yüksek atlama --
		public static bool HighJumpActive { get; private set; }
		public static float JumpMultiplier = 3f;
		private static float _origGravityY = float.NaN;

		public static void ToggleHighJump()
		{
			HighJumpActive = !HighJumpActive;
			if (HighJumpActive)
			{
				if (float.IsNaN(_origGravityY)) _origGravityY = Physics.gravity.y;
				Physics.gravity = new Vector3(0f, _origGravityY / JumpMultiplier, 0f);
			}
			else
			{
				if (!float.IsNaN(_origGravityY)) Physics.gravity = new Vector3(0f, _origGravityY, 0f);
				_origGravityY = float.NaN;
			}
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("High jump / low gravity: ", "Yüksek atlama / düşük yer çekimi: ") +
				L.OnOff(HighJumpActive) + (HighJumpActive ? " x" + JumpMultiplier.ToString("0.0") : ""));
		}

		public static void SetJumpMultiplier(float v)
		{
			JumpMultiplier = Mathf.Clamp(v, 1f, 8f);
			if (HighJumpActive && !float.IsNaN(_origGravityY))
				Physics.gravity = new Vector3(0f, _origGravityY / JumpMultiplier, 0f);
		}

		// ---------------------------------------------------------- uçma (fly) --
		public static bool FlyActive { get; private set; }
		public static float FlySpeed = 12f;

		public static void ToggleFly()
		{
			FlyActive = !FlyActive;
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Fly mode: ", "Uçma modu: ") + L.OnOff(FlyActive) +
				(FlyActive ? " (Space up / Ctrl down)" : ""));
		}

		/// <summary>Her karede: Space ile yukarı, LeftCtrl ile aşağı uç (noclip ile birlikte çalışır).</summary>
		public static void ApplyFly()
		{
			if (!FlyActive || !GameAccess.HasPlayer) return;
			float dy = 0f;
			if (Input.GetKey(KeyCode.Space)) dy += 1f;
			if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) dy -= 1f;
			if (dy == 0f) return;
			GameAccess.Player.transform.position += Vector3.up * (dy * FlySpeed * Time.unscaledDeltaTime);
			BaldiInfiniteBsodaPlugin.LogBoth("[Fly] moved " + (dy > 0 ? "up" : "down"));
		}

		// ---------------------------------------------------------- düşmanları sil --
		public static bool EnemiesHidden { get; private set; }
		private static readonly List<GameObject> _hiddenEnemies = new List<GameObject>();

		public static void ToggleEnemiesHidden()
		{
			EnemiesHidden = !EnemiesHidden;
			if (EnemiesHidden)
			{
				_hiddenEnemies.Clear();
				foreach (string tn in NpcTypes)
				{
					Type t = AccessTools.TypeByName(tn);
					if (t == null) continue;
					foreach (object obj in UnityEngine.Object.FindObjectsOfType(t))
					{
						var mb = obj as MonoBehaviour;
						if (mb == null || mb.gameObject == null) continue;
						if (_hiddenEnemies.Contains(mb.gameObject)) continue;
						_hiddenEnemies.Add(mb.gameObject);
						mb.gameObject.SetActive(false);
					}
				}
			}
			else
			{
				foreach (var go in _hiddenEnemies)
					if (go != null) go.SetActive(true);
				_hiddenEnemies.Clear();
			}
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Enemies hidden: ", "Düşmanlar gizlendi: ") + L.OnOff(EnemiesHidden) +
				" (" + _hiddenEnemies.Count + ")");
		}

		// ---------------------------------------------------------- notebook topla --
		public static bool AutoNotebooksActive { get; private set; }

		public static void ToggleAutoNotebooks()
		{
			AutoNotebooksActive = !AutoNotebooksActive;
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Auto collect notebooks: ", "Notebook otomatik topla: ") + L.OnOff(AutoNotebooksActive));
		}

		public static void ApplyAutoNotebooks()
		{
			if (!AutoNotebooksActive || !GameAccess.HasGameController) return;
			Type t = GameAccess.Gc.GetType();
			FieldInfo f = AccessTools.Field(t, "notebookPickups");
			var pickups = f?.GetValue(GameAccess.Gc) as GameObject[];
			if (pickups == null) return;
			int collected = 0;
			foreach (var go in pickups)
			{
				if (go == null || !go.activeSelf) continue;
				go.SetActive(false);
				collected++;
			}
			if (collected > 0)
			{
				GameAccess.InvokeGc(new[] { "CollectNotebook" });
				BaldiInfiniteBsodaPlugin.LogBoth(L.C("Auto-collected notebooks: ", "Otomatik toplanan notebook: ") + collected);
			}
		}

		// ---------------------------------------------------------- guilt / süre sıfırla --
		public static void ResetGuiltAndTimer()
		{
			if (GameAccess.HasPlayer)
			{
				GameAccess.SetFloat(GameAccess.Player, new[] { "guilt", "initGuilt" }, 0f);
				GameAccess.SetTextValue(GameAccess.Player, new[] { "guiltType" }, "");
			}
			if (GameAccess.HasGameController)
			{
				GameAccess.SetFloat(GameAccess.Gc, new[] { "gameOverDelay" }, 99999f);
			}
			Type baldiType = AccessTools.TypeByName("BaldiScript");
			if (baldiType != null)
				foreach (object obj in UnityEngine.Object.FindObjectsOfType(baldiType))
				{
					var mb = obj as MonoBehaviour;
					if (mb == null) continue;
					SetFloatField(mb, "baldiAnger", 0f);
					SetFloatField(mb, "baldiTempAnger", 0f);
					SetFloatField(mb, "baldiWait", 99999f);
					SetFloatField(mb, "timeToAnger", 99999f);
				}
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Guilt cleared, timer/anger reset.", "Suçluluk temizlendi, süre/öfke sıfırlandı."));
		}

		// ---------------------------------------------------------- kapılar / çıkışlar --
		public static void UnlockAllDoorsAndExits()
		{
			int doors = 0, entrances = 0;
			foreach (string tn in new[] { "DoorScript" })
			{
				Type t = AccessTools.TypeByName(tn);
				if (t == null) continue;
				foreach (object obj in UnityEngine.Object.FindObjectsOfType(t))
				{
					var mb = obj as MonoBehaviour;
					if (mb == null) continue;
					SetBoolField(mb, "bDoorLocked", false);
					GameAccess.InvokeOn(mb, new[] { "UnlockDoor" });
					doors++;
				}
			}
			foreach (string tn in new[] { "EntranceScript" })
			{
				Type t = AccessTools.TypeByName(tn);
				if (t == null) continue;
				foreach (object obj in UnityEngine.Object.FindObjectsOfType(t))
				{
					var mb = obj as MonoBehaviour;
					if (mb == null) continue;
					GameAccess.InvokeOn(mb, new[] { "Raise" });
					entrances++;
				}
			}
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Doors unlocked: ", "Kapılar açıldı: ") + doors +
				L.C(", entrances raised: ", ", çıkışlar kaldırıldı: ") + entrances);
		}

		public static void TeleportToExit(int index = 0)
		{
			if (!GameAccess.HasGameController) return;
			Type t = GameAccess.Gc.GetType();
			FieldInfo f = AccessTools.Field(t, "entrance_" + Mathf.Clamp(index, 0, 3));
			var entrance = f?.GetValue(GameAccess.Gc) as Component;
			if (entrance == null || GameAccess.Player == null) return;
			Vector3 p = entrance.transform.position;
			GameAccess.Player.transform.position = new Vector3(p.x, p.y + 1f, p.z);
			Teleports++;
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Teleported to exit ", "Çıkışa ışınlandı ") + index + " (#" + Teleports + ")");
		}

		// ---------------------------------------------------------- debug / endless --
		public static bool DebugModeActive { get; private set; }
		public static bool EndlessModeActive { get; private set; }

		public static void ToggleDebugMode()
		{
			DebugModeActive = !DebugModeActive;
			if (GameAccess.HasGameController)
				GameAccess.SetBool(GameAccess.Gc, new[] { "debugMode" }, DebugModeActive);
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Debug mode: ", "Debug modu: ") + L.OnOff(DebugModeActive));
		}

		public static void ToggleEndlessMode()
		{
			EndlessModeActive = !EndlessModeActive;
			Type baldiType = AccessTools.TypeByName("BaldiScript");
			if (baldiType != null)
				foreach (object obj in UnityEngine.Object.FindObjectsOfType(baldiType))
				{
					var mb = obj as MonoBehaviour;
					if (mb != null) SetBoolField(mb, "endless", EndlessModeActive);
				}
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Endless mode: ", "Sonsuz mod: ") + L.OnOff(EndlessModeActive));
		}

		// ======================================================================
		// SLOP'A ÖZEL HİLELER (2.6.0) — Whistle / Dash / Speed caps
		// SLOP (Mono build) PlayerScript alanları:
		//   WhistleKey, DashKey, IsWhistling, WhistleCooldown,
		//   Dashing, DashCooldown, MaxWalkSpeed, MaxRunSpeed, DashSlider
		// ======================================================================

		public static bool InfiniteWhistleActive { get; private set; }
		public static bool InfiniteDashActive { get; private set; }
		public static float SlopSpeedCap = 20f;
		public static bool SpeedCapActive { get; private set; }

		public static void ToggleInfiniteWhistle()
		{
			InfiniteWhistleActive = !InfiniteWhistleActive;
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Infinite whistle (SLOP): ", "Sonsuz ıslık (SLOP): ") + L.OnOff(InfiniteWhistleActive));
		}

		public static void ToggleInfiniteDash()
		{
			InfiniteDashActive = !InfiniteDashActive;
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Infinite dash (SLOP): ", "Sonsuz atılma (SLOP): ") + L.OnOff(InfiniteDashActive));
		}

		public static void ToggleSpeedCap()
		{
			SpeedCapActive = !SpeedCapActive;
			BaldiInfiniteBsodaPlugin.LogBoth(L.C("Speed cap override (SLOP): ", "Hız sınırı geçersiz kıl (SLOP): ") +
				L.OnOff(SpeedCapActive) + (SpeedCapActive ? " " + SlopSpeedCap.ToString("0") : ""));
		}

		public static void SetSlopSpeedCap(float v)
		{
			SlopSpeedCap = Mathf.Clamp(v, 1f, 60f);
			if (SpeedCapActive) ApplySlopSpeedCap();
		}

		/// <summary>Her kare: ıslık/dash cooldown'larını sıfırla, bayrakları kapat (tekrar tekrar kullanılabilsin).</summary>
		public static void ApplySlopAbilities()
		{
			if (!GameAccess.HasPlayer) return;
			var p = GameAccess.Player;
			var t = p.GetType();
			if (InfiniteWhistleActive)
			{
				SetFloatField(p, "WhistleCooldown", 0f);
				SetBoolField(p, "IsWhistling", false); // her kare reset -> sürekli tetiklenebilir
				SetBoolField(p, "CanWhistle", true);
			}
			if (InfiniteDashActive)
			{
				SetFloatField(p, "DashCooldown", 0f);
				SetBoolField(p, "Dashing", false);
				// Dash slider'ı tam doldur
				FieldInfo sliderField = AccessTools.Field(t, "DashSlider");
				var slider = sliderField?.GetValue(p) as UnityEngine.UI.Slider;
				if (slider != null) slider.value = slider.maxValue;
			}
		}

		/// <summary>SLOP'un MaxWalkSpeed/MaxRunSpeed sınırlarını yükselt.</summary>
		public static void ApplySlopSpeedCap()
		{
			if (!SpeedCapActive || !GameAccess.HasPlayer) return;
			var p = GameAccess.Player;
			SetFloatField(p, "MaxWalkSpeed", SlopSpeedCap);
			SetFloatField(p, "MaxRunSpeed", SlopSpeedCap * 1.5f);
		}

		/// <summary>Sahne değiştiğinde cache'leri temizle (yeni NPC/Player örnekleri için).</summary>
		public static void OnSceneChanged()
		{
			_speedBackup.Clear();
			_frozenNpcs.Clear();
			_frozenSpeeds.Clear();
			_espRenderers.Clear();
			_hiddenEnemies.Clear();
			Time.timeScale = SlowMoActive ? SlowMoScale : 1f;
			if (HighJumpActive && !float.IsNaN(_origGravityY))
				Physics.gravity = new Vector3(0f, _origGravityY / JumpMultiplier, 0f);
		}

		/// <summary>Tüm hileleri kapat (menü kapanırken/oyundan çıkarken güvenli temizlik).</summary>
		public static void DeactivateAll()
		{
			if (SpeedActive) ToggleSpeed();
			if (SlowMoActive) ToggleSlowMo();
			if (ItemsActive) ToggleItems();
			if (NpcFreezeActive) ToggleNpcFreeze();
			if (NoJumpscareActive) ToggleNoJumpscare();
			if (NoclipActive) ToggleNoclip();
			if (WallhackActive) ToggleWallhack();
			if (CalmBaldiActive) ToggleCalmBaldi();
			if (HighJumpActive) ToggleHighJump();
			if (FlyActive) ToggleFly();
			if (EnemiesHidden) ToggleEnemiesHidden();
			if (AutoNotebooksActive) ToggleAutoNotebooks();
			if (DebugModeActive) ToggleDebugMode();
			if (EndlessModeActive) ToggleEndlessMode();
			if (InfiniteWhistleActive) ToggleInfiniteWhistle();
			if (InfiniteDashActive) ToggleInfiniteDash();
			if (SpeedCapActive) ToggleSpeedCap();
			if (!float.IsNaN(_origGravityY)) Physics.gravity = new Vector3(0f, _origGravityY, 0f);
			Time.timeScale = 1f;
		}
	}
}
