"""Run only against the disposable CI SQL instance created by DatabaseChecks."""
import json, os, subprocess, sys, time, uuid
from urllib.request import Request, urlopen
from urllib.error import HTTPError, URLError

if '--disposable-database' not in sys.argv:
    raise SystemExit('Refusing to write accounting data without --disposable-database.')
key = os.environ['ApiKey']
base = 'http://127.0.0.1:5080/api/'

def request(route, body=None, method=None, secret=key, expected=200):
    payload = None if body is None else json.dumps(body).encode()
    headers = {'Content-Type': 'application/json', 'X-Api-Key': secret}
    req = Request(base+route, data=payload, method=method or ('POST' if body is not None else 'GET'), headers=headers)
    try:
        with urlopen(req, timeout=10) as response:
            status, content = response.status, response.read()
    except HTTPError as error:
        status, content = error.code, error.read()
    assert status == expected, f'{route}: expected {expected}, received {status}'
    return json.loads(content) if content else None

def cash(kind, amount, party=None):
    return request('cash', {'requestId':str(uuid.uuid4()),'kind':kind,'amount':amount,'partyId':party,'note':'CI accounting check'})

process = subprocess.Popen(['dotnet','run','--project','src/Supermarket.Api','-c','Release','--no-build','--no-launch-profile'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
try:
    for attempt in range(60):
        if process.poll() is not None:
            raise RuntimeError('API exited before readiness.')
        try:
            request('health')
            break
        except (URLError, AssertionError):
            if attempt == 59: raise
            time.sleep(1)
    request('health',secret='incorrect-key',expected=401)
    product_input={'barcode':str(uuid.uuid4()),'name':'CI product','salePrice':10,'minimumStock':2,'isActive':True}
    product = request('products', product_input)['id']
    customer = request('parties', {'name':'CI customer','kind':'Customer','phone':''})['id']
    supplier = request('parties', {'name':'CI supplier','kind':'Supplier','phone':''})['id']
    cash('Capital',1000)
    purchase={'requestId':str(uuid.uuid4()),'kind':'Purchase','partyId':supplier,'paid':20,'note':'CI purchase','items':[{'productId':product,'quantity':10,'unitPrice':6}]}
    request('invoices',purchase)
    sale={'requestId':str(uuid.uuid4()),'kind':'Sale','partyId':customer,'paid':10,'note':'CI sale','items':[{'productId':product,'quantity':3,'unitPrice':10}]}
    posted = request('invoices',sale)
    replay = request('invoices',sale)
    assert posted['id']==replay['id'], 'Retry created a duplicate invoice.'
    row = next(p for p in request('products') if p['id']==product)
    assert row['stock']==7 and row['stockValue']==42 and row['averageCost']==6
    parties = {p['id']:p for p in request('parties')}
    assert parties[customer]['balance']==20 and parties[supplier]['balance']==40
    cash('Receipt',20,customer)
    cash('Payment',40,supplier)
    cash('Expense',5)
    parties = {p['id']:p for p in request('parties')}
    assert parties[customer]['balance']==0 and parties[supplier]['balance']==0
    values = {r['name']:r['value'] for r in request('dashboard')}
    assert values['رصيد الصندوق']==965 and values['صافي الربح منذ البداية']==7
    rejected=dict(sale,requestId=str(uuid.uuid4()),items=[{'productId':product,'quantity':100,'unitPrice':10}])
    request('invoices',rejected,expected=409)
    row = next(p for p in request('products') if p['id']==product)
    assert row['stock']==7, 'Rejected sale changed stock.'
    rejected=dict(sale,requestId=str(uuid.uuid4()),items=[{'productId':product,'quantity':1,'unitPrice':10.001}])
    request('invoices',rejected,expected=400)
    product_input['isActive']=False
    request(f'products/{product}',product_input,method='PUT')
    rejected=dict(sale,requestId=str(uuid.uuid4()),items=[{'productId':product,'quantity':1,'unitPrice':10}])
    request('invoices',rejected,expected=409)
    request(f'documents/{posted["id"]}')
    for report in ('trial','income','journal'):
        request(f'reports/{report}?from=2000-01-01&to=2099-12-31')
    request('reports/income?from=2030-01-02&to=2030-01-01',expected=400)
    print('PASS: API authentication, purchases, sales, retries, balances, settlements, stock rejection, precision, inactive products and reports.')
finally:
    process.terminate()
    try: process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.kill(); process.wait()
