"""Exercise the actual bundled UI and SQLite core at phone size.

The stdin adapter replaces only Android's transport; no app data or financial
responses are mocked. Camera, printing, native bridge and on-device rendering
still need the APK smoke test / a real Android phone.
"""
import argparse
import asyncio
import json
from contextlib import AsyncExitStack, contextmanager
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import tempfile
from threading import Thread

from playwright.async_api import async_playwright


@contextmanager
def static_server():
    directory = Path(__file__).resolve().parents[1]/'mobile/Supermarket.Android/www'
    class QuietHandler(SimpleHTTPRequestHandler):
        def log_message(self, *args):
            pass
    server = ThreadingHTTPServer(('127.0.0.1', 0), partial(QuietHandler, directory=str(directory)))
    thread = Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield f'http://127.0.0.1:{server.server_port}/index.html'
    finally:
        server.shutdown()
        server.server_close()
        thread.join()


async def main(url):
    parser = argparse.ArgumentParser()
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--chromium', default=None)
    parser.add_argument('--output', default='artifacts/ui')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    host = root/'tests/Madar.UiHost/bin/Release/net10.0/Madar.UiHost.dll'
    async with async_playwright() as pw, AsyncExitStack() as cleanup:
        with tempfile.TemporaryDirectory(prefix='madar-ui-') as directory:
            process = await asyncio.create_subprocess_exec(args.dotnet, str(host), directory, stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE)
            async def stop_process():
                process.stdin.close()
                try:
                    await asyncio.wait_for(process.wait(), 5)
                except asyncio.TimeoutError:
                    process.terminate()
                    await process.wait()
            cleanup.push_async_callback(stop_process)
            lock = asyncio.Lock()

            async def invoke(raw):
                async with lock:
                    process.stdin.write((raw+'\n').encode())
                    await process.stdin.drain()
                    response = await process.stdout.readline()
                    if not response:
                        raise RuntimeError((await process.stderr.read()).decode())
                    return json.loads(response)

            browser = await pw.chromium.launch(executable_path=args.chromium, headless=True, args=['--no-sandbox'])
            cleanup.push_async_callback(browser.close)
            context = await browser.new_context(viewport={'width':393,'height':852}, device_scale_factor=2, is_mobile=True, has_touch=True)
            await context.expose_function('madarTestInvoke', invoke)
            await context.add_init_script("window.MadarNative={invoke:raw=>window.madarTestInvoke(raw).then(result=>window.madarReceive(result))};")
            page = await context.new_page()
            async def capture(name):
                await page.locator('#loading').wait_for(state='hidden')
                await page.locator('#toast.show').wait_for(state='hidden')
                await page.evaluate('document.fonts.ready')
                await page.screenshot(path=str(output/name), animations='disabled')
            errors = []
            page.on('pageerror', lambda error: errors.append(str(error)))
            await page.goto(url)
            await page.get_by_role('heading',name='إنشاء حساب المدير').wait_for()
            await capture('01-welcome.png')
            for name,value in [('storeName','متجر الاختبار'),('currency','ر.س'),('name','عبدالله'),('pin','123456'),('confirm','123456')]:
                await page.locator(f'input[name="{name}"]').fill(value)
            await page.get_by_role('button',name='إنشاء المتجر',exact=True).click()
            await page.get_by_role('heading',name='مرحباً، عبدالله').wait_for()
            await page.locator('.bottom-nav [data-go="inventory"]').click()
            products = [('حليب طازج','1001','الألبان','عبوة','10'),('أرز','1002','الحبوب','كيس','25'),('سكر','1003','المواد الغذائية','كيس','8')]
            for title,barcode,category,unit,price in products:
                await page.get_by_role('button',name='إضافة منتج',exact=True).click()
                for name,value in [('name',title),('barcode',barcode),('category',category),('baseUnit',unit),('minimum','2'),('price',price)]:
                    await page.locator(f'dialog input[name="{name}"]').fill(value)
                await page.get_by_role('button',name='إضافة المنتج',exact=True).click()
                await page.locator('dialog').wait_for(state='hidden')
            await page.locator('[data-action="product"]').first.wait_for()
            await page.locator('.bottom-nav [data-go="accounts"]').click()
            await page.get_by_role('button',name='فتح وردية الصندوق',exact=True).click()
            await page.get_by_role('button',name='تأكيد',exact=True).click()
            await page.get_by_role('button',name='تمويل الصندوق',exact=True).click()
            await page.locator('dialog input[name="amount"]').fill('1000')
            await page.locator('dialog input[name="note"]').fill('رأس مال الاختبار')
            await page.get_by_role('button',name='حفظ الحركة',exact=True).click()
            await page.locator('dialog').wait_for(state='hidden')
            await page.locator('.bottom-nav [data-go="home"]').click()
            await page.locator('[data-go="purchase"]').click()
            for index,(amount,cost) in enumerate([('10','3'),('18','10'),('32','4')]):
                await page.get_by_role('button',name='إضافة منتج إلى الفاتورة',exact=True).click()
                await page.locator(f'[data-action="invoice.choose"][data-id="{index+1}"]').click()
                await page.locator(f'[data-line-quantity="{index}"]').fill(amount)
                await page.locator(f'[data-line-quantity="{index}"]').press('Tab')
                await page.locator(f'[data-line-price="{index}"]').fill(cost)
                await page.locator(f'[data-line-price="{index}"]').press('Tab')
            await page.get_by_role('button',name='اعتماد الفاتورة',exact=True).click()
            await page.get_by_role('button',name='تأكيد',exact=True).click()
            await page.get_by_role('heading',name='سجل العمليات',exact=True).wait_for()
            await page.locator('.bottom-nav [data-go="sale"]').click()
            for product in range(1,4):
                await page.get_by_role('button',name='إضافة منتج إلى الفاتورة',exact=True).click()
                await page.locator(f'[data-action="invoice.choose"][data-id="{product}"]').click()
            await page.locator('[data-line-quantity="0"]').fill('2')
            await page.locator('[data-line-quantity="0"]').press('Tab')
            await capture('03-sale.png')
            await page.get_by_role('button',name='اعتماد الفاتورة',exact=True).click()
            await page.get_by_role('button',name='تأكيد',exact=True).click()
            await page.get_by_role('heading',name='سجل العمليات',exact=True).wait_for()
            await page.locator('.bottom-nav [data-go="inventory"]').click()
            await page.locator('[data-action="product"]').first.wait_for()
            await capture('02-inventory.png')
            stock = await page.evaluate("rpc('products')")
            dashboard = await page.evaluate("rpc('dashboard')")
            assert {p['barcode']:p['quantity'] for p in stock['items']}=={'1001':8000,'1002':17000,'1003':31000}, stock
            assert dashboard['cash']==71500 and dashboard['sales']==5300, dashboard
            await page.locator('.bottom-nav [data-go="home"]').click()
            await page.get_by_role('heading',name='مرحباً، عبدالله').wait_for()
            await capture('04-home.png')
            assert await page.evaluate('document.documentElement.scrollWidth<=innerWidth'), 'Horizontal page overflow'
            await page.locator('.bottom-nav [data-go="more"]').click()
            await page.locator('[data-go="reports"]').click()
            await page.get_by_role('heading',name='تقارير متجرك').wait_for()
            await capture('05-reports.png')
            await page.locator('.bottom-nav [data-go="more"]').click()
            await page.locator('.menu-row[data-go="assistant"]').click()
            await page.get_by_role('heading',name='المساعد الذكي المحلي').wait_for()
            await page.get_by_role('button',name='استيراد النموذج GGUF',exact=True).wait_for()
            assert await page.get_by_role('button',name='اسأل المساعد',exact=True).count()==0, 'Missing model must not appear enabled'
            await capture('06-assistant-setup.png')
            await page.set_viewport_size({'width':320,'height':740})
            for destination in ('home','inventory','sale'):
                await page.locator(f'.bottom-nav [data-go="{destination}"]').click()
                await page.locator('#loading').wait_for(state='hidden')
                assert await page.evaluate('document.documentElement.scrollWidth<=innerWidth'), f'320px overflow: {destination}'
            assert not errors, errors
            print('PASS phone UI setup, product, shift, funding, purchase, sale, actual stock/cash, reports and overflow')


if __name__=='__main__':
    with static_server() as url:
        asyncio.run(main(url))
