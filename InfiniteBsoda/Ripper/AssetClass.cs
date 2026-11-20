using System;
using System.Collections.Generic;

namespace BaldiInfiniteBsoda.Ripper
{
	/// <summary>Unity classID -> tanınan kategori adı (AssetStudio ClassIDType değerleri).</summary>
	internal static class ClassId
	{
		public static string Name(int id)
		{
			switch (id)
			{
				case 1: return "GameObject";
				case 2: return "Component";
				case 4: return "Transform";
				case 8: return "Behaviour";
				case 9: return "GameManager";
				case 11: return "AudioManager";
				case 20: return "Camera";
				case 21: return "Material";
				case 23: return "MeshRenderer";
				case 28: return "Texture2D";
				case 33: return "MeshFilter";
				case 43: return "Mesh";
				case 48: return "Shader";
				case 49: return "TextAsset";
				case 50: return "Rigidbody";
				case 54: return "Collider";
				case 60: return "Animator";
				case 64: return "AnimatorController";
				case 74: return "AnimationClip";
				case 81: return "AudioListener";
				case 82: return "AudioSource";
				case 83: return "AudioClip";
				case 89: return "Cubemap";
				case 90: return "Avatar";
				case 95: return "AnimatorOverrideController";
				case 96: return "BillboardAsset";
				case 104: return "RenderSettings";
				case 108: return "Light";
				case 111: return "Animation";
				case 114: return "MonoBehaviour";
				case 115: return "MonoScript";
				case 117: return "Font";
				case 120: return "LineRenderer";
				case 128: return "TextAsset2";      // (rezerv)
				case 129: return "Rigidbody2D";
				case 130: return "Collider2D";
				case 135: return "SpriteRenderer";
				case 142: return "AssetBundle";
				case 150: return "SpriteAtlas";
				case 187: return "CanvasRenderer";
				case 198: return "ParticleSystem";
				case 199: return "ParticleSystemRenderer";
				case 212: return "Sprite";
				case 213: return "Sprite2";
				case 222: return "Canvas";
				case 224: return "RectTransform";
				case 226: return "CanvasGroup";
				case 228: return "AssetBundleManifest";
				case 240: return "VideoClip";
				case 328: return "SceneManager";     // (Unity2019 Unit)
				case 850480614: return "SpriteAtlas2";
				default: return "Class_" + id;
			}
		}

		// ---------------------------------------------------------- kategoriler --
		// Kategori indeksleri (GUI filtresi için):
		// 0=Audio 1=Texture 2=Text 3=Animation 4=Model 5=Script 6=Other
		public const int CatAudio = 0, CatTexture = 1, CatText = 2, CatAnimation = 3,
			CatModel = 4, CatScript = 5, CatOther = 6;

		public const int CategoryCount = 7;

		/// <summary>classID -> kategori indeksi (varlık filtreleme/gruplandırma için).</summary>
		public static int Category(int id)
		{
			switch (id)
			{
				case 83: case 82: case 81: case 11:   // AudioClip, AudioSource, AudioListener, AudioManager
					return CatAudio;
				case 28: case 89: case 96: case 212: case 150:  // Texture2D, Cubemap, Billboard, Sprite, SpriteAtlas
					return CatTexture;
				case 49: case 117:                    // TextAsset, Font
					return CatText;
				case 74: case 95: case 64: case 111: case 90:   // AnimationClip, AnimatorOverride, AnimatorController, Animation, Avatar
					return CatAnimation;
				case 43: case 23: case 33:            // Mesh, MeshRenderer, MeshFilter
					return CatModel;
				case 114: case 115:                   // MonoBehaviour, MonoScript
					return CatScript;
				default:
					return CatOther;
			}
		}

		// GUI'de/ticket'ta çok gürültü çıkarmayan, alt klasöre ayıklanabilir sınıflar.
		public static readonly HashSet<int> Exportable = new HashSet<int>
		{
			28,   // Texture2D
			83,   // AudioClip
			49,   // TextAsset
			115,  // MonoScript
			43,   // Mesh
			74,   // AnimationClip
			48,   // Shader
			21,   // Material
			212,  // Sprite
			117,  // Font
			142,  // AssetBundle
			240,  // VideoClip
		};
	}

	internal sealed class Texture2DData
	{
		public string Name;
		public int Width, Height, Format, MipCount, CompleteSize;
		public bool Streaming;
		public string StreamPath;       // örn. sharedassets2.assets.resS (Data dizinine göreli)
		public long StreamOffset;
		public long StreamSize;
		public int InlineSize;
		public int InlinePos;           // nesne içinde (Data içinde mutlak konum)
	}

	internal sealed class AudioClipData
	{
		public string Name;
		public int Channels, Frequency, BitsPerSample;
		public float Length;
		public int CompressionFormat;
		public string Source;           // örn. sharedassets2.resource
		public long Offset, Size;
	}

	internal sealed class TextAssetData
	{
		public string Name;
		public byte[] Bytes;
	}

	/// <summary>
	/// Sınıf alan düzenleri (Unity 2019.4 / v21, type tree yok):
	///   Texture2D : m_Name, (4,0) sabit pair, w,h,completeSize,format,mipCount,
	///               3 bool + align, streamPriority,imageCount,dimension,
	///               GLTextureSettings(6 alan), lightmapFormat,colorSpace,
	///               imageDataSize [==0 -> StreamingInfo(offset,size,path)]
	///   AudioClip : m_Name, loadType,channels,freq,bits,len, [bool+align], subsound,
	///               3 bool + align, source, offset i64, size i64, compressionFormat
	///   TextAsset : m_Name, byte[] (i32 + data, align YOK)
	///   MonoBehaviour : PPtr(12) + bool + align + PPtr(12) + m_Name
	/// </summary>
	internal static class AssetClass
	{
		/// <summary>Çoğu nesne için isim: sınıf bazlı ofset denemeleriyle.</summary>
		public static string TryReadName(SerializedFile sf, SerializedFile.ObjectInfo info)
		{
			try
			{
				if (info.ClassId == 114) // MonoBehaviour: 2 PPtr + enabled bool öncesi
				{
					var r0 = sf.ReaderAt(info);
					r0.ReadInt32(); r0.ReadInt64();       // m_GameObject PPtr
					r0.ReadBoolean(); r0.Align();
					r0.ReadInt32(); r0.ReadInt64();       // m_Script PPtr
					return Sanitize(r0.ReadAlignedString());
				}
				var r = sf.ReaderAt(info);
				return Sanitize(r.ReadAlignedString());
			}
			catch
			{
				return null;
			}
		}

		public static string Sanitize(string s)
		{
			if (string.IsNullOrEmpty(s)) return "isimsiz";
			var chars = s.ToCharArray();
			bool changed = false;
			for (int i = 0; i < chars.Length; i++)
			{
				char c = chars[i];
				if (c < 32 || c == '"' || c == '*' || c == '?' || c == '<' || c == '>' || c == '|' || c == ':' || c == '\\')
				{
					chars[i] = '_';
					changed = true;
				}
			}
			s = changed ? new string(chars) : s;
			return s.Length == 0 ? "isimsiz" : s;
		}

		public static Texture2DData ReadTexture2D(SerializedFile sf, SerializedFile.ObjectInfo info)
		{
			var r = sf.ReaderAt(info);
			var d = new Texture2DData();
			d.Name = Sanitize(r.ReadAlignedString());
			r.ReadInt32(); r.ReadInt32();                 // sabit pair (4, 0)
			d.Width = r.ReadInt32();
			d.Height = r.ReadInt32();
			d.CompleteSize = r.ReadInt32();
			d.Format = r.ReadInt32();
			d.MipCount = r.ReadInt32();
			r.ReadBoolean(); r.ReadBoolean(); r.ReadBoolean();
			r.Align();
			r.ReadInt32();                                // streamPriority
			r.ReadInt32();                                // imageCount
			r.ReadInt32();                                // dimension
			r.ReadInt32(); r.ReadInt32(); r.ReadSingle(); // filter, aniso, mipBias
			r.ReadInt32(); r.ReadInt32(); r.ReadInt32();  // wrapU/V/W
			r.ReadInt32(); r.ReadInt32();                 // lightmapFormat, colorSpace
			int imageDataSize = r.ReadInt32();
			if (imageDataSize == 0)
			{
				d.Streaming = true;
				d.StreamOffset = r.ReadUInt32();
				d.StreamSize = r.ReadUInt32();
				d.StreamPath = r.ReadAlignedString();
			}
			else
			{
				d.Streaming = false;
				d.InlineSize = imageDataSize;
				d.InlinePos = (int)(info.Start + (uint)r.Pos);
			}
			return d;
		}

		public static AudioClipData ReadAudioClip(SerializedFile sf, SerializedFile.ObjectInfo info)
		{
			var r = sf.ReaderAt(info);
			var d = new AudioClipData();
			d.Name = Sanitize(r.ReadAlignedString());
			r.ReadInt32();                       // loadType
			d.Channels = r.ReadInt32();
			d.Frequency = r.ReadInt32();
			d.BitsPerSample = r.ReadInt32();
			d.Length = r.ReadSingle();
			r.ReadBoolean(); r.Align();          // isTrackerFormat
			r.ReadInt32();                       // subsoundIndex
			r.ReadBoolean(); r.ReadBoolean(); r.ReadBoolean(); r.Align(); // preload/loadBg/legacy3D
			d.Source = r.ReadAlignedString();
			d.Offset = r.ReadInt64();
			d.Size = r.ReadInt64();
			d.CompressionFormat = r.ReadInt32(); // 1 = Vorbis (FSB5)
			return d;
		}

		public static TextAssetData ReadTextAsset(SerializedFile sf, SerializedFile.ObjectInfo info)
		{
			var r = sf.ReaderAt(info);
			var d = new TextAssetData();
			d.Name = Sanitize(r.ReadAlignedString());
			int n = r.ReadInt32();
			if (n < 0 || r.Pos + n > r.Length) n = 0;
			d.Bytes = r.ReadBytes(n);
			return d;
		}
	}
}
