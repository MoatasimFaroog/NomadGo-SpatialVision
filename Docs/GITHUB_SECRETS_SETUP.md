# إعداد GitHub Secrets لمشروع NomadGo SpatialVision

## الخطوات المطلوبة

اذهب إلى مستودعك على GitHub:
**Settings → Secrets and variables → Actions → New repository secret**

---

## Secrets المطلوبة

### 1. UNITY_LICENSE
- اتبع خطوات ملف `Docs/CI_CD_SETUP.md` للحصول على ملف `.ulf`
- انسخ محتوى الملف كاملاً وضعه في هذا الـ Secret

### 2. UNITY_EMAIL
- بريدك الإلكتروني المسجل في Unity

### 3. UNITY_PASSWORD
- كلمة مرور حساب Unity الخاص بك

---

## Secrets التوقيع (Release Signing) — القيم جاهزة

### 4. ANDROID_KEYSTORE_BASE64
تم إنشاء Keystore جاهز. انسخ القيمة من ملف:
`nomadgo-release.keystore.base64`

**معلومات الـ Keystore:**
- Alias: `nomadgo`
- Store Password: `NomadGo@2026!`
- Key Password: `NomadGo@2026!`
- Validity: 10,000 days (~27 years)
- Algorithm: RSA 2048-bit
- Organization: NomadGo, Riyadh, SA

### 5. ANDROID_KEYSTORE_PASS
```
NomadGo@2026!
```

### 6. ANDROID_KEYALIAS_NAME
```
nomadgo
```

### 7. ANDROID_KEYALIAS_PASS
```
NomadGo@2026!
```

---

## ملاحظات مهمة

> **احتفظ بملف `nomadgo-release.keystore` في مكان آمن!**
> إذا فقدت هذا الملف، لن تستطيع تحديث التطبيق على Google Play.

> **لا ترفع الـ Keystore أو كلمات المرور على GitHub مباشرة.**
> استخدم GitHub Secrets فقط.

---

## بعد إضافة جميع الـ Secrets

1. اذهب إلى **Actions** في مستودعك
2. اضغط على **Build Android APK (Release)**
3. اضغط **Run workflow**
4. انتظر 15-30 دقيقة
5. حمّل الـ APK من **Artifacts** أو من **Releases**
