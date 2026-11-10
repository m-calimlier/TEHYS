# TEHYS 0.1.6 — Tek masaüstü veri klasörü

- Mevcut kayıtları koruyan yükseltme varsayılandır. Eski Local/Roaming AppData dosyaları, PDF ve arşivler doğrulanarak masaüstündeki OzelEgitimHakedisSistemi klasörüne taşınır. Farklı içerikli çakışan eski dosyalar ayrıca saklanır.
- Kayitlar: programlara göre kayıt dosyaları, imzacı/pencere/dönem ayarları.
- Belgeler: Ciktilar ve Arsiv/Hakedisler dizinleri.
- Yedekler: tam yedekler, dönem arşivleri, kurtarma ve önceki geri yükleme kopyaları.
- Calisma: geçici PDF ve doğrulama/geri yükleme dosyaları.
- Yeni yedek ve dönem arşivleri için başka bir konuma kaydetme seçimi kaldırıldı. Dışarıdaki eski yedekler geri yükleme için seçilebilir; doğrulama dosyaları çalışma klasöründe tutulur.
- Kurulumda Temiz kurulum kutusu varsayılan boş gelir ve önceki seçim hatırlanmaz. Seçimden sonra ayrıca silme onayı gerekir; sessiz temiz kurulum yapılmaz.
- Temizleme yalnız kurulum yapan Windows hesabının bilinen OEHS veri klasörlerini, masaüstü program klasörünü ve eski sıfırlama/kurtarma kopyalarını kapsar. Başka hesaplar ve bu konumların dışına elle kopyalanmış, kaydı olmayan dosyalar silinmez.
- Klasör bağlantıları izlenmez; normal yükseltmede doğrulanamayan taşıma durdurulur. Temizlemede bağlantı kaldırılır, bağlantının dış hedefindeki dosyalar korunur.
- Eski deneme paketi üzerine normal kurulumda DenemeVerisi masaüstü yedek bölümüne alınır ve normal kurulum deneme modunda kalmaz.
- Arayüz yerleşimi ve hakediş hesaplama kuralları korunur. Normal kaldırma masaüstü kullanıcı verilerini silmez.
- CI denetimleri altı proje ve ayrılmış ana pencere dosyalarıyla uyumlu hâle getirildi; davranışlar tam test takımlarıyla denetlenir.

Bu sürüm tüm geliştirmeleri main dalında toplar. Kullanıcı verisi içermeyen Windows x64 kurulum ve taşınabilir ZIP paketleri son kararlı sürüm olarak paylaşılır.

Doğrulama: 301 çekirdek ve 374 WPF testi, toplam 675 test geçti. Klasör bağlantısı üzerinden dış hedefin korunması, çakışan kayıtların saklanması, başarısız taşımada kaynakların korunması, eski kurtarma kopyaları ve deneme paketi verilerinin taşınması test edildi. Inno Setup betiği derlendi; seçim ekranında temiz kurulumun varsayılan boş olduğu ve hazır özette veri yolunun doğru olduğu doğrulandı. Veri silen gerçek bir kurulum geliştirme bilgisayarında çalıştırılmadı.

İlk kurulumdaki imzacı adları ve görevleri boşaltıldı; eski Excel evrak aktarımındaki sabit kurum imzacıları kaldırıldı. Mevcut kullanıcının kendi kaydettiği imzacı ayarları normal güncellemede korunur.
