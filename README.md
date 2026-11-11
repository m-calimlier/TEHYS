# Taşımalı Eğitim Hakediş Yönetim Sistemi (TEHYS)

TEHYS, Temel Eğitim ve Özel Eğitim taşıma hakedişleri için Windows uygulamasıdır.

| Konum | Ad veya yol |
| --- | --- |
| GitHub deposu | `mcalimlier/TEHYS` |
| GitHub Desktop klonu | Varsayılan konumda `Belgeler/GitHub/TEHYS` |
| Visual Studio çözümü | `TEHYS.sln` |
| Kullanıcı verisi | `Masaüstü/TEHYS` |
| Windows kurulum konumu | `%LOCALAPPDATA%/Programs/TEHYS` |
| Windows kurulum dosyası | `TEHYS-0.1.7-Setup-win-x64.exe` |

`OEHS` ve `OzelEgitimHakedisSistemi` adları eski kayıtların taşınması ve mevcut araçlarla uyumluluk için kaynakta kullanılır.

**Windows kurulumu.** En güncel sürümün Assets bölümünden `TEHYS-0.1.7-Setup-win-x64.exe` dosyasını indirin ve sihirbazı tamamlayın. Ardından programı Başlat menüsündeki **TEHYS** kısayolundan açın. [En güncel GitHub sürümü](https://github.com/m-calimlier/TEHYS/releases/latest)
Excel veya ayrıca .NET kurulumu gerekmez. İlk kullanımda resmî belge imzacılarını kendi kurumunuza göre düzenleyin.
Hakediş Oluştur içindeki Excel aktarımı, görünen girdi sütunlarının sağına her hattın hesaplanan KDV, tevkifat, damga vergisi, ek kesin teminat, diğer kesintiler, brüt ve net tutarlarını sayısal hücrelerle ekler.

Kurulum dosyasını yeniden üretmek için önce `scripts/Publish-Tehys.ps1`, ardından Inno Setup derleyicisinin yoluyla `scripts/Build-TehysInstaller.ps1 -CompilerPath <ISCC.exe>` çalıştırılır.

## Teknoloji ve çözüm yapısı

- C# / .NET 8
- Windows WPF masaüstü uygulaması
- `src/OzelEgitimHakedis.Core`: iş modelleri, hesaplama ve veri servisleri
- `src/OzelEgitimHakedis.Wpf`: kullanıcı arayüzü, raporlama ve dışa aktarma
- `tests/OzelEgitimHakedis.Tests`: xUnit otomatik testleri

Çözüm dosyası: `TEHYS.sln`.

## Mevcut kapsam

- İhale/sözleşme, hat ve yüklenici bilgileri
- Hakediş hesaplama, geçmiş kayıtları ve icmal/rapor ekranları
- Arşivleme, arşiv kayıtlarını görüntüleme, geri yükleme ve düzeltme iş akışları
- PDF ve Excel çıktı akışları
- Dört hakediş belgesi için ortak önizleme ve PDF çıktısı
- Resmî belge imza adları ve unvanları için kullanıcı tarafından düzenlenebilir JSON ayarı (`Masaüstü/TEHYS/Kayitlar/official-document-settings.json`)
- WPF `DocumentViewer` tabanlı belge önizlemesi ve doğrudan Windows yazdırma iletişim kutusu
- xUnit testleri ve Windows üzerinde derleme/yayın akışı

## Pratik Ana Ekran ve yedekleme

Aylık iş takibi, seçili hakedişte PDF öncesi kontrol, düzeltme karşılaştırması, geçmiş/arşiv filtreleri ve tam yedekleme için [kullanım notuna](docs/PRATIK_ANA_EKRAN_VE_YEDEKLEME.md) bakın. Ayrı resmî sözleşme numarası girişi gerekmez; İKN kullanılır.

## Excel kaynak analizi ve hakediş iş akışı

Orijinal XLSM'nin sekiz sayfası, VBA makroları ve Hesaplamayı Kontrol Et denetimi için [kaynak inceleme ve tek diyagramlı iş akışı](docs/XLSM_KAYNAK_INCELEMESI_VE_HAKEDIS_AKISI.md) eklendi. Bu doküman, her bir PDF alanının hangi Excel sayfası/hücresinden geldiğini, aylık K+T aktarımını ve E = C − D doğrulamasını açıklar.

## Son PDF görsel kontrolü

Windows'ta dört örnek A4 PDF çıktısını yerel ortamda oluşturmak için depo kökünde PowerShell açıp ` .\scripts\Generate-OfficialPdfSmoke.ps1` komutunu çalıştırın. Kılavuz: [PDF görsel inceleme adımları](docs/UI_UX_TASMA_VE_DPI_DUZELTME_KILAVUZU.md#10-son-gorsel-kontrol-icin-yerel-pdf-uretimi).

## Arayüz düzeni ve DPI

WPF ekranlarında metin kesilmesi, düğme taşması ve yüksek DPI ölçeklendirmesi için [UI/UX taşma ve DPI düzeltme kılavuzu](docs/UI_UX_TASMA_VE_DPI_DUZELTME_KILAVUZU.md) hazırlanmıştır. Kılavuzda mevcut riskler, önerilen XAML örüntüleri ve Windows üzerinde uygulanacak görsel kabul testleri açıklanır.

## Resmî belge ayarları

Dört resmî belgedeki düzenleyen, kontrol eden ve onaylayan ad/unvan alanları ayar dosyasından değiştirilebilir. Ayrıntılar için [docs/RESMI_BELGE_IMZA_AYARLARI.md](docs/RESMI_BELGE_IMZA_AYARLARI.md) ve örnek JSON [docs/official-document-settings.example.json](docs/official-document-settings.example.json) dosyalarına bakın.

## Geliştirme ortamı

Windows ve .NET 8 SDK gereklidir. Depo kökünde PowerShell veya Terminal açarak:

```powershell
dotnet --version
dotnet restore .\TEHYS.sln
dotnet test .\TEHYS.sln --configuration Release
```

WPF uygulamasını yerel olarak derlemek için:

```powershell
dotnet build .\TEHYS.sln --configuration Release
```


## Dikkat edilmesi gereken kalite adımı

Git'e gönderilen kodun Windows üzerinde gerçekten derlenmesi ve testlerden geçmesi ayrıca doğrulanmalıdır. Önizleme/yazdırma akışı güncellenmiştir; PDF çıktısının vektör tabanlı motora geçirilmesi ve gerçek sayfa düzeninin (Türkçe karakterler, tablolar, uzun tutarlar ve çok sayfalı belgeler) Windows üzerinde gözle kontrol edilmesi ayrı doğrulama konularıdır.

Gerçek kurum verilerini, kişisel bilgileri veya üretim kayıtlarını test paketi içine koymayın.

## 0.1.7: masaüstünde tek veri klasörü

Tüm kayıtlar ve ayarlar `Masaüstü/TEHYS/Kayitlar`, PDF ve arşivler `Belgeler`, yedekler `Yedekler` altında tutulur. Normal kurulum eski dosyaları doğrulayarak taşır. Mevcut kayıtlarla aynı adı taşıyan farklı içerikler yedeklenir. İsteğe bağlı **Temiz kurulum** yalnızca kurulumu yapan Windows hesabının bilinen program klasörlerini kalıcı temizler. Kutu varsayılan olarak boştur ve ayrıca onay gerektirir.

0.1.7 kurulum dosyası kullanıcı verisi içermez. İlk kurulumda imzacı adları ve görevleri boştur.
