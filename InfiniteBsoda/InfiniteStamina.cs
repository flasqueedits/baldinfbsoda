using UnityEngine;

namespace BaldiInfiniteBsoda
{
	/// <summary>
	/// Sonsuz stamina: stamina her karede max'a sabitlenir.
	/// PlayerScript (stamina/maxStamina) veya PlayerMovement (stamina/staminaMax)
	/// farketmez; GameAccess her iki alan adını da dener. Sürüm-bağımsız.
	/// </summary>
	internal static class InfiniteStamina
	{
		public static void Apply()
		{
			if (!GameAccess.HasPlayer) return;

			float max = GameAccess.GetFloat(GameAccess.Player, GameAccess.StaminaMaxField, 100f);
			if (max <= 0f) max = 100f;

			// Stamina'yı max'a kilitler -> koşu hiç bitmez.
			GameAccess.SetFloat(GameAccess.Player, GameAccess.StaminaField, max);

			// Stamina barı (Slider) UI'ını da tam doldur (HUD tutarlı olsun).
			object bar = GameAccess.GetStaminaBar();
			if (bar is UnityEngine.UI.Slider slider)
			{
				slider.value = 100f; // oyun içinde bar 0-100 arası gösterilir
			}
		}
	}
}
