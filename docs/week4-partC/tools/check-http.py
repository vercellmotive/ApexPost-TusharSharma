# Sends every request in ApexPost.Api.http and checks the status code against the one in its title.
# Usage: python3 docs/week4-partC/tools/check-http.py ApexPost.Api/ApexPost.Api.http https://localhost:7180
import re, sys, urllib.error, urllib.request, datetime
path, base = sys.argv[1], sys.argv[2]
text = open(path).read()
blocks = re.split(r'^### ', text, flags=re.M)[1:]
now = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
fails = 0
for b in blocks:
    lines = b.split('\n')
    title = lines[0]
    rest = [l for l in lines[1:]]
    # drop comment lines and trailing separators
    req = []
    for l in rest:
        if l.startswith('#'):
            break
        req.append(l)
    while req and not req[0].strip(): req.pop(0)
    method, url = req[0].split(' ', 1)
    url = url.replace('{{baseUrl}}', base)
    headers = {}
    i = 1
    while i < len(req) and req[i].strip():
        k, v = req[i].split(':', 1); headers[k.strip()] = v.strip(); i += 1
    body = '\n'.join(req[i+1:]).strip().replace('{{$datetime iso8601}}', now)
    expected = int(re.search(r': (\d{3})', title).group(1))
    r = urllib.request.Request(url, data=body.encode() if body else None, method=method, headers=headers)
    try:
        resp = urllib.request.urlopen(r); code = resp.status; hdrs = resp.headers
    except urllib.error.HTTPError as e:
        code = e.code; hdrs = e.headers
    ok = code == expected
    fails += not ok
    print(('PASS' if ok else 'FAIL'), code, title[:90], ('| Location: '+hdrs.get('Location')) if hdrs.get('Location') else '')
print('failures:', fails)
