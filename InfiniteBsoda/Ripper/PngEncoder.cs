using System;
using System.IO;
using System.IO.Compression;

namespace BaldiInfiniteBsoda.Ripper
{
	/// <summary>
	/// Texture ham verisini RGBA32'ye çevirir + el yapımı PNG yazar.
	/// (net472'de ZLibStream yok: zlib başlığı + DeflateStream + elle Adler32.)
	/// Desteklenen formatlar: Unity TextureFormat enum değerleri.
	/// </summary>
	internal static class PngEncoder
	{
		/// <summary>Format adı (GUI/durum için).</summary>
		public static string FormatName(int f)
		{
			switch (f)
			{
				case 1: return "Alpha8";
				case 2: return "ARGB4444";
				case 3: return "RGB24";
				case 4: return "RGBA32";
				case 5: return "ARGB32";
				case 6: return "RGB565";
				case 10: return "DXT1";
				case 12: return "DXT5";
				case 13: return "RGBA4444";
				case 14: return "BGRA32";
				case 25: return "BC7";
				default: return "Format" + f;
			}
		}

		public static bool IsSupported(int f)
		{
			switch (f)
			{
				case 1: case 2: case 3: case 4: case 5: case 6:
				case 10: case 12: case 13: case 14:
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// mip0'u RGBA'ya çevirir. Unity verisi alttan yukarı saklanır -> PNG için
		/// satırlar döndürülür (flipY). Dönen byte[]: w*h*4.
		/// </summary>
		public static byte[] DecodeToRgba(byte[] raw, int w, int h, int format)
		{
			if (raw == null || w <= 0 || h <= 0) return null;
			var dst = new byte[w * h * 4];

			switch (format)
			{
				case 4: // RGBA32
				{
					int need = w * h * 4;
					if (raw.Length < need) return null;
					CopyFlipped(raw, dst, w, h, 4);
					return dst;
				}
				case 14: // BGRA32
				{
					int need = w * h * 4;
					if (raw.Length < need) return null;
					for (int y = 0; y < h; y++)
					{
						int si = (h - 1 - y) * w * 4;
						int di = y * w * 4;
						for (int x = 0; x < w; x++)
						{
							dst[di + 0] = raw[si + 2];
							dst[di + 1] = raw[si + 1];
							dst[di + 2] = raw[si + 0];
							dst[di + 3] = raw[si + 3];
							si += 4; di += 4;
						}
					}
					return dst;
				}
				case 5: // ARGB32
				{
					int need = w * h * 4;
					if (raw.Length < need) return null;
					for (int y = 0; y < h; y++)
					{
						int si = (h - 1 - y) * w * 4;
						int di = y * w * 4;
						for (int x = 0; x < w; x++)
						{
							dst[di + 0] = raw[si + 1];
							dst[di + 1] = raw[si + 2];
							dst[di + 2] = raw[si + 3];
							dst[di + 3] = raw[si + 0];
							si += 4; di += 4;
						}
					}
					return dst;
				}
				case 3: // RGB24
				{
					int need = w * h * 3;
					if (raw.Length < need) return null;
					for (int y = 0; y < h; y++)
					{
						int si = (h - 1 - y) * w * 3;
						int di = y * w * 4;
						for (int x = 0; x < w; x++)
						{
							dst[di + 0] = raw[si + 0];
							dst[di + 1] = raw[si + 1];
							dst[di + 2] = raw[si + 2];
							dst[di + 3] = 255;
							si += 3; di += 4;
						}
					}
					return dst;
				}
				case 1: // Alpha8
				{
					int need = w * h;
					if (raw.Length < need) return null;
					for (int y = 0; y < h; y++)
					{
						int si = (h - 1 - y) * w;
						int di = y * w * 4;
						for (int x = 0; x < w; x++)
						{
							byte a = raw[si++];
							dst[di + 0] = 255;
							dst[di + 1] = 255;
							dst[di + 2] = 255;
							dst[di + 3] = a;
							di += 4;
						}
					}
					return dst;
				}
				case 6: // RGB565
				{
					int need = w * h * 2;
					if (raw.Length < need) return null;
					for (int y = 0; y < h; y++)
					{
						int si = (h - 1 - y) * w * 2;
						int di = y * w * 4;
						for (int x = 0; x < w; x++)
						{
							int v = raw[si] | (raw[si + 1] << 8); si += 2;
							byte r = (byte)(((v >> 11) & 0x1F) * 255 / 31);
							byte g = (byte)(((v >> 5) & 0x3F) * 255 / 63);
							byte b = (byte)((v & 0x1F) * 255 / 31);
							dst[di++] = r; dst[di++] = g; dst[di++] = b; dst[di++] = 255;
						}
					}
					return dst;
				}
				case 2: // ARGB4444
				{
					int need = w * h * 2;
					if (raw.Length < need) return null;
					for (int y = 0; y < h; y++)
					{
						int si = (h - 1 - y) * w * 2;
						int di = y * w * 4;
						for (int x = 0; x < w; x++)
						{
							int v = raw[si] | (raw[si + 1] << 8); si += 2;
							dst[di++] = N4((v >> 8) & 0xF);   // R
							dst[di++] = N4((v >> 4) & 0xF);   // G
							dst[di++] = N4(v & 0xF);          // B
							dst[di++] = N4((v >> 12) & 0xF);  // A
						}
					}
					return dst;
				}
				case 13: // RGBA4444
				{
					int need = w * h * 2;
					if (raw.Length < need) return null;
					for (int y = 0; y < h; y++)
					{
						int si = (h - 1 - y) * w * 2;
						int di = y * w * 4;
						for (int x = 0; x < w; x++)
						{
							int v = raw[si] | (raw[si + 1] << 8); si += 2;
							dst[di++] = N4((v >> 12) & 0xF);
							dst[di++] = N4((v >> 8) & 0xF);
							dst[di++] = N4((v >> 4) & 0xF);
							dst[di++] = N4(v & 0xF);
						}
					}
					return dst;
				}
				case 10: // DXT1
					return DecodeDxt(raw, w, h, dxt5: false);
				case 12: // DXT5
					return DecodeDxt(raw, w, h, dxt5: true);
				default:
					return null;
			}
		}

		private static byte N4(int v) => (byte)(v * 17);

		private static void CopyFlipped(byte[] src, byte[] dst, int w, int h, int bpp)
		{
			int stride = w * bpp;
			for (int y = 0; y < h; y++)
			{
				int si = (h - 1 - y) * stride;
				if (si + stride > src.Length) return;
				Array.Copy(src, si, dst, y * stride, stride);
			}
		}

		// ---------------------------------------------------------- DXT ------

		private static byte[] DecodeDxt(byte[] raw, int w, int h, bool dxt5)
		{
			int bw = (w + 3) / 4;
			int bh = (h + 3) / 4;
			int blockSize = dxt5 ? 16 : 8;
			int need = bw * bh * blockSize;
			if (raw.Length < need) return null;

			var dst = new byte[w * h * 4];
			int off = 0;
			for (int by = 0; by < bh; by++)
			{
				for (int bx = 0; bx < bw; bx++, off += blockSize)
					DecodeBlock(raw, off, dxt5, bx * 4, by * 4, w, h, dst);
			}
			// DXT verisi de alttan yukarı blok sırası ->全局 flip
			return FlipRgba(dst, w, h);
		}

		private static void DecodeBlock(byte[] raw, int off, bool dxt5, int x0, int y0, int w, int h, byte[] dst)
		{
			int colorOff = off;
			if (dxt5)
			{
				var alpha = new byte[16];
				byte a0 = raw[off], a1 = raw[off + 1];
				ulong bits = 0;
				for (int i = 0; i < 6; i++) bits |= (ulong)raw[off + 2 + i] << (8 * i);

				var atab = new byte[8];
				atab[0] = a0; atab[1] = a1;
				if (a0 > a1)
				{
					for (int i = 1; i <= 6; i++) atab[1 + i] = (byte)(((7 - i) * a0 + i * a1) / 7);
				}
				else
				{
					for (int i = 1; i <= 4; i++) atab[1 + i] = (byte)(((5 - i) * a0 + i * a1) / 5);
					atab[6] = 0; atab[7] = 255;
				}
				for (int i = 0; i < 16; i++)
					alpha[i] = atab[(bits >> (3 * i)) & 7];

				for (int py = 0; py < 4; py++)
				{
					int y = y0 + py;
					if (y >= h) continue;
					for (int px = 0; px < 4; px++)
					{
						int x = x0 + px;
						if (x >= w) continue;
						dst[(y * w + x) * 4 + 3] = alpha[py * 4 + px];
					}
				}
				colorOff = off + 16;
			}

			ushort c0 = (ushort)(raw[colorOff] | (raw[colorOff + 1] << 8));
			ushort c1 = (ushort)(raw[colorOff + 2] | (raw[colorOff + 3] << 8));
			byte[,] col = new byte[4, 4];
			Expand565(c0, col, 0);
			Expand565(c1, col, 1);
			if (!dxt5 || c0 > c1) // DXT1: c0<=c1 ise 1-bit alfa modu (şeffaf)
			{
				for (int i = 0; i < 3; i++) { col[2, i] = (byte)((2 * col[0, i] + col[1, i]) / 3); col[3, i] = (byte)((col[0, i] + 2 * col[1, i]) / 3); }
			}
			else
			{
				for (int i = 0; i < 3; i++) { col[2, i] = (byte)((col[0, i] + col[1, i]) / 2); col[3, i] = 0; }
			}
			uint bits32 = (uint)(raw[colorOff + 4] | (raw[colorOff + 5] << 8) | (raw[colorOff + 6] << 16) | (raw[colorOff + 7] << 24));

			for (int py = 0; py < 4; py++)
			{
				int y = y0 + py;
				if (y >= h) continue;
				for (int px = 0; px < 4; px++)
				{
					int x = x0 + px;
					if (x >= w) continue;
					int ci = (int)((bits32 >> (2 * (py * 4 + px))) & 3);
					int di = (y * w + x) * 4;
					dst[di + 0] = col[ci, 0];
					dst[di + 1] = col[ci, 1];
					dst[di + 2] = col[ci, 2];
					if (!dxt5)   // DXT5 alfa yukarıda yazıldı
						dst[di + 3] = (byte)(ci == 3 && c0 <= c1 ? 0 : 255);
				}
			}
		}

		private static void Expand565(ushort c, byte[,] col, int idx)
		{
			col[idx, 0] = (byte)(((c >> 11) & 0x1F) * 255 / 31);
			col[idx, 1] = (byte)(((c >> 5) & 0x3F) * 255 / 63);
			col[idx, 2] = (byte)((c & 0x1F) * 255 / 31);
		}

		private static byte[] FlipRgba(byte[] src, int w, int h)
		{
			var dst = new byte[src.Length];
			int stride = w * 4;
			for (int y = 0; y < h; y++)
				Array.Copy(src, (h - 1 - y) * stride, dst, y * stride, stride);
			return dst;
		}

		// ---------------------------------------------------------- PNG ------

		/// <summary>RGBA byte[]'den PNG dosyası üretir.</summary>
		public static byte[] EncodePng(byte[] rgba, int w, int h)
		{
			if (rgba == null || w <= 0 || h <= 0 || rgba.Length < w * h * 4) return null;

			// scanline'lar: filter byte 0 + satır
			int stride = w * 4;
			var raw = new byte[(stride + 1) * h];
			for (int y = 0; y < h; y++)
			{
				raw[y * (stride + 1)] = 0;
				Array.Copy(rgba, y * stride, raw, y * (stride + 1) + 1, stride);
			}

			byte[] z = ZlibCompress(raw);

			using (var ms = new MemoryStream())
			{
				ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);

				// IHDR
				var ihdr = new byte[13];
				ihdr[0] = (byte)(w >> 24); ihdr[1] = (byte)(w >> 16); ihdr[2] = (byte)(w >> 8); ihdr[3] = (byte)w;
				ihdr[4] = (byte)(h >> 24); ihdr[5] = (byte)(h >> 16); ihdr[6] = (byte)(h >> 8); ihdr[7] = (byte)h;
				ihdr[8] = 8;   // bit depth
				ihdr[9] = 6;   // color type RGBA
				ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
				WriteChunk(ms, "IHDR", ihdr);

				WriteChunk(ms, "IDAT", z);
				WriteChunk(ms, "IEND", new byte[0]);
				return ms.ToArray();
			}
		}

		private static void WriteChunk(Stream s, string type, byte[] data)
		{
			byte[] t = { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
			uint len = (uint)data.Length;
			var head = new byte[4];
			head[0] = (byte)(len >> 24); head[1] = (byte)(len >> 16); head[2] = (byte)(len >> 8); head[3] = (byte)len;
			s.Write(head, 0, 4);
			s.Write(t, 0, 4);
			s.Write(data, 0, data.Length);

			var crcIn = new byte[4 + data.Length];
			Array.Copy(t, 0, crcIn, 0, 4);
			Array.Copy(data, 0, crcIn, 4, data.Length);
			uint crc = PngCrc.Compute(crcIn);
			var cb = new byte[4];
			cb[0] = (byte)(crc >> 24); cb[1] = (byte)(crc >> 16); cb[2] = (byte)(crc >> 8); cb[3] = (byte)crc;
			s.Write(cb, 0, 4);
		}

		/// <summary>zlib: 0x78 0x9C + deflate + Adler32.</summary>
		private static byte[] ZlibCompress(byte[] data)
		{
			using (var ms = new MemoryStream())
			{
				ms.WriteByte(0x78);
				ms.WriteByte(0x9C);
				using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true))
					ds.Write(data, 0, data.Length);
				uint adler = Adler32(data);
				ms.WriteByte((byte)(adler >> 24));
				ms.WriteByte((byte)(adler >> 16));
				ms.WriteByte((byte)(adler >> 8));
				ms.WriteByte((byte)adler);
				return ms.ToArray();
			}
		}

		private static uint Adler32(byte[] data)
		{
			uint a = 1, b = 0;
			foreach (byte c in data)
			{
				a = (a + c) % 65521;
				b = (b + a) % 65521;
			}
			return (b << 16) | a;
		}
	}

	/// <summary>PNG için standart CRC-32 (poly 0xEDB88320).</summary>
	internal static class PngCrc
	{
		private static readonly uint[] Table = Build();

		private static uint[] Build()
		{
			var t = new uint[256];
			for (uint i = 0; i < 256; i++)
			{
				uint r = i;
				for (int k = 0; k < 8; k++)
					r = (r & 1) != 0 ? (r >> 1) ^ 0xEDB88320 : r >> 1;
				t[i] = r;
			}
			return t;
		}

		public static uint Compute(byte[] data)
		{
			uint crc = 0xFFFFFFFF;
			for (int i = 0; i < data.Length; i++)
				crc = (crc >> 8) ^ Table[(crc ^ data[i]) & 0xFF];
			return crc ^ 0xFFFFFFFF;
		}
	}
}
