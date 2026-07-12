# AIO Hybrid Clipboard

[English](README.md) | **Türkçe**

<img width="900" alt="AIO Hybrid Clipboard ekran görüntüsü" src="https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/download/v1.3.0/screenshot_v1.3.0.png" />

[![CI](https://github.com/Layellie/AIO-Hybrid-Clipboard/actions/workflows/ci.yml/badge.svg)](https://github.com/Layellie/AIO-Hybrid-Clipboard/actions/workflows/ci.yml)
[![İndirme](https://img.shields.io/github/downloads/Layellie/AIO-Hybrid-Clipboard/total?style=flat-square&color=success)](https://github.com/Layellie/AIO-Hybrid-Clipboard/releases)
[![Sürüm](https://img.shields.io/github/v/release/Layellie/AIO-Hybrid-Clipboard?style=flat-square&color=blueviolet)](https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/latest)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue?style=flat-square&logo=windows)
![Lisans](https://img.shields.io/badge/Lisans-MIT-green?style=flat-square)

Windows için ışık hızında, hafif, hibrit (C# WPF + C++20) bir pano yöneticisi. AIO Hybrid Clipboard kopyaladığınız metinleri ve görselleri takip eder; dahili asenkron C++ WinRT OCR motoruyla ekran görüntülerindeki metni anında çıkarır.

## Kurulum

[Releases](https://github.com/Layellie/AIO-Hybrid-Clipboard/releases/latest) sayfasından en güncel `AIO_Hybrid_Clipboard_Setup_v*.exe` dosyasını indirip çalıştırın. Kurulum:

- Kullanıcı bazlıdır — **yönetici izni / UAC gerektirmez**
- Tamamen kendi kendine yeterlidir — **.NET kurulumu gerektirmez**
- Türkçe ve İngilizce kurulum dili destekler

## Özellikler

- **Hibrit Mimari:** Arayüz C# WPF ile; ağır OCR ve piksel işleme, arayüzü hiç bekletmeyen özel bir C++20 DLL ile.
- **Akıllı OCR Motoru:** Yakalanan görsellerden Windows'un yerleşik WinRT yapay zekâ motoruyla anında metin çıkarır. Kayıtlı bir ekran görüntüsüne tıklamanız yeterli.
- **Ters OCR Araması:** Görsellerinizin *içindeki* kelimeleri arayın. Motor görsel metnini dizinler, yakalamaları anında bulursunuz.
- **Oturum Kalıcılığı:** Pano geçmişi, ekran görüntüleri ve pin durumu çıkışta kaydedilir, sonraki açılışta geri yüklenir.
- **Önemli Kayıtları Pinleyin:** Herhangi bir metne veya ekran görüntüsüne sağ tıklayarak pinleyin — pinli öğeler üstte kalır ve limit tarafından asla silinmez.
- **Hızlı Yapıştırma Kısayolları:** `ALT+1`, `ALT+2`, `ALT+3` ile son 3 metni pencereyi açmadan istediğiniz uygulamaya yapıştırın.
- **Büyük Önizleme:** Küçük resmin üzerine gelin, tam boyutlu önizlemeyi görün.
- **Sürükle & Bırak:** Görselleri galeriden Discord'a, Photoshop'a veya masaüstüne sürükleyin.
- **Düzenleme & Toplu Silme:** Düzenleme modunda çoklu seçim yapıp kayıtları ve önbellek dosyalarını tek tıkla silin.
- **Otomatik Güncelleme:** Uygulama açılışta ve ayarlar panelinden GitHub Releases'ı denetler — tek tıkla indirip kurar.
- **Sistem Tepsisi:** Arka planda minimum bellek kullanımıyla sessizce çalışır.
- **Genel Kısayollar:** Başlatıcıyı her yerden çağıran, tamamen özelleştirilebilir kısayollar.
- **Çok Dilli Arayüz:** Türkçe ve İngilizce.

## Varsayılan Kısayollar

| Eylem | Kısayol |
|---|---|
| Başlatıcıyı aç / gizle | `ALT + SPACE` |
| Ekran yakala | `WIN + SHIFT + S` (Windows yerleşik) |
| Hızlı yapıştır 1 / 2 / 3 | `ALT + 1` / `ALT + 2` / `ALT + 3` |

## Nasıl Kullanılır?

1. `ALT + SPACE` ile arayüzü açın.
2. Herhangi bir metni kopyalayın ya da görüntü yakalayın — listeler otomatik dolar.
3. Bir görsele **tek tıklayın** → OCR ile metni çıkarılır.
4. Bir kayda **sağ tıklayın** → en üste pinlenir (tekrar sağ tık → çözülür).
5. Bir görseli **sürükleyin** → PNG dosyası hedef uygulamaya aktarılır.
6. `ALT+1/2/3` ile pencereyi açmadan son kayıtları yapıştırın.
7. `ESC` ile kapatın.

## Derleme

Gereksinimler ve adımlar için [İngilizce README](README.md#how-to-build) ve [CONTRIBUTING.md](CONTRIBUTING.md) dosyalarına bakın. Özet:

```bat
:: 1. Önce C++ OCR DLL (x64 zorunlu)
msbuild "AIO_SearchEngine\AIO_SearchEngine.vcxproj" /p:Configuration=Release /p:Platform=x64

:: 2. Sonra C# WPF uygulaması
dotnet build "AIO Clipboard & Search\AIO_Hybrid_Clipboard.csproj" -p:Configuration=Release -p:Platform=x64

:: 3. Testler
dotnet test AIO_Hybrid_Clipboard.Tests\AIO_Hybrid_Clipboard.Tests.csproj
```

## SSS

**Kurulumda Windows SmartScreen uyarısı çıkıyor, neden?**
Kurulum dosyası kod imzalı değil (sertifikalar ücretsiz bir açık kaynak araç için maliyetli). Kaynak kod tamamen açık ve her sürüm bu depodan derleniyor — *Ek bilgi → Yine de çalıştır* deyin. Her sürüm sayfasındaki SHA-256 özetiyle dosyayı doğrulayabilirsiniz.

**Verilerim nerede saklanıyor?**
Her şey makinenizde kalır: geçmiş ve ekran görüntüleri, exe'nin yanındaki `AIO_Cache/` klasöründedir. Tek ağ çağrısı GitHub Releases API'sine yapılan güncelleme denetimidir.

**Kısayolum çalışmıyor.**
Muhtemelen başka bir uygulama aynı kombinasyonu önce kaydetmiş. Ayarlardan farklı bir kombinasyon seçin; hatalar `AIO_Cache/app.log` dosyasına yazılır.

## Sürüm Geçmişi

Tam geçmiş için [CHANGELOG.md](CHANGELOG.md).

## Katkıda Bulunma

Katkılar memnuniyetle karşılanır! Başlamadan önce [CONTRIBUTING.md](CONTRIBUTING.md) dosyasını okuyun.

## Lisans

[MIT](LICENSE) — © 2026 SAMET KAŞMER AKA LAYE77IE
