# بناء وتشغيل مدار سوفت لأندرويد

الهاتف لا يحتاج إلى كمبيوتر بعد التثبيت. الأدوات التالية للمطور لبناء APK فقط:

- .NET 10 SDK وحزمة `maui-android`.
- JDK 21 كامل، وAndroid SDK Platform 36 وBuild Tools 36.
- هاتف أندرويد 8+ بمعمارية ARM64. لا توجد صلاحية INTERNET في الحزمة.

```sh
dotnet workload install maui-android --skip-manifest-update
dotnet run --project tests/Madar.OfflineChecks -c Release
dotnet publish mobile/Supermarket.Android -c Release -r android-arm64 -p:AndroidPackageFormats=apk
```

عند عدم اكتشاف الأدوات تلقائياً، مرر `-p:AndroidSdkDirectory=/path/to/android-sdk -p:JavaSdkDirectory=/path/to/jdk21`. الناتج في `mobile/Supermarket.Android/bin/Release/net10.0-android/android-arm64/publish/`؛ ثبّت ملف `*-Signed.apk`.

`Build-Android.cmd` ينفذ بناء أندرويد من ويندوز؛ لا يبني تطبيقاً لويندوز. لبناء APK المحاكي استخدم `-r android-x64 -c Debug`.

## الواجهة والبيانات

تُحزم ملفات `www` والخط العربي والشعار داخل APK. يعرضها WebView محلي ويتصل عبر جسر أندرويد بمحرك C# وSQLite. لا توجد صفحة ويب خارجية أو خادم localhost في التطبيق. خادم HTTP في اختبار الواجهة فقط لتقديم الملفات نفسها إلى متصفح الاختبار.

- `madar-production`: قاعدة العمل والمرفقات والنسخة التلقائية.
- `madar-training`: قاعدة التدريب، مع بيانات توضيحية مستقلة.
- لا تُحفظ قواعد بيانات المتجر أو رموز الدخول أو مفاتيح التوقيع في Git.
- إنهاء عملية التطبيق يتطلب تسجيل الدخول عند تشغيله من جديد؛ عند تبديل الموظف استخدم «تسجيل الخروج».

## التحقق من الواجهة

```sh
dotnet build tests/Madar.UiHost -c Release
python3 -m pip install playwright==1.62.0
python3 -m playwright install chromium
python3 tests/ui_check.py
```

يمكن تمرير `--chromium /path/to/chromium --dotnet /path/to/dotnet --output artifacts/ui`. الاختبار يستخدم محرك SQLite الحقيقي، ويستبدل وسيلة النقل الأصلية فقط. لا يثبت اختبار المتصفح سلامة جسر Android أو الكاميرا والطباعة على الجهاز.

## التوقيع والتحقق على الهاتف

بناء Release الافتراضي ينتج حزمة للاختبار بتوقيع التطوير. استخدم مفتاح إصدار خاصاً ثابتاً للنشر والتحديثات، وخزنه خارج المستودع. لا ترفع كلمات مروره أو المفتاح إلى Git. لا تستبدل APK سبق تثبيته بتوقيع مختلف بحذف بيانات المستخدم.

اختبر على الهاتف: إنشاء الحساب وإعادة الدخول، شراء وبيع دون إنترنت، إعادة فتح التطبيق وبقاء البيانات، التدريب المنفصل، التصدير والاستعادة بمرفقات، قراءة باركود من صورة، ومعاينة الطباعة واختيار الطابعة ومقاس الورق. دعم طابعات Bluetooth المباشر يعتمد على خدمة الطباعة المثبتة؛ لا يوجد بروتوكول ESC/POS خاص داخل التطبيق.
