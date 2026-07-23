# QZ Tray imzalama


| Ne | Nerede |
|---|---|
| `digital-certificate.txt` (public) | `appsettings` → `Printer:QzCertificatePem` **veya** bu klasör |
| `private-key.pem` (GİZLİ) | **Yalnızca sunucu** → `QzSigning/private-key.pem` |
| İmza API | `GET /api/qz/sign?request=...` (giriş yapmış kullanıcı) |

## 1) Anahtar üret (her operatör PC veya merkezi)

QZ Tray → Advanced → Site Manager → **+** → Create New

Masaüstünde `QZ Tray Demo Cert` oluşur.

## 2) Sunucuya koy

```
QzSigning/private-key.pem          ← sunucuya kopyala (wwwroot DEĞİL)
```

## 3) Sertifikayı koda göm (appsettings.Production.json)

`digital-certificate.txt` içeriğini tek satır `\n` ile:

```json
"Printer": {
  "UseQzTray": true,
  "QzCertificatePem": "-----BEGIN CERTIFICATE-----\nMIIC...\n-----END CERTIFICATE-----",
  "QzPrivateKeyPath": "QzSigning/private-key.pem"
}
```

Alternatif: `digital-certificate.txt` dosyasını bu klasöre koy (publish ile gider).

## 4) Operatör PC

- QZ Tray kurulu ve çalışır
- Site Manager ile oluşturduysanız o PC'de **override.crt** otomatik kurulur
- İlk Allow'da **Remember this decision**

> Tüm PC'lerde sessiz basım için aynı sertifika + her PC'de QZ'ye güven (override.crt veya şirket sertifikası) gerekir.

## Test

Giriş yaptıktan sonra tarayıcıda:

```
/api/qz/certificate   → PEM metni
/api/qz/sign?request=test  → base64 imza (401 olmamalı)
```
