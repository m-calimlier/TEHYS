# TEHYS 0.1.3 — Kararlı sürüm

Windows 64-bit için taşınabilir, .NET bileşenlerini içeren program paketi.

- Aynı hesaplamada seçilen yüklenici hatları, tutarlarıyla birlikte tek dört sayfalık PDF olarak hazırlanır. Her hat için ayrı belge takımı oluşturulmaz.
- Birleşik hat adları dört belgede de “Hattı Taşıma İşi” son ekiyle gösterilir. Hakediş Raporu'ndaki Hat No hücresi kaldırılmıştır.
- Dönem içi değişiklik penceresinde gün ve günlük fiyat bölümleri düzenlenebilir. Özel eğitimde taşıma ve rehber personel fiyat farkları ayrı girilir.
- Gün sayıları hakediş girişinden alınır. Yeni aya yalnız son günlük fiyatlar önerilir; önceki ayın günleri ve fiyat farkları taşınmaz.
- İKN listeleri sadeleştirilmiş, taşıma türü adları güncellenmiştir. Excel çıktısına dönem içi hesap ayrıntıları eklenmiştir.

## İndirme ve çalıştırma

`TEHYS-0.1.3-win-x64.zip` dosyasının tamamını bir klasöre çıkarıp `TEHYS.exe` dosyasını açın. Source code arşivleri çalıştırılabilir program değildir.

Paket yalnızca programı, kullanım notunu ve lisansları içerir. Kullanıcıların ihale, yüklenici, hat, hakediş, PDF, arşiv, yedek ve ayar dosyaları pakete eklenmemiştir. Mevcut bilgisayardaki kayıtlar silinmez; güncellemeden önce uygulamanın yedekleme işlevini kullanın. İlk kullanımda resmî belge imzacılarını kurumunuza göre kontrol edin.

Doğrulama: 623 test başarılı; her iki taşıma türünün dört sayfası on hatlık örnekle görsel olarak kontrol edilmiştir. ZIP bütünlüğü `SHA256SUMS-0.1.3.txt` ile doğrulanabilir.
