# Baldi Infinite BSODA — Cheat Pack (BepInEx Mod)

Baldi's Basics Classic kod tabanına sahip oyunlar için **22 hilelik paket**
(BepInEx 5 + HarmonyX) + oyun içi konsol overlay + AssetStudio tarzı varlık
ripper'ı. **Sürüm-bağımsız**: oyun sınıfları ve alanları runtime'da reflection
ile bulunur; Assembly-CSharp'a compile-time bağımlılık yoktur.

- **Plugin GUID:** `com.mod.baldi.infinitebsoda`
- **Framework:** BepInEx 5 (Unity Mono, net472) + HarmonyX + TextMeshPro
- **Test edildiği oyun:** "TAMAM ULAN ASKER" (Baldi's Basics Classic kod tabanı, Unity 2019.4)

## Hileler ve tuşlar

| Tuş | Hile | Açıklama |
|---|---|---|
| **F6** | **Sonsuz Eşya** | Aldığın her eşya **kendi slotunda kalıcı** olur (hangi ID/slot olursa olsun); kullanınca geri gelir. **Boş slotlara hiçbir şey eklenmez** |
| **F7** | Sonsuz Stamina | Stamina hiç bitmez — koşu sonsuz (HUD barı da tam dolu kalır) |
| **F8** | Finale Mode | Tüm notebooklar toplanmış gibi yapar, oyun finale mode'a geçer |
| **F9** | **Godmode** | NPC'ler dokunamaz. Varsayılan **süresiz** (`∞`); istersen config'den süreli |
| **F5** | Hız Hilesi | Yürüme/koşu hızı çarpanı (varsayılan 3x, GUI'den 1-10 ayarlanır) |
| **F11** | Yavaş Çekim | `Time.timeScale` ile yavaş oyun (varsayılan 0.35x) |
| **F12** | Tüm Eşyalar | Envanterin tüm slotlarını seçilen eşya ID'siyle doldurur |
| **T** | NPC Dondurma | Baldi, Playtime, Principal, Bully vb. ajanlarını durdurur |
| **J** | Jumpscare Yok | Baldi'nin flaş/kamera animasyonlarını ve game-over'u engeller |
| **N** | Duvar Geçme (Noclip) | CharacterController'ı kapatır, duvarlardan geçersin |
| **G** | Işınlanma | İleri yönde 12 m ışınlanır (tekrar tekrar basılabilir) |
| **H** | Duvar Hilesi (ESP) | NPC'leri duvarların arkasından görünür yapar (ZTest Always) |
| **C** | Sakin Baldi | Baldi'nin öfkesini sıfırlar, asla kızmaz/kovalamaz |

### Ek hileler (2.4.0)

| Tuş | Hile | Açıklama |
|---|---|---|
| **V** | Yüksek Atlama | Düşük yer çekimi (`Physics.gravity`), 1-8x ayarlanabilir |
| **B** | Uçma Modu | Space ile yukarı, Ctrl ile aşağı uçar |
| **X** | Düşmanları Gizle | Tüm NPC GameObject'lerini kapatır (tekrar basınca geri gelir) |
| **M** | Notebook Otomatik Topla | Tüm notebook pickuplarını anında toplar |
| **R** | Sıfırla | Suçluluk, game-over süresi ve Baldi öfkesini sıfırlar |
| **P** | Kapılar/Çıkışlar | Tüm kapıların kilidini açar, çıkışları kaldırır |
| **K** | Çıkışa Işınlan | `entrance_0..3` konumuna ışınlanır |
| **F4** | Debug Modu | Oyunun `debugMode` alanını açar |
| **Y** | Sonsuz Mod | Baldi'nin `endless` alanını açar |

**Diğer kontroller:** `Delete` menü, `F10` konsol overlay, menüdeki **Ayarlar**
slider'ları ile hız/yavaşlık/eşya ID/FOV/ses perdesi/notebook/atlama/uçma hızı
anında değiştirilir. **Açılış splash'i kaldırıldı** — oyun doğrudan menüye açılır.

### Godmode detayı
- Süre varsayılan **0 = süresiz** (HUD'da `GODMODE ∞`).
- Süreli istersen `4_Godmode/DurationSeconds` değerini saniye olarak ayarla;
  üst-ortada oyunun kendi COMIC PRO fontuyla geri sayım görünür.
- Oyundan çıkınca (GameController yok olunca) otomatik kapanır.

### Varlık Ripper'ı (AssetStudio tarzı)
`Delete` → **Varlıklar** sekmesi → `Dosya > Oyun Data'sını Tara` → **Sınıflar**
sekmesinde tipleri seç → **EXPORT**. Çıktı masaüstünde `BaldiRip_*` klasöründe
kategori alt klasörleriyle (Audio/Texture/Text/Animation/Model/Script/Other)
yazılır: Texture2D → PNG, AudioClip → OGG, TextAsset → TXT, diğerleri → RAW/BIN.
Tüm olaylar BepInEx log'una **ve** oyun içi konsola yazılır.

## Sürüm-bağımsızlık (neden her Unity sürümünde çalışır)

Mod, `Assembly-CSharp`'a **compile-time bağlı değildir**. Oyun sınıfları ve
alanları runtime'da reflection ile, muhtemel adların listesi taranarak bulunur:

- Sınıf: `GameControllerScript` / `GameController` / `GameManager...`
- Oyuncu: `PlayerScript` / `PlayerMovement` / `PlayerFunctions`
- Stamina: `stamina` + `maxStamina` **veya** `staminaMax` (sürüme göre)
- Envanter: `item` / `items`, slot: `itemSlot`, ikon: `itemTextures` **veya** `useableItems`
- Notebooks: `notebooks` / `notebookCount`
- Godmode yamaları: `OnTriggerEnter`/`Stay`/`Exit` (private olsa bile AccessTools ile)

Böylece BB Classic'in 1.4.x farklı derlemelerinde alan adları değişse bile mod uyum sağlar.

## Gereksinimler

1. Bir **Baldi's Basics Classic** kod tabanlı oyun (ör. TAMAM ULAN ASKER klasik çıkışı).
2. [BepInEx 5 — `BepInEx_win_x64_5.4.23.6.zip`](https://github.com/BepInEx/BepInEx/releases)
   Zip'i oyunun kök klasörüne açın (`doorstop_config.ini`, `winhttp.dll`, `BepInEx\`
   oluşmalı). Oyunu bir kez başlatıp kapatın.
3. .NET SDK (`dotnet build` için) veya Visual Studio 2022.

## Derleme ve kurulum

```powershell
git clone https://github.com/flasqueedits/baldinfbsoda.git
cd baldinfbsoda
.\build.ps1                       # varsayılan oyun yolunu kullanır
# veya
.\build.ps1 -GameDir "D:\Oyunlar\BenimBaldi"
```

`build.ps1` projeyi derler ve `InfiniteBsoda.dll`'yi otomatik olarak
`<Oyun>\BepInEx\plugins\` klasörüne kopyalar.

Visual Studio ile: `InfiniteBsoda.sln` açıp **Release → Build**.

### Oyun yolu / *_Data tespiti

`*_Data` klasörü **otomatik** bulunur. Sadece oyunun kök klasörünü verin:

```powershell
dotnet build InfiniteBsoda\InfiniteBsoda.csproj -p:BaldiDir="D:\Games\BenimBaldi"
```

## Ayarlar

`<Oyun>\BepInEx\config\com.mod.baldi.infinitebsoda.cfg`

| Bölüm | Anahtar | Varsayılan | Açıklama |
|---|---|---|---|
| `1_BSDOA` | `Enabled` | `true` | Sonsuz Eşya aç/kapat (alınan eşyalar kalıcı, boş slotlar boş kalır) |
| `2_Stamina` | `StartEnabled` | `false` | Oyun başında sonsuz stamina açık mı |
| `4_Godmode` | `DurationSeconds` | `0` | Godmode süresi (saniye), **0 = süresiz** |
| `6_Extra` | `SpeedMultiplier` / `SlowMoScale` / `ItemId` / `FieldOfView` / `AudioPitch` / `Notebooks` / `JumpMultiplier` / `FlySpeed` | `3` / `0.35` / `4` / `90` / `1` / `20` / `3` / `12` | Ek hile değerleri |
| `General` | `Language` | `en` | GUI dili (`en` / `tr`) |
| `Keys` | `Bsoda` / `Stamina` / `Finale` / `Godmode` | `F6`/`F7`/`F8`/`F9` | Ana hile tuşları |
| `Keys` | `Speed` / `SlowMotion` / `AllItems` / `NpcFreeze` / `NoJumpscare` / `Noclip` / `Teleport` / `Wallhack` / `CalmBaldi` | `F5`/`F11`/`F12`/`T`/`J`/`N`/`G`/`H`/`C` | Ek hile tuşları |
| `Keys` | `HighJump` / `Fly` / `HideEnemies` / `AutoNotebooks` / `ResetGuilt` / `UnlockDoors` / `ExitTeleport` / `DebugMode` / `EndlessMode` | `V`/`B`/`X`/`M`/`R`/`P`/`K`/`F4`/`Y` | Yeni hile tuşları |
| `5_Dev` | `AutoExportOnStart` | `false` | DEV: sadece `Warning` sahnesinde açılışta otomatik scan+export |

## Dosyalar

```
baldinfbsoda\
├─ InfiniteBsoda.sln
├─ build.ps1
├─ README.md
└─ InfiniteBsoda\
   ├─ InfiniteBsoda.csproj        Proje (BaldiDir verilir, *_Data otomatik)
   ├─ InfiniteBsodaPlugin.cs      Ana plugin (config, tuşlar, splash, log)
   ├─ GameAccess.cs               Sürüm-bağımsız reflection erişim katmanı
   ├─ ExtraCheats.cs              9 ek hile (hız, slow-mo, eşya, NPC, noclip, ESP...)
   ├─ InfiniteStamina.cs          Sonsuz stamina
   ├─ FinaleModeCheat.cs          Spoop + Finale mode
   ├─ Godmode.cs                  Süresiz godmode + COMIC PRO HUD
   ├─ CheatMenu.cs                GUI (Cheats/Assets/Classes + ayar slider'ları)
   ├─ ModConsole.cs               Oyun içi konsol overlay
   ├─ Lang.cs                     EN/TR localization
   └─ Ripper\                     AssetStudio tarzı varlık ripper'ı
```

## Notlar

- Derleme oyunun `*_Data\Managed` DLL'lerine referans verir
  (UnityEngine, TextMeshPro vb.); oyun kodu değişse bile derlenir çünkü
  Assembly-CSharp'a compile-time bağlı değil.
- Classic Remastered / Plus farklı sınıf yapısı kullanır, bu mod doğrudan
  classic `GameControllerScript`/`PlayerScript` hedefler.
