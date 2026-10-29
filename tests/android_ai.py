"""Exercise the actual Android JavaScript bridge and native local inference.

Uses a fresh debug emulator, synthetic financial data, verified model bytes and CDP.
No HTTP inference server exists inside the app. Model installation here uses run-as;
the end-user SAF picker remains a physical-device check.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import time
import urllib.request
import websocket

PACKAGE = 'com.supermarket.accounting'
SHA = '9acfc1e001311f34b4252001b626f2e466d592a42065f66571bff3790d4e1b14'
PROFILE = {'id':'qwen3-06b-q4km','name':'Qwen3 0.6B — خفيف','fileName':'Qwen_Qwen3-0.6B-Q4_K_M.gguf','bytes':484220320,'sha256':SHA,'downloadUrl':''}

def adb(*args):
    return subprocess.check_output(['adb',*args],text=True,timeout=40).strip()

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('model')
    args=parser.parse_args()
    model=Path(args.model)
    with model.open('rb') as source: assert hashlib.file_digest(source,'sha256').hexdigest()==SHA
    out=Path('artifacts/android-ai');out.mkdir(parents=True,exist_ok=True)
    pid=adb('shell','pidof',PACKAGE).split()[0]
    socket='webview_devtools_remote_'+pid
    port=adb('forward','tcp:0','localabstract:'+socket)
    pages=[]
    for attempt in range(20):
        try:
            pages=json.load(urllib.request.urlopen(f'http://127.0.0.1:{port}/json/list',timeout=5))
            if pages: break
        except Exception: pass
        time.sleep(1)
    if not pages: raise AssertionError('Debug WebView target missing')
    page=next(p for p in pages if p.get('type')=='page')
    wsurl=re.sub(r'ws://[^/]+',f'ws://127.0.0.1:{port}',page['webSocketDebuggerUrl'])
    connection=websocket.create_connection(wsurl,timeout=240,suppress_origin=True)
    serial=0
    def evaluate(expression):
        nonlocal serial
        serial+=1
        connection.send(json.dumps({'id':serial,'method':'Runtime.evaluate','params':{'expression':expression,'awaitPromise':True,'returnByValue':True}}))
        while True:
            result=json.loads(connection.recv())
            if result.get('id')!=serial: continue
            if 'error' in result: raise AssertionError(result['error'])
            value=result['result']
            if value.get('exceptionDetails'): raise AssertionError(value['exceptionDetails'])
            return value['result'].get('value')
    try:
        evaluate("rpc('bootstrap',{name:'Owner',pin:'123456',storeName:'Emulator QA',currency:'SAR'})")
        evaluate("initialize()")
        evaluate("rpc('shift.open')")
        evaluate("rpc('cash.post',{kind:'Capital',amount:1000,note:'QA capital',requestId:crypto.randomUUID()})")
        evaluate("rpc('products.save',{name:'حليب',barcode:'1001',baseUnit:'عبوة',minimum:2,price:5})")
        evaluate("(async()=>{const unit=(await rpc('products')).units[0].id;return rpc('invoice.post',{kind:'Purchase',warehouseId:1,full:true,requestId:crypto.randomUUID(),lines:[{unitId:unit,quantity:10,price:3}]})})()")
        evaluate("(async()=>{const unit=(await rpc('products')).units[0].id;return rpc('invoice.post',{kind:'Sale',warehouseId:1,full:true,requestId:crypto.randomUUID(),lines:[{unitId:unit,quantity:2,price:5}]})})()")
        adb('shell','run-as',PACKAGE,'mkdir','-p','files/madar-ai')
        # adb exec-in may not deliver stdin EOF to tee on newer platform-tools.
        # Push once, then perform a bounded local copy rather than an open pipe.
        temporary='/data/local/tmp/madar-qa-model.gguf'
        subprocess.run(['adb','push',str(model),temporary],check=True,timeout=240)
        adb('shell','chmod','644',temporary)
        adb('shell','run-as',PACKAGE,'cp',temporary,'files/madar-ai/model.gguf')
        adb('shell','rm',temporary)
        installed=adb('shell','run-as',PACKAGE,'sha256sum','files/madar-ai/model.gguf').split()[0]
        assert installed==SHA,installed
        marker=out/'model.json';marker.write_text(json.dumps(PROFILE),encoding='utf-8')
        subprocess.run(['adb','push',str(marker),'/data/local/tmp/madar-qa-model.json'],check=True,timeout=30)
        adb('shell','chmod','644','/data/local/tmp/madar-qa-model.json')
        adb('shell','run-as',PACKAGE,'cp','/data/local/tmp/madar-qa-model.json','files/madar-ai/model.json')
        adb('shell','rm','/data/local/tmp/madar-qa-model.json')
        status=evaluate("rpc('ai.status')")
        assert status['status']['ready'],status
        evaluate("go('assistant')")
        evaluate("askLocalAi({question:'كم رصيد الصندوق الحالي؟',days:30})")
        result=evaluate("({answer:aiState.answer,error:aiState.error,facts:aiState.facts})")
        assert not result['error'],result
        assert '980' in result['answer'] or '٩٨٠' in result['answer'],result
        assert result['facts']['cash']==98000 and result['facts']['netProfit']==400,result
        assert evaluate("rpc('dashboard')")['cash']==98000
        (out/'answer.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
        print('PASS actual Android bridge, native model and Arabic answer from posted SQLite facts')
        # A long generation must not monopolize the normal app request queue.
        result=evaluate("(async()=>{const p=rpc('ai.ask',{question:'اشرح بالتفصيل الفرق بين المبيعات والربح ورأس المال والديون',days:30});await new Promise(r=>setTimeout(r,500));const start=Date.now();const dashboard=await rpc('dashboard');const ms=Date.now()-start;await rpc('ai.cancel');return {reply:await p,ms,cash:dashboard.cash}})()")
        assert result['ms']<5000 and result['cash']==98000,result
        assert result['reply']['stopped'],result
        print('PASS cancellation and responsive financial read while native AI generates')
        assert len(evaluate("rpc('documents')"))==3,'AI changed financial documents'
        print('PASS model answers do not create financial documents')
        print('ALL ANDROID AI CHECKS PASSED')
    finally:
        connection.close()
        with (out/'screen.png').open('wb') as image: subprocess.run(['adb','exec-out','screencap','-p'],stdout=image,check=False,timeout=30)
        (out/'logcat.txt').write_text(adb('logcat','-d'),encoding='utf-8')
        (out/'memory.txt').write_text(adb('shell','dumpsys','meminfo',PACKAGE),encoding='utf-8')

if __name__=='__main__': main()
