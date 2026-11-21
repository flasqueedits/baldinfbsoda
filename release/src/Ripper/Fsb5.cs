using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BaldiInfiniteBsoda.Ripper
{
	/// <summary>
	/// FSB5 (FMOD Sample Bank v5) parser + Vorbis -> OGG yeniden kurulumu.
	///
	/// Bulgu (bu oyun üzerinde doğrulandı):
	///   - header 60B: magic, ver=1, numSamples, sampleHeaderSize, nameTableSize, dataSize, mode
	///   - sample: u64 bitfield {extra:1, freq:4, stereo:1, dataOffset:28, samples:30}
	///   - extra chunk: {hasNext:1, size:24, type:7}; VORBISDATA(type 11) = crc32 + tableBytes + N×{granule,offset}
	///   - paketler: u16 uzunluk önekli, uzunluk 0'da bitiyor
	///   - setup header dosyanın İÇİNDE DEĞİL: crc32 -&gt; UnityPlayer.dll'den çıkarıldı (VorbisSetupTable)
	///   - granule: seek tablosu noktaları arasında doğrusal interpolasyon, son paket = samples
	/// </summary>
	internal static class Fsb5
	{
		private const uint FsbMagic = 0x35425346; // 'FSB5'

		private static readonly int[] FreqTable =
			{ 0, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 64000 };

		private sealed class Sample
		{
			public int FreqIdx, Channels, DataOffset, Samples;
			public byte[] VorbisData;
		}

		/// <summary>FSB5 byte'larını OGG'ye çevirir; başarısızsa null (çağıran ham .fsb yazar).</summary>
		public static byte[] ToOgg(byte[] fsb, string dbgName, List<string> log)
		{
			try
			{
				return ToOggInternal(fsb, dbgName, log);
			}
			catch (Exception e)
			{
				log?.Add($"{dbgName}: OGG hatası {e.GetType().Name} -> ham .fsb");
				return null;
			}
		}

		private static byte[] ToOggInternal(byte[] fsb, string dbgName, List<string> log)
		{
			if (fsb == null || fsb.Length < 64) return null;
			if (Rd32(fsb, 0) != FsbMagic) return null;
			if (Rd32(fsb, 4) != 1) return null;
			uint numSamples = Rd32(fsb, 8);
			uint shSize = Rd32(fsb, 12);
			uint nameSize = Rd32(fsb, 16);
			uint dataSize = Rd32(fsb, 20);
			uint mode = Rd32(fsb, 24);
			if (numSamples == 0 || numSamples > 512) return null;

			var samples = new List<Sample>();
			int p = 60;
			for (int i = 0; i < numSamples; i++)
			{
				if (p + 8 > fsb.Length) return null;
				ulong u = Rd64(fsb, p); p += 8;
				var s = new Sample();
				bool extra = (u & 1) != 0;
				s.FreqIdx = (int)((u >> 1) & 0xF);
				bool stereo = ((u >> 5) & 1) != 0;
				s.DataOffset = (int)((u >> 6) & 0xFFFFFFF);
				s.Samples = (int)((u >> 34) & 0x3FFFFFFF);
				s.Channels = stereo ? 2 : 1;
				while (extra)
				{
					if (p + 4 > fsb.Length) return null;
					uint h = Rd32(fsb, p); p += 4;
					extra = (h & 1) != 0;
					int size = (int)((h >> 1) & 0xFFFFFF);
					int type = (int)((h >> 25) & 0x7F);
					if (p + size > fsb.Length) return null;
					if (type == 11 && size >= 8)
						s.VorbisData = Copy(fsb, p, size);
					p += size;
				}
				samples.Add(s);
			}

			int dataStart = 60 + (int)shSize + (int)nameSize;
			if (dataStart > fsb.Length) return null;
			int dataEnd = (int)Math.Min((long)dataStart + dataSize, fsb.Length);

			if (mode != 15)
			{
				log?.Add($"{dbgName}: mode={mode} (vorbis değil) -> ham .fsb");
				return null;
			}

			var ogg = new OggWriter();
			for (int si = 0; si < samples.Count; si++)
			{
				var s = samples[si];
				if (s.VorbisData == null)
				{
					log?.Add($"{dbgName}: VORBISDATA chunk yok -> ham .fsb");
					return null;
				}
				uint crc = Rd32(s.VorbisData, 0);
				byte[] setup = VorbisSetupTable.Get(crc);
				if (setup == null)
				{
					log?.Add($"{dbgName}: crc 0x{crc:X8} setup tablosunda yok -> ham .fsb");
					return null;
				}

				int regionStart = dataStart + s.DataOffset;
				int regionEnd = (si + 1 < samples.Count)
					? dataStart + samples[si + 1].DataOffset
					: dataEnd;
				if (regionStart < dataStart || regionStart > dataEnd) regionStart = dataStart;
				if (regionEnd > dataEnd || regionEnd < regionStart) regionEnd = dataEnd;

				var packets = new List<byte[]>();
				var ends = new List<int>();
				int q = regionStart;
				while (q + 2 <= regionEnd)
				{
					int len = fsb[q] | (fsb[q + 1] << 8);
					if (len == 0) break;
					if (q + 2 + len > regionEnd) break;
					packets.Add(Copy(fsb, q + 2, len));
					ends.Add(q + 2 + len - dataStart);
					q += 2 + len;
				}
				if (packets.Count == 0)
				{
					log?.Add($"{dbgName}: paket yok -> ham .fsb");
					return null;
				}

				var anchorOff = new List<int>();
				var anchorGr = new List<long>();
				int tblBytes = (int)Rd32(s.VorbisData, 4);
				int entries = Math.Min(tblBytes / 8, (s.VorbisData.Length - 8) / 8);
				for (int e = 0; e < entries; e++)
				{
					int o = 8 + e * 8;
					anchorGr.Add(Rd32(s.VorbisData, o));
					anchorOff.Add((int)Rd32(s.VorbisData, o + 4));
				}

				int freq = (s.FreqIdx >= 0 && s.FreqIdx < FreqTable.Length) ? FreqTable[s.FreqIdx] : 44100;
				if (freq <= 0) freq = 44100;

				ogg.BeginStream(freq, s.Channels, setup);
				for (int k = 0; k < packets.Count; k++)
				{
					long granule;
					if (k == packets.Count - 1 && s.Samples > 0)
						granule = s.Samples;
					else
						granule = Interpolate(ends[k], anchorOff, anchorGr, s.Samples);
					if (granule < 0) granule = 0;
					ogg.WriteAudioPacket(packets[k], granule, k == packets.Count - 1);
				}
			}
			return ogg.Finish();
		}

		private static long Interpolate(int x, List<int> aOff, List<long> aGr, int totalSamples)
		{
			if (aOff.Count == 0) return 0;
			if (x <= aOff[0])
			{
				if (aOff[0] <= 0) return aGr[0];
				return (long)((double)x * aGr[0] / aOff[0]);
			}
			for (int i = 1; i < aOff.Count; i++)
			{
				if (x <= aOff[i])
				{
					long span = aOff[i] - aOff[i - 1];
					if (span <= 0) return aGr[i];
					double t = (double)(x - aOff[i - 1]) / span;
					return aGr[i - 1] + (long)(t * (aGr[i] - aGr[i - 1]));
				}
			}
			int last = aOff.Count - 1;
			if (totalSamples <= aGr[last]) return aGr[last];
			int xspan = x - aOff[last];
			if (xspan <= 0) return aGr[last];
			double tt = Math.Min(1.0, (double)(x - aOff[last]) / xspan);
			return aGr[last] + (long)(tt * (totalSamples - aGr[last]));
		}

		private static byte[] Copy(byte[] src, int o, int n)
		{
			var v = new byte[n];
			Array.Copy(src, o, v, 0, n);
			return v;
		}

		private static uint Rd32(byte[] b, int o)
			=> (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

		private static ulong Rd64(byte[] b, int o)
			=> Rd32(b, o) | ((ulong)Rd32(b, o + 4) << 32);
	}

	/// <summary>
	/// Ogg konteyner yazıcı: id/comment/setup başlıkları sentezlenir; her ses paketi
	/// lacing'e göre sayfalara bölünür (255'lik segmentler). CRC Ogg polinomu ile.
	/// </summary>
	internal sealed class OggWriter
	{
		private uint _serial;
		private uint _seq;
		private long _lastGranule;
		private readonly MemoryStream _out = new MemoryStream();

		public void BeginStream(int freq, int channels, byte[] setupHeader)
		{
			_serial = (uint)Environment.TickCount & 0x7FFFFFFF;
			if (_serial == 0) _serial = 1;
			_seq = 0;
			_lastGranule = 0;

			// identification header
			var id = new MemoryStream();
			id.WriteByte(0x01);
			id.Write(Encoding.ASCII.GetBytes("vorbis"), 0, 6);
			W32(id, 0);                     // vorbis version
			id.WriteByte((byte)channels);
			W32(id, (uint)freq);
			W32(id, 0); W32(id, 0); W32(id, 0); // bitrate max/nom/min
			id.WriteByte((byte)((11 << 4) | 8)); // blocksize_1=2048 (0x800), blocksize_0=256 (0x100)
			id.WriteByte(0x01);             // framing flag
			EmitPage(id.ToArray(), 2 /*BOS*/, 0);

			// comment + setup tek sayfada
			var cs = new MemoryStream();
			cs.WriteByte(0x03);
			cs.Write(Encoding.ASCII.GetBytes("vorbis"), 0, 6);
			byte[] vendor = Encoding.UTF8.GetBytes("baldi-ripper");
			W32(cs, (uint)vendor.Length);
			cs.Write(vendor, 0, vendor.Length);
			W32(cs, 0);                     // comment count
			cs.WriteByte(0x01);             // comment framing
			cs.Write(setupHeader, 0, setupHeader.Length);
			EmitPage(cs.ToArray(), 0, 0);
		}

		/// <summary>Bir vorbis ses paketini (gerekirse çoklu sayfaya bölünerek) yazar.</summary>
		public void WriteAudioPacket(byte[] packet, long granule, bool lastPacket)
		{
			// lacing segmentleri
			var segs = new List<byte>();
			int rem = packet.Length;
			while (rem >= 255) { segs.Add(255); rem -= 255; }
			segs.Add((byte)rem);

			int idx = 0;
			bool first = true;
			while (idx < segs.Count)
			{
				int take = Math.Min(255, segs.Count - idx);
				bool lastChunk = idx + take >= segs.Count;

				int bodyOff = 0;
				for (int i = 0; i < idx; i++) bodyOff += segs[i];
				int bodyLen = 0;
				for (int i = idx; i < idx + take; i++) bodyLen += segs[i];

				var body = new byte[bodyLen];
				Array.Copy(packet, bodyOff, body, 0, bodyLen);

				byte headerType = 0;
				if (!first) headerType |= 1;                 // continued packet
				if (lastPacket && lastChunk) headerType |= 4; // EOS

				// tamamlanan son paketin granule'i: son chunk'ta granule, aksi halde önceki değer
				long g = lastChunk ? granule : _lastGranule;

				EmitPage(body, headerType, g, segs.GetRange(idx, take).ToArray());

				if (lastChunk) _lastGranule = granule;
				idx += take;
				first = false;
			}
		}

		public byte[] Finish() => _out.ToArray();

		private void EmitPage(byte[] body, byte headerType, long granule, byte[] segments = null)
		{
			if (segments == null)
			{
				var list = new List<byte>();
				int rem = body.Length;
				while (rem >= 255) { list.Add(255); rem -= 255; }
				list.Add((byte)rem);
				segments = list.ToArray();
			}

			var page = new MemoryStream();
			page.Write(Encoding.ASCII.GetBytes("OggS"), 0, 4);
			page.WriteByte(0);                      // structure version
			page.WriteByte(headerType);
			W64(page, granule);                     // granule position
			W32(page, _serial);
			W32(page, _seq++);
			W32(page, 0);                           // crc placeholder
			page.WriteByte((byte)segments.Length);
			page.Write(segments, 0, segments.Length);
			page.Write(body, 0, body.Length);

			byte[] bytes = page.ToArray();
			uint crc = OggCrc.Compute(bytes);
			bytes[22] = (byte)crc;
			bytes[23] = (byte)(crc >> 8);
			bytes[24] = (byte)(crc >> 16);
			bytes[25] = (byte)(crc >> 24);
			_out.Write(bytes, 0, bytes.Length);
		}

		private static void W32(Stream s, uint v)
		{
			s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8));
			s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24));
		}

		private static void W64(Stream s, long v)
		{
			W32(s, (uint)v);
			W32(s, (uint)((ulong)v >> 32));
		}
	}

	/// <summary>Ogg CRC-32 (poly 0x04C11DB7, init 0, yansıtılmamış).</summary>
	internal static class OggCrc
	{
		private static readonly uint[] Table = Build();

		private static uint[] Build()
		{
			var t = new uint[256];
			for (uint i = 0; i < 256; i++)
			{
				uint r = i << 24;
				for (int k = 0; k < 8; k++)
					r = (r & 0x80000000) != 0 ? (r << 1) ^ 0x04C11DB7 : r << 1;
				t[i] = r;
			}
			return t;
		}

		public static uint Compute(byte[] data)
		{
			uint crc = 0;
			for (int i = 0; i < data.Length; i++)
				crc = (crc << 8) ^ Table[((crc >> 24) ^ data[i]) & 0xFF];
			return crc;
		}
	}
}
