"""Install the actual signed APK and verify it opens on a fresh Android emulator."""
from pathlib import Path
import os, re, subprocess, sys, time, xml.etree.ElementTree as ET

package = 'com.supermarket.accounting'
root = Path(sys.argv[1])
apks = sorted(root.rglob('*-Signed.apk'))
if not apks: raise SystemExit('No signed APK found.')
out = Path('artifacts/android-launch')
out.mkdir(parents=True, exist_ok=True)

def adb(*args, check=True):
    result = subprocess.run(['adb',*args],capture_output=True,text=True,check=check)
    return result.stdout.strip()

try:
    adb('install','-r',str(apks[0]))
    adb('logcat','-c')
    component = adb('shell','cmd','package','resolve-activity','--brief',package).splitlines()[-1]
    if '/' not in component: raise AssertionError('Launcher activity missing.')
    print(adb('shell','am','start','-W','-n',component))
    for attempt in range(12):
        time.sleep(2)
        if not adb('shell','pidof',package,check=False):
            raise AssertionError('Android app exited during startup.')
        adb('shell','uiautomator','dump','/sdcard/app-window.xml',check=False)
        xml = adb('shell','cat','/sdcard/app-window.xml',check=False)
        if '<?xml' not in xml: continue
        (out/'window.xml').write_text(xml,encoding='utf-8')
        tree = ET.fromstring(xml[xml.index('<?xml'):])
        labels = [node.attrib.get('text','') for node in tree.iter('node')]
        if 'اتصال الهاتف بالخادم' in labels:
            assert not any('keeps stopping' in text or 'has stopped' in text for text in labels)
            print('PASS: signed Android APK installed and opened the first-run connection screen.')
            break
    else:
        raise AssertionError('First-run connection screen did not appear.')
    settings = adb('shell','dumpsys','package',package)
    (out/'package.txt').write_text(settings,encoding='utf-8')
    sdk = Path(os.environ.get('ANDROID_HOME') or os.environ['ANDROID_SDK_ROOT'])
    aapt = sorted(sdk.glob('build-tools/*/aapt2'))[-1]
    manifest = subprocess.check_output([str(aapt),'dump','xmltree',str(apks[0]),'--file','AndroidManifest.xml'],text=True)
    (out/'manifest.txt').write_text(manifest,encoding='utf-8')
    # Android 15's package dump omits this flag; inspect the packaged manifest.
    assert re.search(r'android:usesCleartextTraffic[^=\n]*=.*0xffffffff',manifest), 'LAN development APK still blocks HTTP.'
    print('PASS: development APK allows LAN HTTP traffic.')
finally:
    (out/'logcat.txt').write_text(adb('logcat','-d',check=False),encoding='utf-8')
    with (out/'screen.png').open('wb') as image:
        subprocess.run(['adb','exec-out','screencap','-p'],stdout=image,check=False)
