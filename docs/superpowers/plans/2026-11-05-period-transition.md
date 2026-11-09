# Dönem Yönetimi Implementation Plan

**Goal:** Ayarlar altında doğrulanmış dönem arşivi oluşturup iki taşıma programını birlikte boş yeni döneme geçirmek.
**Architecture:** Mevcut OehsBackupService doğrulama ve geri alma altyapısı kullanılacak. İşlem canlı verileri doğrudan silmeyecek; eski klasörler kurtarma için korunacak. Kullanıcı çalıştırana kadar gerçek veriler sıfırlanmayacak.
**Tech Stack:** .NET 8, WPF, xUnit.

- [x] Core testleri: iki program, PDF, taslak, ayarlar, geri yükleme, geçersiz dönem, yedek hatası, ikinci kökte hata ve yeniden açılış.
- [x] OehsPeriodTransitionService: dönem doğrulama, üzerine yazılmayan arşiv, doğrulama, boş veri paketi ve mevcut transactional Restore.
- [x] Ayarlar kartı: eski/yeni dönem, ayarları koruma seçeneği, açık düzenleme engeli, kapsam/onay, yedek dosyası seçimi, sonuç.
- [x] Bellekteki koleksiyonları ve formları yeniden yükle; aktif dönemi yeniden açılışta göster.
- [x] Testler ve yerel derleme; mevcut verilerin bütünlüğünü koruyarak kurulu EXE'yi güncelle. GitHub'a gönderme.

İmzacı/pencere ayarları varsayılan olarak korunur; seçenek kapalıysa uygulama varsayılanlarına dönülür. Hesap sabitleri ve geçmişleri sıfırlanır. Dönem bilgisi yedeklenebilir JSON dosyasında tutulur. Arşiv iptali, bozuk veri veya yedek hatası canlı veriyi değiştirmez.
