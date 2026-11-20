using System;
using System.Collections.Generic;
using UnityEngine;

namespace BaldiInfiniteBsoda
{
	/// <summary>
	/// Better Among Us tarzı oyun içi konsol overlay'i.
	///  - Oyunu DONDURMEZ (sadece gösterim), F10 ile aç/kapa, sürüklenir.
	///  - ModConsole.Add(...) ile tüm olaylar buraya düşer.
	/// </summary>
	internal static class ModConsole
	{
		public static bool Visible = true;

		private static readonly List<string> Lines = new List<string>();
		private static readonly object Lock = new object();
		private static Vector2 _scroll;
		private static bool _stick = true;
		private static Rect _win = new Rect(10, 10, 660, 320);
		private static bool _init;
		private static bool _scrolledThisFrame;
		private static GUIStyle _lineStyle, _headStyle;
		private static Texture2D _bg, _titleBg;

		/// <summary>Konsola satır ekle (BepInEx loguna YAZMAZ — orası ayrıdır).</summary>
		public static void Add(string msg)
		{
			if (string.IsNullOrEmpty(msg)) return;
			string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
			lock (Lock)
			{
				Lines.Add(line);
				if (Lines.Count > 500) Lines.RemoveAt(0);
			}
		}

		public static void Clear()
		{
			lock (Lock) Lines.Clear();
		}

		public static string[] Snapshot()
		{
			lock (Lock) return Lines.ToArray();
		}

		public static void Draw()
		{
			if (!Visible) return;
			Init();
			// Konsol penceresi de her zaman ekranda kalsın.
			_win.x = Mathf.Clamp(_win.x, 0, Mathf.Max(0, Screen.width - _win.width));
			_win.y = Mathf.Clamp(_win.y, 0, Mathf.Max(0, Screen.height - _win.height));
			_win = GUI.Window(0xC095, _win, DrawWindow,
				BaldiInfiniteBsodaPlugin.PluginName + " — " + L.C("CONSOLE", "KONSOL"));
		}

		private static void Init()
		{
			if (_init) return;
			_init = true;
			_bg = MakeTex(new Color(0.05f, 0.07f, 0.10f, 0.92f));
			_titleBg = MakeTex(new Color(0.10f, 0.16f, 0.30f, 0.95f));
			_lineStyle = new GUIStyle(GUI.skin.label)
			{
				fontSize = 12,
				richText = false,
				wordWrap = true,
				normal = { textColor = new Color(0.75f, 0.95f, 0.75f) },
			};
			_headStyle = new GUIStyle(GUI.skin.label)
			{
				fontSize = 12,
				fontStyle = FontStyle.Bold,
				alignment = TextAnchor.MiddleLeft,
				normal = { textColor = Color.white },
			};
		}

		private static void DrawWindow(int id)
		{
			// arkaplan
			GUI.DrawTexture(new Rect(0, 0, _win.width, _win.height), _bg);
			GUI.DrawTexture(new Rect(0, 0, _win.width, 22), _titleBg);

			GUILayout.BeginArea(new Rect(6, 24, _win.width - 12, _win.height - 30));

			GUILayout.BeginHorizontal();
			GUILayout.Label(L.C("Live log — game keeps running", "Canlı log — oyun çalışmaya devam eder"),
				_headStyle, GUILayout.Height(18));
			GUILayout.FlexibleSpace();
			_stick = GUILayout.Toggle(_stick, L.C("Follow", "Takip"), GUILayout.Width(70));
			if (GUILayout.Button(L.C("Clear", "Temizle"), GUILayout.Width(70))) Clear();
			if (GUILayout.Button("X", GUILayout.Width(24))) Visible = false;
			GUILayout.EndHorizontal();

			string[] snap = Snapshot();
			_scroll = GUILayout.BeginScrollView(_scroll);
			for (int i = 0; i < snap.Length; i++)
				GUILayout.Label(snap[i], _lineStyle);
			GUILayout.EndScrollView();

			GUILayout.BeginHorizontal();
			GUILayout.Label(L.C("[F10] hide/show this console", "[F10] bu konsolu gizle/göster"), _lineStyle);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.EndArea();

			// auto-scroll
			if (_stick && !_scrolledThisFrame && Event.current.type == EventType.Repaint)
			{
				_scroll.y = float.MaxValue;
				_scrolledThisFrame = true;
			}
			else if (Event.current.type == EventType.Repaint) _scrolledThisFrame = false;

			GUI.DragWindow(new Rect(0, 0, _win.width, 22));
		}

		private static Texture2D MakeTex(Color c)
		{
			var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
			t.SetPixel(0, 0, c);
			t.Apply();
			return t;
		}
	}
}
