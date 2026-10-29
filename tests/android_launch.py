"""Install the signed offline APK and check the real Android bridge at first run."""
from pathlib import Path
import os
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

package = 'com.supermarket.accounting'
apks = sorted(Path(sys.argv[1]).rglob('*-Signed.apk'))
if not apks:
    raise SystemExit('No signed APK found.')
out = Path('artifacts/android-launch')
out.mkdir(parents=True, exist_ok=True)

def adb(*args, check=True):
    return subprocess.run(['adb', *args], capture_output=True, text=True, check=check, timeout=40).stdout.strip()

try:
    sdk = Path(os.environ.get('ANDROID_HOME') or os.environ['ANDROID_SDK_ROOT'])
    aapt = sorted(sdk.glob('build-tools/*/aapt2'))[-1]
    manifest = subprocess.check_output([str(aapt), 'dump', 'xmltree', str(apks[0]), '--file', 'AndroidManifest.xml'], text=True)
    (out/'manifest.txt').write_text(manifest, encoding='utf-8')
    assert 'android.permission.INTERNET' not in manifest, 'Offline app unexpectedly requests Internet access.'
    assert 'android.permission.ACCESS_NETWORK_STATE' not in manifest, 'Network access configuration returned.'
    assert not re.search(r'android:usesCleartextTraffic[^=\n]*=.*(?:\btrue\b|0xffffffff)', manifest), 'Cleartext traffic enabled.'
    print('PASS packaged manifest has no Internet/network-state permissions or cleartext access.')
    adb('install', '-r', str(apks[0]))
    adb('logcat', '-c')
    component = adb('shell', 'cmd', 'package', 'resolve-activity', '--brief', package).splitlines()[-1]
    if '/' not in component:
        raise AssertionError('Launcher activity missing.')
    print(adb('shell', 'am', 'start', '-W', '-n', component))
    for attempt in range(20):
        time.sleep(2)
        if not adb('shell', 'pidof', package, check=False):
            raise AssertionError('Android app exited during startup.')
        adb('shell', 'uiautomator', 'dump', '/sdcard/app-window.xml', check=False)
        xml = adb('shell', 'cat', '/sdcard/app-window.xml', check=False)
        if '<?xml' not in xml:
            continue
        (out/'window.xml').write_text(xml, encoding='utf-8')
        tree = ET.fromstring(xml[xml.index('<?xml'):])
        labels = [node.attrib.get('text', '')+' '+node.attrib.get('content-desc', '') for node in tree.iter('node')]
        # A fresh Google APIs image can show its own Pixel Launcher ANR over
        # our already-loaded setup screen. Dismiss that specific OS dialog;
        # never suppress an ANR or crash belonging to the app under test.
        if any('Pixel Launcher' in label and ('responding' in label or 'الاستجابة' in label) for label in labels):
            (out/'launcher-anr.xml').write_text(xml,encoding='utf-8')
            close=next((node for node in tree.iter('node') if node.attrib.get('text') in ('Close app','إغلاق التطبيق')),None)
            if close is not None:
                bounds=list(map(int,re.findall(r'\d+',close.attrib['bounds'])))
                adb('shell','input','tap',str((bounds[0]+bounds[2])//2),str((bounds[1]+bounds[3])//2))
                print('Dismissed external Pixel Launcher ANR; target app remains running.')
                continue
        if any('إنشاء حساب المدير' in label for label in labels):
            assert not any('keeps stopping' in label or 'has stopped' in label for label in labels)
            assert not any('اتصال الهاتف بالخادم' in label for label in labels)
            print('PASS signed Android APK opens local owner creation through the native SQLite bridge.')
            break
    else:
        raise AssertionError('Local setup screen did not appear; inspect bridge/WebView startup logs.')
    (out/'package.txt').write_text(adb('shell', 'dumpsys', 'package', package), encoding='utf-8')
finally:
    (out/'logcat.txt').write_text(adb('logcat', '-d', check=False), encoding='utf-8')
    with (out/'screen.png').open('wb') as screenshot:
        subprocess.run(['adb', 'exec-out', 'screencap', '-p'], stdout=screenshot, check=False, timeout=30)
