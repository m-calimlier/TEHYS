# TEHYS 0.1.4 — Kararlı sürüm

Windows 64-bit için kullanıcı kayıtlarını içermeyen taşınabilir paket. .NET ve Excel kurulumu gerekmez.

- Hizmet İşleri Hakediş Raporu'ndaki No.su alanı hakediş numarasını gösterir. Bu belge ve Kabul Tutanağı üzerindeki ayrı İKN açıklamaları kaldırılmıştır.
- Uzun hat adları ve kabul açıklamaları PDF yazı ölçülerine göre sığdırılır. Dikey kesintiler başlığı ortalanmış, yüklenici adları sola yaslanıp satırlara ayrılmış, dış çerçeveler kapatılıp kalınlaştırılmıştır.
- Haziran kabul tutanağı sözleşmedeki bitiş tarihini gün adıyla gösterir ve istenen süre uzatımı açıklamasını yazar. Diğer ayların davranışı korunmuştur.
- İcmal sayfa numarası birleşik belgede 4, tek başına belgede 1 olur. Alt sözleşme tabloları örneğe uygun düzenlenmiş; tutarlar ve imzalar korunmuştur.
- Kayıtlar ve arşivlerde aynı hesaplamada seçilip hesaplanan hatlar, toplam tutarlarıyla tek satırda gösterilir. Hat ayrıntıları ve arşiv revizyonları korunur; bireysel işlemde hat seçilebilir.
- Çıktılar ekranı yükleniciye göre çalışır. Brüt ve net bilgiler birleşik hakedişi gösterir.
- Hibrit tabloda kayıtlı Hat No ve Hattın Adı ayrıdır. Hakediş Gün Sayısı başlığı kısaltılmıştır. Asgari ücret desteği onay kutusu olmadan elle girilir.
- Sol alt köşeye “Created by Aşkın KESKİNBALTA & Murat ÇALIMLIER” eklenmiştir.

## Kurulum ve veriler

`TEHYS-0.1.4-win-x64.zip` dosyasının tamamını bir klasöre çıkarıp `TEHYS.exe` dosyasını açın. Source code arşivleri çalıştırılabilir program değildir.

Paket yalnızca program, kullanım notu ve lisansları içerir; ihale, yüklenici, hat, hakediş, arşiv, PDF, yedek ve kullanıcı ayarları dahil değildir. Bilgisayardaki mevcut kayıtlar silinmez. Güncellemeden önce uygulamanın yedekleme işlevini kullanın ve resmî belge imzacılarını kurumunuza göre kontrol edin.

Doğrulama: 630 test başarılı; her iki taşıma türünün dört sayfalı PDF'leri 18 uzun hat adıyla, kayıt/arşiv/çıktı ekranları ve sol alt imza görsel olarak kontrol edilmiştir. ZIP için `SHA256SUMS-0.1.4.txt` yayımlanır.
