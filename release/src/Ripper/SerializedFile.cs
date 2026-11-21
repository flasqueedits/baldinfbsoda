using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BaldiInfiniteBsoda.Ripper
{
	/// <summary>
	/// Unity serialized file parser (format v21 = StoresTypeDependencies, Unity 2019.3/2019.4).
	/// Type tree YOK (enableTypeTree=0) -> sınıf alan düzenleri sabit (bkz. AssetStudio).
	/// Header big-endian, metadata little-endian (endian byte header offset 16).
	/// </summary>
	internal sealed class SerializedFile
	{
		public string FilePath;
		public string FileName;
		public byte[] Data;
		public uint Version;         // 21
		public uint DataOffset;
		public string UnityVersion;
		public List<int> ClassIds = new List<int>();
		public List<ObjectInfo> Objects = new List<ObjectInfo>();

		public struct ObjectInfo
		{
			public long PathId;
			public uint Start;        // dataOffset dahil
			public uint Size;
			public int ClassId;
		}

		public static SerializedFile Load(string path)
		{
			var sf = new SerializedFile();
			sf.FilePath = path;
			sf.FileName = Path.GetFileName(path);
			sf.Data = File.ReadAllBytes(path);
			sf.Parse();
			return sf;
		}

		private void Parse()
		{
			byte[] b = Data;
			if (b.Length < 20) throw new InvalidDataException("dosya çok kısa: " + FileName);

			uint metaSize = BE32(b, 0);
			uint fileSize = BE32(b, 4);
			Version = BE32(b, 8);
			DataOffset = BE32(b, 12);
			byte endian = b[16];
			if (Version != 21)
				throw new NotSupportedException($"sürüm {Version} desteklenmiyor (v21 bekleniyor): {FileName}");

			var r = new BinReader(b, 20, endian == 0);
			UnityVersion = r.ReadCString();
			r.ReadInt32();            // platform (BuildTarget)
			bool typeTree = r.ReadByte() != 0;

			int typeCount = r.ReadInt32();
			for (int i = 0; i < typeCount; i++)
			{
				int classId = r.ReadInt32();
				r.ReadByte();                  // stripped
				if (Version >= 17) r.ReadInt16(); // scriptTypeIndex
				if (Version >= 13)
				{
					if (classId == 114 || (Version < 16 && classId < 0))
						r.ReadBytes(16);       // scriptID
					r.ReadBytes(16);           // oldTypeHash
				}
				if (typeTree)
					throw new NotSupportedException("type tree desteklenmiyor: " + FileName);
				ClassIds.Add(classId);
			}

			int objCount = r.ReadInt32();
			for (int i = 0; i < objCount; i++)
			{
				r.Align();
				var info = new ObjectInfo();
				info.PathId = r.ReadInt64();
				info.Start = r.ReadUInt32() + DataOffset;
				info.Size = r.ReadUInt32();
				int typeId = r.ReadInt32();   // m_Types index (v15+)
				if (typeId >= 0 && typeId < ClassIds.Count) info.ClassId = ClassIds[typeId];
				else info.ClassId = typeId;   // fallback: ham classID
				if (info.Start > (uint)b.Length || info.Start + info.Size > (uint)b.Length)
					continue;                 // güvenli: sınırlar dışı atla
				Objects.Add(info);
			}
		}

		private static uint BE32(byte[] b, int o)
			=> (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

		/// <summary>Bir nesne verisini okumak için little-endian BinReader konumlandırır.</summary>
		public BinReader ReaderAt(ObjectInfo info) => new BinReader(Data, (int)info.Start, true);
	}

	/// <summary>Little-endian (varsayılan) binary reader + Unity aligned string/align yardımcıları.</summary>
	internal sealed class BinReader
	{
		private readonly byte[] _b;
		public int Pos;
		public readonly bool LE;

		public BinReader(byte[] b, int pos, bool le)
		{
			_b = b; Pos = pos; LE = le;
		}

		public int Length => _b.Length;

		public byte ReadByte()
		{
			if (Pos >= _b.Length) throw new EndOfStreamException();
			return _b[Pos++];
		}

		public bool ReadBoolean() => ReadByte() != 0;

		public byte[] ReadBytes(int n)
		{
			if (Pos + n > _b.Length) throw new EndOfStreamException();
			var v = new byte[n];
			Array.Copy(_b, Pos, v, 0, n);
			Pos += n;
			return v;
		}

		public int ReadInt32()
		{
			if (Pos + 4 > _b.Length) throw new EndOfStreamException();
			int p = Pos; Pos += 4;
			return LE ? (_b[p] | (_b[p + 1] << 8) | (_b[p + 2] << 16) | (_b[p + 3] << 24))
			          : ((_b[p] << 24) | (_b[p + 1] << 16) | (_b[p + 2] << 8) | _b[p + 3]);
		}

		public uint ReadUInt32() => unchecked((uint)ReadInt32());

		public short ReadInt16()
		{
			if (Pos + 2 > _b.Length) throw new EndOfStreamException();
			int p = Pos; Pos += 2;
			return LE ? (short)(_b[p] | (_b[p + 1] << 8)) : (short)((_b[p] << 8) | _b[p + 1]);
		}

		public long ReadInt64()
		{
			if (Pos + 8 > _b.Length) throw new EndOfStreamException();
			int p = Pos; Pos += 8;
			ulong lo, hi;
			if (LE)
			{
				lo = (ulong)(_b[p] | (_b[p + 1] << 8) | (_b[p + 2] << 16) | (_b[p + 3] << 24));
				hi = (ulong)(_b[p + 4] | (_b[p + 5] << 8) | (_b[p + 6] << 16) | (_b[p + 7] << 24));
				return unchecked((long)(lo | (hi << 32)));
			}
			hi = (ulong)((_b[p] << 24) | (_b[p + 1] << 16) | (_b[p + 2] << 8) | _b[p + 3]);
			lo = (ulong)((_b[p + 4] << 24) | (_b[p + 5] << 16) | (_b[p + 6] << 8) | _b[p + 7]);
			return unchecked((long)((hi << 32) | lo));
		}

		public float ReadSingle()
		{
			int i = ReadInt32();
			byte[] tmp = BitConverter.GetBytes(i);
			return BitConverter.ToSingle(LE ? tmp : Reverse(tmp), 0);
		}

		private static byte[] Reverse(byte[] x)
		{
			Array.Reverse(x);
			return x;
		}

		/// <summary>\0-sonlu string (metadata bölümünde kullanılır).</summary>
		public string ReadCString()
		{
			int start = Pos;
			while (Pos < _b.Length && _b[Pos] != 0) Pos++;
			string s = Encoding.UTF8.GetString(_b, start, Pos - start);
			if (Pos < _b.Length) Pos++; // null terminator
			return s;
		}

		/// <summary>Unity aligned string: i32 uzunluk + byte'lar + 4'e hizala.</summary>
		public string ReadAlignedString()
		{
			int n = ReadInt32();
			if (n < 0 || Pos + n > _b.Length) throw new InvalidDataException("geçersiz string uzunluğu: " + n);
			string s = Encoding.UTF8.GetString(_b, Pos, n);
			Pos += n;
			Align();
			return s;
		}

		public void Align()
		{
			int m = Pos & 3;
			if (m != 0) Pos += 4 - m;
		}
	}
}
