<div align="center" dir="rtl">

<img src="assets/images/app_icon.png" width="128" height="128" alt="شعار عبور" />

# برنامج عبور (Ubour)
### المنظومة المتطورة لتجاوز الحجب وحظر الإعلانات لويندوز وأندرويد

[![License: MIT](https://img.shields.io/badge/License-MIT-emerald.svg)](LICENSE)
[![Platform: Android](https://img.shields.io/badge/Platform-Android%207.0%2B-blue.svg)](#-تطبيق-الأندرويد)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6.svg)](#-تطبيق-الكمبيوتر-لويندوز)
[![Privacy: Zero-Logging](https://img.shields.io/badge/الخصوصية-محلية%20100%25%20بدون%20سجلات-success.svg)](#-الخصوصية-والأمان)
[![Engine: ByeDPI + GoodbyeDPI](https://img.shields.io/badge/المحرك-ByeDPI%20%7C%20GoodbyeDPI-orange.svg)](#-المصادر-وشكر-المطورين)

**[English (الإنجليزية)](README.md) | [العربية](README.ar.md)**

</div>

---

## 📖 نبذة عن المشروع

**عبور (Ubour)** هو منظومة مفتوحة المصدر وعالية الأداء مخصصة لنظامي **أندرويد (Android)** و **ويندوز (Windows)** لتجاوز حجب المواقع وحماية الخصوصية ومنع الإعلانات.

على عكس برامج الـ VPN التقليدية التي تعيد توجيه اتصالك بالكامل عبر خوادم وسيطة خارجية (مما يسبب بطء السرعة وتحديد سعة البيانات ومخاطر الخصوصية)، يعمل **برنامج عبور محلياً 100% على جهازك**:
- **تجاوز فحص الحزم العميق (DPI)**: تجزئة حزم TCP/TLS وتعديل ترويسات HTTP على مستوى النظام لتجاوز أنظمة الرقابة والحجب لدى مزودي خدمة الإنترنت دون الحاجة لخوادم وسيطة.
- **حظر الإعلانات والتتبع محلياً**: دمج كامل لقواعد **uBlock Origin** و **AdGuard** لتصفية الإعلانات ومسارات التجسس قبل خروجها من جهازك.
- **أقصى سرعة اتصال أصلية**: الاستفادة من كامل سرعة خط الإنترنت الخاص بك دون أي انخفاض في السرعة أو زيادة في زمن الاستجابة (Ping).

---

## 📸 لقطات شاشة للتطبيق

<div align="center">
<table>
  <tr>
    <td align="center" width="25%"><b>الواجهة الرئيسية (متصل)</b></td>
    <td align="center" width="25%"><b>النفق المقسم (المستثناة)</b></td>
    <td align="center" width="25%"><b>البحث في التطبيقات</b></td>
    <td align="center" width="25%"><b>الإعدادات والبطارية</b></td>
  </tr>
  <tr>
    <td><img src="assets/images/android_main.png" alt="الواجهة الرئيسية" width="100%" /></td>
    <td><img src="assets/images/android_split_tunnel.png" alt="النفق المقسم" width="100%" /></td>
    <td><img src="assets/images/android_all_apps.png" alt="البحث في التطبيقات" width="100%" /></td>
    <td><img src="assets/images/android_settings.png" alt="الإعدادات" width="100%" /></td>
  </tr>
</table>
</div>

---

## 🌟 المميزات بالتفصيل

### 📱 تطبيق الأندرويد (`apk+ublock`)
* **محرك ByeDPI المدمج**: تجزئة حزم TCP، وتقسيم SNI، وحقن الحزم الوهمية لتجاوز أكثر أنظمة الحجب تعقيداً.
* **درع منع الإعلانات الشامل**: أكثر من ١٦٠,٠٠٠ قاعدة منع نشطة مدمجة من uBlock Origin و AdGuard.
* **النفق المقسم (Split Tunneling) مع البحث والتبويبات**:
  * استثناء التطبيقات الحساسة (مثل التطبيقات البنكية وتطبيق سند) لتتصل بالإنترنت مباشرة وتتجاوز الحماية دون أن تكتشف الـ VPN.
  * تبويبان منفصلان: **جميع التطبيقات** و **المستثناة فقط** مع حقل بحث فوري يدعم اللغتين العربية والإنجليزية واسم الحزمة.
* **مؤشر استثناء البطارية الذكي**: كشف فوري لحالة استثناء النظام لضمان عدم توقف الخدمة في الخلفية.
* **٣ أوضاع تشغيل رئيسية**:
  1. *الوضع الشامل*: تجاوز الحجب + منع الإعلانات والتتبع.
  2. *منع الإعلانات فقط*: وضع خفيف وموفر للبطارية لتصفية الإعلانات.
  3. *تجاوز الحجب فقط*: لفك الحجب بأقصى سرعة بدون تصفية.
* **خوادم DNS مشفرة وسريعة**: سهولة التبديل بين AdGuard DNS و Cloudflare (1.1.1.1) و Google DNS.
* **توقيع رقمي رسمي (Release Signing)**: موقع بمفتاح تشفير رسمي دائم مع دعم شهادات V2 و V3 لقبول التحديثات التلقائية مستقبلاً بسلاسة.
* **واجهة حديثة Material 3**: دعم كامل للوضع الداكن والفاتح واللغة العربية والإنجليزية.

### 💻 تطبيق الكمبيوتر لويندوز (`vpn/Ubour`)
* **تكامل كامل مع GoodbyeDPI و WinDivert**: فك الحجب على مستوى نواة ويندوز بزمن استجابة فائق.
* **أوضاع DPI متعددة**: التبديل الفوري بين الأوضاع الافتراضية والتجزئة المتقدمة.
* **واجهة عربية أنيقة وسهلة**: تطبيق WPF خفيف يعمل في شريط المهام (System Tray).
* **فاحص التحديثات التلقائي**: التحقق من التحديثات بضغطة زر واحدة عبر GitHub.

---

## 📦 التنزيل والإصدارات الجاهزة

| المنصة | اسم الحزمة | المعمارية | الوصف |
| :--- | :--- | :--- | :--- |
| **أندرويد** | `Ubour-VPN-v1.0.0-Release.apk` | `arm64-v8a`, `armeabi-v7a`, `x86_64`, `x86` | حزمة التثبيت الرسمية الموقعة |
| **ويندوز 64 بت** | `Ubour-windows-x64.zip` | `x64` | حزمة جاهزة للتشغيل (Win 10/11) |
| **ويندوز 32 بت** | `Ubour-windows-x86.zip` | `x86` | حزمة جاهزة للتشغيل (Win 7/8/10/11) |

---

## 🛠️ البناء من المصدر (Build)

### تطبيق الأندرويد
```bash
git clone https://github.com/mohammedjaferalshouha/ubour-vpn.git
cd ubour-vpn/apk+ublock
./gradlew assembleRelease
```
ستجد ملف الحزمة الموقعة في المسار: `app/build/outputs/apk/release/app-release.apk`

### تطبيق الويندوز
```bash
cd ubour-vpn/vpn/Ubour
dotnet publish -c Release -r win-x64 --self-contained true
```

---

## 🔒 الخصوصية والأمان

- **بدون أي سجلات (Zero-Logging)**: لا يقوم البرنامج بجمع أو تسجيل أو نقل أي بيانات تصفح أو عناوين IP أو استعلامات DNS.
- **معالجة محلية بالكامل**: تتم جميع عمليات تعديل الحزم وتصفية الإعلانات على جهازك فقط.
- **مفتوح المصدر بالكامل**: كود برمجي شفاف وقابل للمراجعة والتدقيق.

---

## 🤝 المصادر وشكر المطورين

تم بناء عبور بالاعتماد على مشاريع مفتوحة المصدر رائدة:
- **[GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI)** للمطور ValdikSS — أداة تجاوز الـ DPI للويندوز.
- **[WinDivert](https://github.com/basil00/Divert)** للمطور basil00 — مكتبة التقاط وتحويل الحزم لويندوز.
- **[ByeDPI](https://github.com/hiddify/ByeDPI)** و **[ByeDPIAndroid](https://github.com/dovecoteescapee/ByeDPIAndroid)** للمطور dovecoteescapee — محرك ByeDPI للأندرويد.
- **[uBlock Origin](https://github.com/gorhill/uBlock)** للمطور Raymond Hill (gorhill) — قواعد منع الإعلانات ومسارات التتبع.
- **[AdGuard Filters](https://github.com/AdguardTeam/AdguardFilters)** — القوائم المحدثة لحظر الإعلانات والحماية من البرمجيات الضارة.

---

## 📄 رخصة الاستخدام

هذا المشروع مرخص بموجب **رخصة MIT**. راجع ملف [`LICENSE`](LICENSE) للمزيد من التفاصيل.
