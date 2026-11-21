namespace BaldiInfiniteBsoda
{
	/// <summary>
	/// GUI dil seçimi. Varsayılan İngilizce; menüden (Language) veya config'ten
	/// (General/Language = en|tr) değiştirilir.
	/// </summary>
	internal static class L
	{
		/// <summary>true = Türkçe, false = İngilizce.</summary>
		public static bool Turkish;

		/// <summary>en: İngilizce metin, tr: Türkçe metin. GUI'de görünen her metin buradan geçer.</summary>
		public static string C(string en, string tr) => Turkish ? tr : en;

		public static string OnOff(bool v) => v ? C("ON", "AÇIK") : C("OFF", "KAPALI");

		/// <summary>Başlangıç uyarısı: GUI açıkken hileler çalışmaz.</summary>
		public static string GuiWarning =>
			C("WARNING: IF YOU OPEN THIS GUI THE GAME WILL NOT FREEZE, BUT YOU MUST CLOSE THIS GUI FOR THE CHEATS TO WORK!",
			  "UYARI: EĞER BU GUI'Yİ AÇARSANIZ OYUN DONMAZ, AMA HİLELERİN ÇALIŞMASI İÇİN BU GUI'Yİ KAPATMAN LAZIM!");
	}
}
