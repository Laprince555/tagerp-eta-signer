# TagERP ETA Signer

Signs Egyptian Tax Authority (ETA) e-invoices with your company's USB token and sends the signatures back to TagERP.

**Download:** [tagerp-eta-signer.exe](https://github.com/Laprince555/tagerp-eta-signer/raw/main/tagerp-eta-signer.exe) (Windows 10/11, 64-bit, no install needed)

## Setup

1. Put `tagerp-eta-signer.exe` and `appsettings.json` in the same folder.
2. Edit `appsettings.json`:
   - `ServerUrl`: your TagERP address, e.g. `https://your-company.tagerp.app`
   - `SignerKey`: the Signing Bridge Key from **E-Invoicing Settings** in TagERP
   - `CertificateThumbprint`: leave empty unless the PC has more than one signing certificate
3. Plug in the USB token and run the program. Your token's own dialog asks for the PIN.

## Safety

- The PIN is typed only into the token driver's dialog. The program never sees, stores or sends it.
- The program only makes outgoing HTTPS calls to your TagERP server; nothing listens on your PC.
- Windows may show "Windows protected your PC" because the program is not code-signed yet: choose **More info → Run anyway**.

Source code is in [`source/`](source).

---

# برنامج التوقيع لـ TagERP

بيوقّع فواتير مصلحة الضرائب المصرية بفلاشة التوقيع الخاصة بشركتك، ويرجّع التوقيعات لـ TagERP.

**التنزيل:** [tagerp-eta-signer.exe](https://github.com/Laprince555/tagerp-eta-signer/raw/main/tagerp-eta-signer.exe) (ويندوز 10/11، 64 بت، من غير تسطيب)

## الإعداد

1. حط `tagerp-eta-signer.exe` و`appsettings.json` في نفس الفولدر.
2. عدّل `appsettings.json`:
   - `ServerUrl`: عنوان TagERP بتاعك، مثلاً `https://your-company.tagerp.app`
   - `SignerKey`: مفتاح برنامج التوقيع من شاشة **إعدادات الفاتورة الإلكترونية** في TagERP
   - `CertificateThumbprint`: سيبه فاضي إلا لو على الجهاز أكتر من شهادة توقيع
3. دخّل الفلاشة وشغّل البرنامج. نافذة الفلاشة نفسها هي اللي هتطلب الـ PIN.

## الأمان

- الـ PIN بيتكتب في نافذة تعريف الفلاشة بس. البرنامج عمره ما بيشوفه ولا يخزّنه ولا يبعته.
- البرنامج بيعمل اتصالات HTTPS خارجة لسيرفر TagERP بتاعك بس، ومفيش حاجة بتستقبل اتصالات على جهازك.
- ممكن ويندوز يطلع رسالة "Windows protected your PC" لأن البرنامج لسه مش موقّع بشهادة: اختار **More info ← Run anyway**.

الكود المصدري موجود في [`source/`](source).
