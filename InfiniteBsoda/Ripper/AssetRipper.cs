using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace BaldiInfiniteBsoda.Ripper
{
	internal sealed class AssetEntry
	{
		public SerializedFile SF;
		public SerializedFile.ObjectInfo Info;
		public int ClassId;
		public string ClassName;
		public string Name;
	}

	/// <summary>
	/// Oyunun *_Data dizinini tarar (v21 serialized dosyalar) ve seçilen sınıfları
	/// hedef klasöre kategori alt klasörlerine (AudioClip/, Texture2D/, ...) export eder.
	/// İşlem arka plan thread'inde; durum GUI tarafından polling edilir.
	/// </summary>
	internal static class AssetRipper
	{
		public static string DataDir;
		public static readonly List<AssetEntry> Entries = new List<AssetEntry>();

		// durum (worker thread yazar, GUI okur)
		public static volatile bool Busy;
		public static volatile bool CancelRequested;
		public static volatile string Status = "Ready";
		public static volatile int Progress;
		public static volatile int ProgressTotal;
		public static volatile string LastResult = "";
		public static readonly List<string> ExportLog = new List<string>();
		private static readonly object LogLock = new object();

		public static string Scan()
		{
			Entries.Clear();
			if (string.IsNullOrEmpty(DataDir))
			{
				try { DataDir = UnityEngine.Application.dataPath; } catch { }
			}
			if (string.IsNullOrEmpty(DataDir) || !Directory.Exists(DataDir))
				return L.C("Data folder not found: ", "Data dizini bulunamadı: ") + DataDir;

			string[] files;
			try { files = Directory.GetFiles(DataDir, "*", SearchOption.TopDirectoryOnly); }
			catch (Exception e) { return L.C("Scan error: ", "Tarama hatası: ") + e.Message; }

			int parsed = 0;
			foreach (string f in files)
			{
				try
				{
					using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read))
					{
						if (fs.Length < 20) continue;
						var head = new byte[20];
						fs.Read(head, 0, 20);
						uint ver = (uint)((head[8] << 24) | (head[9] << 16) | (head[10] << 8) | head[11]);
						if (ver != 21) continue;
					}
					var sf = SerializedFile.Load(f);
					parsed++;
					foreach (var info in sf.Objects)
					{
						string cls = ClassId.Name(info.ClassId);
						string name = AssetClass.TryReadName(sf, info);
						Entries.Add(new AssetEntry
						{
							SF = sf,
							Info = info,
							ClassId = info.ClassId,
							ClassName = cls,
							Name = string.IsNullOrEmpty(name) ? info.PathId.ToString() : name,
						});
					}
				}
				catch (Exception e)
				{
					AddLog(L.C("SKIP ", "ATLA ") + Path.GetFileName(f) + ": " + e.Message);
				}
			}
			Entries.Sort((a, b) =>
			{
				int c = string.CompareOrdinal(a.ClassName, b.ClassName);
				return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
			});
			return string.Format(L.C("{0} files / {1} objects", "{0} dosya / {1} nesne"), parsed, Entries.Count);
		}

		public static void AddLog(string line)
		{
			lock (LogLock)
			{
				ExportLog.Add(line);
				if (ExportLog.Count > 400) ExportLog.RemoveAt(0);
			}
			ModConsole.Add(line);   // her log satırı oyun içi konsol overlay'ine de düşsün
		}

		public static string[] GetLogSnapshot()
		{
			lock (LogLock) return ExportLog.ToArray();
		}

		/// <summary>Arka planda export başlatır (zaten çalışıyorsa false döner).</summary>
		public static bool StartExport(string destDir, HashSet<int> classIds)
		{
			if (Busy) return false;
			if (Entries.Count == 0) return false;
			if (classIds == null || classIds.Count == 0) return false;

			CancelRequested = false;
			Busy = true;
			Status = L.C("Export starting...", "Export başlıyor...");
			Progress = 0;
			ProgressTotal = 0;
			var entries = Entries.FindAll(e => classIds.Contains(e.ClassId));
			ProgressTotal = entries.Count;

			ThreadPool.QueueUserWorkItem(_ =>
			{
				try
				{
					RunExport(destDir, entries);
				}
				catch (Exception e)
				{
					Status = L.C("ERROR: ", "HATA: ") + e.Message;
					AddLog(L.C("Export ERROR: ", "Export HATA: ") + e);
				}
				finally
				{
					Busy = false;
				}
			});
			return true;
		}

		private static void RunExport(string destDir, List<AssetEntry> entries)
		{
			int done = 0, exported = 0, skipped = 0, failed = 0;
			var usedNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
			var streams = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);

			try
			{
				foreach (var e in entries)
				{
					if (CancelRequested) { Status = L.C("Cancelled", "İptal edildi"); break; }

					Status = $"[{done + 1}/{entries.Count}] {e.ClassName}: {e.Name}";
					try
					{
						string folder = Path.Combine(destDir, e.ClassName);
						Directory.CreateDirectory(folder);

						string nameKey = e.ClassName;
						if (!usedNames.TryGetValue(nameKey, out var set))
							usedNames[nameKey] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

						bool ok = ExportOne(e, folder, set, streams, out bool did);
						if (did) { if (ok) exported++; else failed++; }
						else skipped++;
					}
					catch (Exception ex)
					{
						failed++;
						AddLog($"{e.ClassName}/{e.Name}: {ex.GetType().Name} {ex.Message}");
					}
					done++;
					Progress = done;
				}
			}
			finally
			{
				foreach (var fs in streams.Values)
					try { fs.Dispose(); } catch { }
			}

			if (CancelRequested)
				LastResult = string.Format(L.C("Cancelled: {0} written, {1} skipped, {2} errors",
					"İptal: {0} yazıldı, {1} atlandı, {2} hata"), exported, skipped, failed);
			else
				LastResult = string.Format(L.C("Done: {0} files written ({1} skipped, {2} errors) -> {3}",
					"Bitti: {0} dosya yazıldı ({1} atlandı, {2} hata) -> {3}"), exported, skipped, failed, destDir);
			Status = LastResult;
			AddLog(LastResult);
		}

		/// <summary>Tek nesneyi export eder. did=false -> atlandı (ör. boş/desteklenmiyor).</summary>
		private static bool ExportOne(AssetEntry e, string folder, HashSet<string> used,
			Dictionary<string, FileStream> streams, out bool did)
		{
			did = true;
			switch (e.ClassId)
			{
				case 28: return ExportTexture(e, folder, used);
				case 83: return ExportAudio(e, folder, used, streams);
				case 49: return ExportTextAsset(e, folder, used);
				case 115: return ExportMonoScript(e, folder, used);
				default: return ExportRaw(e, folder, used);
			}
		}

		private static string UniquePath(string folder, string name, HashSet<string> used, string ext)
		{
			string baseName = AssetClass.Sanitize(name);
			string candidate = baseName;
			int i = 1;
			while (!used.Add(candidate))
				candidate = baseName + "_" + (i++);
			return Path.Combine(folder, candidate + ext);
		}

		// -------------------------------------------------------- Texture2D --

		private static bool ExportTexture(AssetEntry e, string folder, HashSet<string> used)
		{
			var t = AssetClass.ReadTexture2D(e.SF, e.Info);
			if (t.Width <= 0 || t.Height <= 0) return false;

			byte[] raw = ReadStreamData(e, t.Streaming, t.StreamPath, t.StreamOffset, t.StreamSize,
				t.InlinePos, t.InlineSize);
			if (raw == null) return false;

			byte[] rgba = PngEncoder.DecodeToRgba(raw, t.Width, t.Height, t.Format);
			if (rgba == null)
			{
				// desteklenmeyen format (ör. BC7): ham döküm
				string rawPath = UniquePath(folder, t.Name + "_" + PngEncoder.FormatName(t.Format), used, ".rawtex");
				File.WriteAllBytes(rawPath, raw);
				AddLog($"Texture2D/{t.Name}: {PngEncoder.FormatName(t.Format)} " +
					L.C("PNG not supported -> .rawtex", "PNG desteklenmiyor -> .rawtex"));
				return true;
			}
			byte[] png = PngEncoder.EncodePng(rgba, t.Width, t.Height);
			if (png == null) return false;
			string path = UniquePath(folder, $"{t.Name}_{t.Width}x{t.Height}_{PngEncoder.FormatName(t.Format)}", used, ".png");
			File.WriteAllBytes(path, png);
			return true;
		}

		// ---------------------------------------------------------- AudioClip

		private static bool ExportAudio(AssetEntry e, string folder, HashSet<string> used,
			Dictionary<string, FileStream> streams)
		{
			var a = AssetClass.ReadAudioClip(e.SF, e.Info);
			if (a.Size <= 0 || string.IsNullOrEmpty(a.Source)) return false;

			byte[] fsb = ReadStreamData(e, true, a.Source, a.Offset, a.Size, 0, 0);
			if (fsb == null) return false;

			byte[] ogg = Fsb5.ToOgg(fsb, a.Name, null);
			if (ogg != null)
			{
				string path = UniquePath(folder, a.Name, used, ".ogg");
				File.WriteAllBytes(path, ogg);
				return true;
			}
			string rawPath = UniquePath(folder, a.Name + "_raw", used, ".fsb");
			File.WriteAllBytes(rawPath, fsb);
			return true;
		}

		// -------------------------------------------------------- TextAsset --

		private static bool ExportTextAsset(AssetEntry e, string folder, HashSet<string> used)
		{
			var t = AssetClass.ReadTextAsset(e.SF, e.Info);
			if (t.Bytes == null || t.Bytes.Length == 0) return false;
			string ext = GuessTextExt(t.Bytes);
			string path = UniquePath(folder, t.Name, used, ext);
			File.WriteAllBytes(path, t.Bytes);
			return true;
		}

		private static string GuessTextExt(byte[] b)
		{
			if (b.Length == 0) return ".bytes";
			if (b[0] == (byte)'{' || b[0] == (byte)'[') return ".json";
			int printable = 0, n = Math.Min(b.Length, 512);
			for (int i = 0; i < n; i++)
			{
				byte c = b[i];
				if (c == 9 || c == 10 || c == 13 || (c >= 32 && c < 127)) printable++;
			}
			return printable >= n * 0.9 ? ".txt" : ".bytes";
		}

		// ------------------------------------------------------- MonoScript --

		private static bool ExportMonoScript(AssetEntry e, string folder, HashSet<string> used)
		{
			string name = AssetClass.TryReadName(e.SF, e.Info) ?? e.Info.PathId.ToString();
			string path = UniquePath(folder, name, used, ".script.txt");
			var sb = new StringBuilder();
			sb.AppendLine("class: " + name);
			sb.AppendLine("pathID: " + e.Info.PathId);
			sb.AppendLine("file: " + e.SF.FileName);
			sb.AppendLine("classID: " + e.Info.ClassId);
			File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
			return true;
		}

		private static bool ExportRaw(AssetEntry e, string folder, HashSet<string> used)
		{
			var r = e.SF.ReaderAt(e.Info);
			byte[] data = r.ReadBytes((int)Math.Min(e.Info.Size, int.MaxValue));
			if (data.Length == 0) return false;
			string path = UniquePath(folder, e.Name + "_" + e.Info.PathId, used, ".bin");
			File.WriteAllBytes(path, data);
			return true;
		}

		// ---------------------------------------------------------- veri okuma

		/// <summary>
		/// Streaming (kaynak dosya + mutlak offset) veya inline (nesne içi) veri okur.
		/// Kaynak yolu Data dizinine göredir (Texture2D .resS / AudioClip .resource).
		/// </summary>
		private static byte[] ReadStreamData(AssetEntry e, bool streaming, string sourcePath,
			long offset, long size, int inlinePos, int inlineSize)
		{
			if (streaming)
			{
				if (string.IsNullOrEmpty(sourcePath) || size <= 0) return null;
				string full = Path.IsPathRooted(sourcePath) ? sourcePath : Path.Combine(DataDir, sourcePath);
				if (!File.Exists(full)) { AddLog(L.C("source missing: ", "kaynak yok: ") + sourcePath); return null; }
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
						var trimmed = new byte[read];
						Array.Copy(buf, trimmed, read);
						return trimmed;
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
