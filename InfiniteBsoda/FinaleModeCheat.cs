using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace BaldiInfiniteBsoda
{
	/// <summary>
	/// Finale Mode - oyunun GERÇEK akışını taklit eder: ÖNCE spoop mode, SONRA finale.
	///
	/// Önemli: finale eşiği sabit 7 DEĞİL. Modlara göre 7, 14, 20... olabilir.
	/// (ör. bu modda 20: "notebooks == 20 &amp; mode == story"). Bu sınıf eşiği 3 yolla bulur:
	///   1) Oyunun kendi sayac metni:  "1/20 KahretsinBooks" -&gt; 20  (en güvenilir)
	///   2) UpdateNotebookCount metodunun IL'indeki karşılaştırma sabiti (ldc.i4 + ceq)
	///   3) Probe: finaleMode'ı sıfırlayıp notesbook'u artırarak tetiklenen değer
	///
	/// Akış (oyunun kendi mantığı):
	///   mode="story" -&gt; SPOOP MODE (NPC'ler açılır, entrance alçalır, jumpscare)
	///   -&gt; notebooks = eşik -&gt; UpdateNotebookCount() -&gt; finaleMode=true (finale)
	///
	/// Metot adları moda göre değişebilir; bu yüzden önce bilinen adlar, sonra
	/// "spoop/notebook/finale" anahtar kelimeli reflection taraması denenir.
	/// Her adım LogBoth ile loglanır.
	/// </summary>
	internal static class FinaleModeCheat
	{
		private static readonly string[] ModeField = { "mode" };
		private static readonly string[] NotebooksField = { "notebooks" };
		private static readonly string[] FinaleModeField = { "finaleMode" };
		// Oyunun GERÇEK alan adı "spoopMode" (Assembly-CSharp.dll'den doğrulandı).
		// "spooped" sadece eski/yedek aday olarak duruyor.
		private static readonly string[] SpoopedField = { "spoopMode", "spooped", "spoopedMode" };
		private static readonly string[] NotebookCountField = { "notebookCount", "notebookCounter", "bookText" };

		private static readonly string[] ActivateSpoopMethod = { "ActivateSpoopMode", "SpoopMode" };
		private static readonly string[] UpdateNotebookMethod = { "UpdateNotebookCount", "NotebookCount" };
		private static readonly string[] ActivateFinaleMethod = { "ActivateFinaleMode", "FinaleMode", "BeginFinale" };

		private static int _cachedThreshold; // tespit edilen eşik cache'lenir (tekrar aramaya gerek yok)

		/// <summary>Activate() sonucu — plugin bunu loglar.</summary>
		public static bool IsActive
		{
			get
			{
				try { return GameAccess.Gc != null && GameAccess.GetBool(GameAccess.Gc, FinaleModeField); }
				catch { return false; }
			}
		}

		/// <summary>Spoop mode açık mı (oyunun kendi "spooped" alanı).</summary>
		public static bool IsSpooped
		{
			get
			{
				try { return GameAccess.Gc != null && GameAccess.GetBool(GameAccess.Gc, SpoopedField); }
				catch { return false; }
			}
		}

		public enum Result
		{
			NoGame,        // oyun sahnesi henüz hazır değil (GameController yok)
			AlreadyActive, // finale zaten açık (çift tuş basımı engellendi)
			Activated      // finale açıldı
		}

		private static void Dbg(string msg) => BaldiInfiniteBsodaPlugin.LogBoth("[Finale] " + msg);

		/// <summary>
		/// Spoop + finale mode'u açar. threshold, tespit edilen notebook eşiğini döner.
		/// Çift çağrıya karşı korumalıdır: finale zaten aktifse hiçbir şey yapmaz.
		/// </summary>
		public static Result Activate(out int threshold)
		{
			threshold = 0;
			if (!GameAccess.HasGameController)
			{
				Dbg(L.C("GameController not found (not in a game scene) — not started.",
					"GameController bulunamadı (oyun sahnesinde değilsin) — başlatılmadı."));
				return Result.NoGame;
			}

			// Çift tuş basımı koruması: finale zaten aktifse HİÇbir şey yapma.
			if (GameAccess.GetBool(GameAccess.Gc, FinaleModeField))
			{
				threshold = _cachedThreshold;
				Dbg(L.C("already active — ignored.", "zaten aktif — yok sayıldı."));
				return Result.AlreadyActive;
			}

			// 1) Hikaye modu (spooped/finale sadece story'de tetiklenir).
			Dbg(L.C("step 1/6: mode = story", "adım 1/6: mode = story"));
			GameAccess.SetMode(GameAccess.Gc, ModeField, "story");

			// 2) Eşiği önce YIKICI OLMAYAN yollarla algıla. Cache varsa kullan.
			int thr = _cachedThreshold;
			bool detectedNow = false;
			if (thr <= 0)
			{
				Dbg(L.C("step 2/6: detecting notebook threshold...", "adım 2/6: notebook eşiği aranıyor..."));
				thr = DetectNonDestructive();
				detectedNow = thr > 0;
				Dbg(string.Format(L.C("threshold detect result: {0}", "eşik tespit sonucu: {0}"), thr));
			}
			bool probed = false;

			// 3) SPOOP MODE (önce bu!): NPC'ler açılır, entrance ALÇALIR, jumpscare.
			Dbg(L.C("step 3/6: SPOOP MODE on...", "adım 3/6: SPOOP MODE açılıyor..."));
			bool spoopOk = StartSpoop();
			bool spoopedNow = IsSpooped;
			Dbg(string.Format(L.C("spoop result: invoked={0}, game 'spooped' field={1}",
				"spoop sonucu: çağrıldı={0}, oyunun 'spooped' alanı={1}"), spoopOk, spoopedNow));

			// 4) Hâlâ eşik bilinmiyorsa, spoop mode açıldıktan sonra probe ile bul.
			if (thr <= 0)
			{
				thr = DetectByProbe();
				probed = thr > 0;
				detectedNow = detectedNow || probed;
				Dbg(string.Format(L.C("probe threshold: {0}", "probe eşiği: {0}"), thr));
			}
			if (thr <= 0)
			{
				thr = 7; // son çare: vanilla
				Dbg(L.C("using fallback threshold: 7", "yedek eşik kullanılıyor: 7"));
			}

			_cachedThreshold = thr;
			threshold = thr;
			if (detectedNow)
				Dbg(string.Format(L.C("threshold detected: {0} notebooks", "eşik tespit edildi: {0} notebook"), thr));

			// 5) Eşiği ayarla ve oyunun kendi metoduyla finale'yi tetikle.
			Dbg(string.Format(L.C("step 5/6: notebooks = {0}, triggering finale...", "adım 5/6: notebooks = {0}, finale tetikleniyor..."), thr));
			GameAccess.SetInt(GameAccess.Gc, NotebooksField, thr);
			if (!probed)
			{
				bool upd = InvokeAny(UpdateNotebookMethod, "UpdateNotebook", keyword: null);
				Dbg(string.Format(L.C("UpdateNotebookCount invoked: {0}", "UpdateNotebookCount çağrıldı: {0}"), upd));
			}

			// 6) Yedek güvence: finaleMode hâlâ false ise direkt çağır + alan yaz.
			if (!GameAccess.GetBool(GameAccess.Gc, FinaleModeField))
			{
				Dbg(L.C("step 6/6: finaleMode still false — calling finale method directly...",
					"adım 6/6: finaleMode hâlâ false — finale metodu doğrudan çağrılıyor..."));
				InvokeAny(ActivateFinaleMethod, "ActivateFinale", keyword: "finale");
			}
			GameAccess.SetBool(GameAccess.Gc, FinaleModeField, true);

			// 7) Doğrulama: oyunun gerçek alanlarını geri oku ve logla.
			bool fin = GameAccess.GetBool(GameAccess.Gc, FinaleModeField);
			bool sp = IsSpooped;
			int nb = GameAccess.GetInt(GameAccess.Gc, NotebooksField, -1);
			Dbg(string.Format(L.C("VERIFY: finaleMode={0}, spooped={1}, notebooks={2}, threshold={3}",
				"DOĞRULAMA: finaleMode={0}, spooped={1}, notebooks={2}, eşik={3}"), fin, sp, nb, thr));
			if (!sp && spoopOk)
				Dbg(L.C("warning: spooped field is still false (game may ignore it).",
					"uyarı: spooped alanı hâlâ false (oyun yok sayıyor olabilir)."));
			return Result.Activated;
		}

		// ---------------------------------------------------------- spoop/finale --

		/// <summary>
		/// Spoop mode'u açar: önce bilinen metot adları, sonra "spoop" anahtar kelimeli
		/// reflection taraması (coroutine dönerse StartCoroutine ile başlatılır).
		/// </summary>
		private static bool StartSpoop()
		{
			if (InvokeAny(ActivateSpoopMethod, "SpoopMode", keyword: null)) return true;
			if (InvokeAny(null, "SpoopMode", keyword: "spoop")) return true;

			// Son çare: oyunun kendi "spooped" alanını doğrudan yaz.
			if (GameAccess.HasField(GameAccess.Gc, SpoopedField))
			{
				GameAccess.SetBool(GameAccess.Gc, SpoopedField, true);
				Dbg(L.C("spooped field set to true directly.", "spooped alanı doğrudan true yapıldı."));
				return true;
			}
			return false;
		}

		/// <summary>
		/// names listesindeki (veya keyword içeren) parametresiz metotları çağırır.
		/// Coroutine dönen metodlar StartCoroutine ile gerçekten başlatılır.
		/// </summary>
		private static bool InvokeAny(string[] names, string dbgName, string keyword)
		{
			try
			{
				Type gcType = GameAccess.Gc.GetType();

				// 1) Bilinen adlar
				if (names != null)
				{
					foreach (string n in names)
					{
						MethodInfo m = AccessTools.Method(gcType, n);
						if (m != null && m.GetParameters().Length == 0 && InvokeMethod(m, n)) return true;
					}
				}

				// 2) Anahtar kelime taraması (ör. "spoop" -> SpoopMode/ActivateSpoopMode/...)
				if (!string.IsNullOrEmpty(keyword))
				{
					foreach (MethodInfo m in gcType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
					{
						if (m.GetParameters().Length != 0) continue;
						if (m.ReturnType != typeof(void) && m.ReturnType != typeof(IEnumerator)) continue;
						if (m.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) continue;
						if (InvokeMethod(m, m.Name)) return true;
					}
				}
			}
			catch (Exception e)
			{
				Dbg(L.C("invoke error: ", "çağrı hatası: ") + e.Message);
			}
			Dbg(string.Format(L.C("{0}: no matching method found.", "{0}: uygun metot bulunamadı."), dbgName));
			return false;
		}

		private static bool InvokeMethod(MethodInfo m, string name)
		{
			try
			{
				object ret = m.Invoke(GameAccess.Gc, null);
				if (ret is IEnumerator co)
				{
					GameAccess.Gc.StartCoroutine(co); // coroutine'leri gerçekten başlat
					Dbg(string.Format(L.C("called {0}() (coroutine started)", "{0}() çağrıldı (coroutine başlatıldı)"), name));
				}
				else
				{
					Dbg(string.Format(L.C("called {0}()", "{0}() çağrıldı"), name));
				}
				return true;
			}
			catch (Exception e)
			{
				Dbg(string.Format(L.C("call failed {0}(): {1}", "çağrı başarısız {0}(): {1}"), name, e.InnerException?.Message ?? e.Message));
				return false;
			}
		}

		// ------------------------------------------------------------ tespit --

		/// <summary>Yıkıcı olmayan tespit: finale'yi tetiklemez (metin + IL).</summary>
		private static int DetectNonDestructive()
		{
			int t = DetectFromText();
			if (t > 0) return t;
			return DetectFromIl();
		}

		/// <summary>En güvenilir: oyunun gösterdiği "N/ESIK" metninden eşiği oku.</summary>
		private static int DetectFromText()
		{
			if (!GameAccess.HasGameController) return 0;

			// mode story olmalı ki "N/ESIK" formatı kullanılsın.
			GameAccess.SetMode(GameAccess.Gc, ModeField, "story");

			// Güvenli probe değeri (eşikle çakışmaz, finale tetiklemez).
			GameAccess.SetInt(GameAccess.Gc, NotebooksField, 1);
			GameAccess.InvokeGc(UpdateNotebookMethod);

			string txt = GameAccess.GetText(GameAccess.Gc, NotebookCountField);
			if (string.IsNullOrEmpty(txt)) return 0;

			// "1/20 KahretsinBooks" -> slash'tan sonraki sayı (eşik).
			Match m = Regex.Match(txt, @"/\s*(\d+)");
			if (m.Success && int.TryParse(m.Groups[1].Value, out int v) && v > 0 && v < 100000)
				return v;

			// Slash yoksa baştaki sayı olabilir.
			return 0;
		}

		/// <summary>Yedek: UpdateNotebookCount IL'indeki karşılaştırma sabitini oku.</summary>
		private static int DetectFromIl()
		{
			try
			{
				Type gcType = GameAccess.Gc.GetType();
				MethodInfo m = null;
				foreach (string n in UpdateNotebookMethod)
				{
					m = AccessTools.Method(gcType, n);
					if (m != null) break;
				}
				if (m == null) return 0;

				byte[] il = m.GetMethodBody()?.GetILAsByteArray();
				if (il == null || il.Length == 0) return 0;

				var ceqCandidates = new List<int>();
				var anyCandidates = new List<int>();

				for (int i = 0; i < il.Length; i++)
				{
					byte op = il[i];
					int? val = null;
					int consumed = 0;

					if (op == 0x1F && i + 1 < il.Length)            // ldc.i4.s
					{ val = (int)(sbyte)il[i + 1]; consumed = 1; }
					else if (op == 0x20 && i + 4 < il.Length)       // ldc.i4
					{ val = BitConverter.ToInt32(il, i + 1); consumed = 4; }
					else if (op >= 0x16 && op <= 0x1B)              // ldc.i4.0 .. ldc.i4.8
					{ val = op - 0x16; consumed = 0; }

					if (val.HasValue && val.Value >= 1 && val.Value <= 100000)
					{
						anyCandidates.Add(val.Value);
						// hemen ardından ceq (FE 01) varsa bu, bir alanla karşılaştırmadır -> eşik olma olasılığı yüksek.
						int j = i + 1 + consumed;
						if (j + 1 < il.Length && il[j] == 0xFE && il[j + 1] == 0x01)
							ceqCandidates.Add(val.Value);
					}
				}

				if (ceqCandidates.Count > 0) return ceqCandidates[ceqCandidates.Count - 1];
				if (anyCandidates.Count > 0)
				{
					// Makul aralıktaki en büyük aday (7/14/20 gibi).
					int best = 0;
					foreach (int c in anyCandidates) if (c <= 1000 && c > best) best = c;
					return best;
				}
			}
			catch { }
			return 0;
		}

		/// <summary>Son çare: finaleMode'ı sıfırlayıp değeri artırarak tetiklenen eşiği bul.
		/// DİKKAT: bu yöntem finale'yi gerçekten tetikler (entrance raise eder). Sadece
		/// diğer iki yol başarısızsa ve spoop mode açıldıktan sonra çalıştırılır.</summary>
		private static int DetectByProbe()
		{
			if (!GameAccess.HasGameController) return 0;
			for (int n = 2; n <= 64; n++)
			{
				GameAccess.SetBool(GameAccess.Gc, FinaleModeField, false);
				GameAccess.SetInt(GameAccess.Gc, NotebooksField, n);
				GameAccess.InvokeGc(UpdateNotebookMethod);
				if (GameAccess.GetBool(GameAccess.Gc, FinaleModeField))
				{
					GameAccess.SetInt(GameAccess.Gc, NotebooksField, n);
					return n; // finale tam bu değerde tetiklendi
				}
			}
			return 0;
		}
	}
}
