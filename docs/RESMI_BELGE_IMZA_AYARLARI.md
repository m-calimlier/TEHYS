# Resmî belge imza ayarları

TEHYS'nin dört resmî PDF şablonunda kullanılan düzenleyen, kontrol eden ve onaylayan adları ve unvanları JSON ayar dosyasından okunur.

## Ayar dosyasının konumu

Uygulamada **İhale Bilgileri** ile **Yüklenici Firmalar** arasındaki **Resmî Belge İmzacıları** düğmesini açın. Düzenleyen, kontrol eden ve onaylayan adlarını/unvanlarını bu ayrı panelden düzenleyip kaydedebilirsiniz. Bu alanlar artık Ayarlar panelinde bulunmaz.

JSON dosyasını doğrudan düzenlemek de mümkündür:

Uygulama ilk resmî belgeyi oluştururken dosya yoksa varsayılan değerlerle otomatik oluşturur:

```text
Masaüstü\TEHYS\Kayitlar\official-document-settings.json
```

Windows Gezgini'nde Masaüstü'nü açın ve belirtilen klasöre gidin. Dosyayı Not Defteri ile açıp JSON değerlerini değiştirin. Değişiklikler bir sonraki belge oluşturma veya önizleme isteğinde okunur.

## Alanlar

- `preparerName`: Düzenleyen adı
- `preparerRole`: Düzenleyen unvanı
- `controllerName`: Kontrol eden adı
- `controllerRole`: Kontrol eden unvanı
- `approverName`: Onaylayan adı
- `approverRole`: Onaylayan unvanı

Örnek dosya: [official-document-settings.example.json](official-document-settings.example.json)

## Güvenli kullanım

Dosyada geçerli JSON kullanılmalıdır. Doldurulmamış imzacı adları ve unvanları boş başlar. JSON biçimi bozuksa TEHYS hata bildirir.

Bu ayar yalnızca imza isimlerini ve unvanlarını değiştirir; hakediş hesaplarını, kesintileri ya da hukuki belge metinlerinin mevzuata uygunluğunu doğrulamaz. Resmî belgeleri kullanımdan önce kurumunuzun onayladığı örneklerle karşılaştırın.
