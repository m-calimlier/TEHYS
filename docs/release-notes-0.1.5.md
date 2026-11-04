# TEHYS 0.1.5

## Kullanıcıya görünen davranış

- Gece Mavisi tasarımı tüm ana panellere ve uygulama pencerelerine uygulanır: koyu tablolar, saten mavi alanlar, buz mavisi düğmeler ve açık renkli net ödeme kartları.
- Mevcut dokuz menü, panel konumları, giriş sırası ve hesaplama kuralları korunur.
- Dönem içi değişiklik, hesap ayrıntıları, onay/uyarı ve PDF önizleme pencereleri aynı görsel dili kullanır. Resmî belge sayfaları beyaz kalır.
- Başlıkta uygulama sürümü gösterilir; TEHYS için uygulama ve kurulum simgesi eklendi.
- 4 Kasım orijinal evrak düzenlemesi ve kesilmeyen oluşturucu bilgileri bu pakete dahildir.
- Verisiz taşınabilir ZIP ve kullanıcı hesabına kurulan Windows x64 kurulum paketi hazırlanır.
- Kurulum markalı lacivert-altın karşılama görseli, Türkçe adımlar, isteğe bağlı masaüstü kısayolu, Başlat menüsü ve kaldırma desteği içerir. Kaldırma kullanıcı kayıtlarını, arşivleri ve PDF'leri silmez.

## Teknik düzenleme

- MainWindow.xaml.cs 8.167 satırdan 291 satıra indirildi. Olay işleyicileri, adları ve eski özel üye sözleşmeleri korunarak ekran/sorumluluk dosyalarına ayrıldı.
- ContractScreenModel, PaymentScreenModel ve OutputScreenModel; iş adı, hesaplama/son kayıt seçimi ve çıktı gruplama/toplam işlemlerini WPF kontrollerinden bağımsız yürütür. Bu aşama tüm uygulamanın MVVM'e dönüştürüldüğü anlamına gelmez; mevcut ekran adaptörleri korunur.
- OzelEgitimHakedis.Documents ayrı projedir: Core'a bağlıdır, masaüstü uygulamasına bağlı değildir. Dört sayfa ayrı şablon dosyalarına; onaylı sayfa ölçüleri OfficialPageLayouts'a taşındı. Eski ad alanı kaynak uyumluluğu için korunur.
- Çıktı gruplarının listelenmesinde üyeler her grup içinde hesaplanır; her kayıt için tüm geçmişin tekrar taranması azaltıldı.

## Doğrulama

- 281 çekirdek ve 368 WPF testi geçti: toplam 649 test.
- Her iki taşıma programının dört sayfalık referans çıktısı, bileşen ayrımından önceki örnekle piksel düzeyinde aynı bulundu.
- Paneller 1366×768 ve 1024×600 boyutlarında kontrol edildi; takvim/seçim kontrol parçaları, tablo seçimi kontrastı ve gerçek PDF sayfa çizimi doğrulandı.
- Performans aracı gerçek servisleri ayrı bir test klasöründe çalıştırır. Kullanıcı verileri okunmaz veya değiştirilmez.

Ölçümler bu bilgisayarda, örnek kayıtlarla, ısınmış süreçte yapıldı. Yükleme, liste oluşturma, filtreleme, rapor hesabı ve dört sayfa PDF süreleri üç çalışmanın ortancasıdır; ilk kaydetme bir kez ölçülür. Bunlar ana pencere açılışı veya etkileşimli tablonun çizilme süreleri değildir. İlk PDF satırındaki başlangıç maliyeti nedeniyle PDF süreleri kayıt sayısıyla doğrudan karşılaştırılmamalıdır.

| Kayıt | Veri boyutu | Yükleme | Liste oluşturma | Filtreleme | Dört sayfa PDF |
|---:|---:|---:|---:|---:|---:|
| 100 | 284.900 bayt | 32,255 ms | 0,153 ms | 0,047 ms | 1.285,729 ms |
| 1.000 | 2.847.156 bayt | 170,736 ms | 1,590 ms | 0,067 ms | 587,933 ms |
| 10.000 | 28.477.816 bayt | 1.359,818 ms | 4,003 ms | 0,504 ms | 197,276 ms |

## Tekrar çalıştırma

`dotnet run --project tools/Tehys.Performance -c Release -- dist/performance-yeni-kosum`

Taşınabilir paketi `scripts/Publish-Tehys.ps1`, kurulumu `scripts/Build-TehysInstaller.ps1 -CompilerPath <ISCC.exe>` üretir. Inno Setup 6.7 veya üzeri gerekir. Her komut mevcut yayın paketlerinin üzerine yazmayı reddeder.

Kurulum ve kaldırma davranışı önceki yerel pakette ayrı klasörde doğrulandı; uygulama kaldırılırken kullanıcı kontrol dosyası korundu. Bu sürümün yayımlanan paket özetleri SHA256SUMS-0.1.5.txt dosyasındadır. Dağıtım paketleri kullanıcı kayıtları, arşiv, ayar, yedek veya PDF çıktısı içermez.
