# Baldi Infinite BSODA (BepInEx Mod)

Baldi's Basics Classic kod tabanına sahip oyunlar için **sonsuz BSODA** modu
(BepInEx 5 + Harmony). Envanterdeki slotlar kalıcı olarak **ID 4 = BSODA**'ya
kilitlenir; BSODA'yı kullandığınızda slot anında yeniden dolar.

- **Plugin GUID:** `com.mod.baldi.infinitebsoda`
- **Framework:** BepInEx 5 (Unity Mono, net472) + HarmonyX
- **Test edildiği oyun:** "TAMAM ULAN ASKER" (Baldi's Basics Classic kod tabanı, Unity 2019.4)

## Mod ne yapıyor?

`GameControllerScript`'i sahnede bulur ve her karede envanteri denetler:

```csharp
gameController.item[i] = 4;                                   // slotu BSODA yap
gameController.itemSlot[i].texture = gameController.useableItems[4]; // HUD ikonunu tazele
```

- `useableItems` / `itemTextures` alan adı oyun sürümüne göre otomatik bulunur.
- **F6** ile mod aç/kapat.

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

Visual Studio ile: `InfiniteBsoda.sln` açıp **Release → Build** (oyun yolu
`InfiniteBsoda\InfiniteBsoda.csproj` içindeki `BaldiDir` alanından okunur).

### Oyun yolu / *_Data tespiti

`*_Data` klasörü (`TAMAM ULAN ASKER_Data`, `BALDI_Data`, ...) **otomatik** bulunur.
Sadece oyunun kök klasörünü bilmeniz yeterlidir:

```powershell
dotnet build InfiniteBsoda\InfiniteBsoda.csproj -p:BaldiDir="D:\Games\BenimBaldi"
```

## Ayarlar

`<Oyun>\BepInEx\config\com.mod.baldi.infinitebsoda.cfg`

| Ayar | Varsayılan | Açıklama |
|---|---|---|
| `Enabled` | `true` | Modu aç/kapat |
| `ToggleKey` | `F6` | Oyun içi aç/kapama tuşu |

## Dosyalar

```
baldinfbsoda\
├─ InfiniteBsoda.sln              Visual Studio çözümü
├─ build.ps1                      Derle + BepInEx\plugins'e kopyala
├─ README.md
└─ InfiniteBsoda\
   ├─ InfiniteBsoda.csproj        Proje (BaldiDir verilir, *_Data otomatik)
   └─ InfiniteBsodaPlugin.cs      Mod kodu
```

## Notlar

- Derleme oyunun `*_Data\Managed\Assembly-CSharp.dll` + `BepInEx\core` DLL'lerine
  referans verir; oyun kodu değişirse alan adlarını kontrol edin.
- Classic Remastered / Plus farklı sınıf yapısı kullanır, bu mod doğrudan
  `GameControllerScript` hedefler.
