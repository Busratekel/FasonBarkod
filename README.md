# Doqu Barkod Uygulaması

SAP üzerinden barkod etiketi basım uygulaması. Barkod numaraları **SAP tarafında** üretilir; SAP'ye geri yazma yok.

## Modüller

| Modül | Listele | Barkod Al | Tekrar Bas |
|-------|---------|-----------|------------|
| SAS (Satınalma Siparişi) | `ZMM_N_SAS_L` | `ZMM_N_SAS_B` | `ZMM_N_SAS_B_T` |
| Malzeme Belgesi | `ZMM_MALZEME_L` | `ZMM_MALZEME_B` | `ZMM_MALZEME_B_T` |
| Üretim Siparişi | `ZMM_URETIM_SIPARIS_L` | `ZMM_URETIM_SIPARIS_B` | `ZMM_URETIM_SIPARIS_B_T` |

## Mimari

```
[Operatör / Tedarikçi]
        │
        ▼
[FasonBarkod.Web veya dış sistem] ──HTTPS + API Key──► [FasonBarkod.Api]
                                                              │
                                                              ▼
                                                    [SapService — SAP NCo RFC]
                                                              │
                                                              ▼
                                                           [SAP]
```

**Web uygulaması SAP'ye doğrudan bağlanmaz.** Tüm SAP işlemleri API üzerinden yapılır.

## Etiket tipleri

- **Koli Üstü:** Miktar paket miktarının katı olmalı. 10 yazılırsa 1 etiket (10 adetlik koli).
- **Koli İçi:** Girilen adet kadar bağımsız etiket basılır.

## Güvenlik

- **LDAP:** `Barcode Basma` (genel), `Barcode Basma_Tekrar` (yeniden basım)
- **API Key:** Tedarikçi entegrasyonu için

## SAP bağlantı ayarları

Şifreleri repoya yazmayın. User Secrets kullanın:

```bash
dotnet user-secrets set "Sap:User" "170RFC" --project src/FasonBarkod.Api
dotnet user-secrets set "Sap:Password" "SIFRENIZ" --project src/FasonBarkod.Api
dotnet user-secrets set "Sap:AppServerHost" "172.17.103.4" --project src/FasonBarkod.Api
```

Test / Canlı ortam için `appsettings.Development.json` veya ortam değişkenleri kullanın.

## Proje yapısı

```
src/
  FasonBarkod.Api/            REST API (tedarikçi)
  FasonBarkod.Web/            MVC arayüz (operatör)
  FasonBarkod.Infrastructure/ SAP RFC, yazıcı, veritabanı
  FasonBarkod.Core/           DTO, enum, validasyon
```

## Geliştirme sırası

1. SAP NCo paketi + `SapService.cs` RFC bağlantısı
2. 3 modül ekranı (SAS, Malzeme Belgesi, Üretim Siparişi)
3. LDAP entegrasyonu
4. `.prn` şablon + SATO yazıcı raw print
5. Marka bilgisi (Dağıtım Kanalı 15, 17 + 229* → Bellona kuralı)

## Çalıştırma

Önce API, sonra Web (Web API'ye bağlanır):

```bash
dotnet run --project src/FasonBarkod.Api
dotnet run --project src/FasonBarkod.Web
```

Test SAS no (mock veri): `50001234`

## QZ Tray (yerel yazıcı)

Etiket basımı operatör PC'sindeki yazıcılara **QZ Tray** ile gider (`Printer:UseQzTray=true`).

1. Her operatör PC'sine [QZ Tray](https://qz.io/download/) kurun ve çalıştırın.
2. **Allow uyarıları için (bir kez):** QZ Tray → Advanced → Site Manager → Create New → masaüstündeki `digital-certificate.txt` + `private-key.pem` dosyalarını `src/FasonBarkod.Web/QzSigning/` klasörüne kopyalayın; Web’i yeniden başlatın. İlk Allow’da **Remember this decision** seçin.
3. Siteye giriş → **Yazıcı** menüsünden bu PC'deki yazıcıyı seçip kaydedin.
4. SAS ekranından basım: sunucu etiketi üretir, tarayıcı QZ ile yazıcıya gönderir.

Eski sunucu-spooler davranışı için: `"UseQzTray": false`.

## SAP test bağlantısı

User Secrets (API projesi — şifre repoda değil):

```bash
dotnet user-secrets set "Sap:Enabled" "true" --project src/FasonBarkod.Api
dotnet user-secrets set "Sap:User" "170RFC" --project src/FasonBarkod.Api
dotnet user-secrets set "Sap:Password" "SIFRENIZ" --project src/FasonBarkod.Api
dotnet user-secrets set "Sap:AppServerHost" "172.17.103.4" --project src/FasonBarkod.Api
dotnet user-secrets set "Sap:Client" "100" --project src/FasonBarkod.Api
```

SAP bağlantı testi (API çalışırken):

```
GET http://localhost:5135/saphealth
```

SAP erişilemezse `UseMockFallback: true` ile mock veriye düşer.

## SAS API endpoint'leri

| Method | Endpoint | Açıklama |
|--------|----------|----------|
| GET | `/api/sas/{orderNo}/lines` | SAS kalemleri (`ZMM_N_SAS_L`) |
| POST | `/api/sas/barcodes` | Barkod al (`ZMM_N_SAS_B`) |

Header: `X-Api-Key: dev-api-key-change-in-production`


**Import:** `I_WERKS`, `I_MJAHR`, `IS_MBLNR`, `IS_ZEILE`, `IS_MATNR`, `I_BUDAT_L`, `I_BUDAT_H`, `I_KUNNR`

**Export (IT_DATA / ZMM14702):** `MJAHR`, `MBLNR`, `ZEILE`, `WERKS`, `MATNR`, `MAKTX`, `MENGE`, `BASILAN_KOLI`
