# Sends every request in ApexPost.Api.http and checks the status code against the one in its title.
# It understands file variables (@name = value), named requests (# @name) and {{name.response.body.$.field}}.
# Usage: python3 docs/week4-partC/tools/check-http.py ApexPost.Api/ApexPost.Api.http [name=value ...]
# Example: python3 docs/week4-partC/tools/check-http.py ApexPost.Api/ApexPost.Api.http baseUrl=https://localhost:7180 partnerKey=...
import datetime, json, re, sys, urllib.error, urllib.request

path, overrides = sys.argv[1], dict(arg.split('=', 1) for arg in sys.argv[2:])
text = open(path).read()
variables = dict(re.findall(r'^@(\w+)\s*=\s*(.*)$', text, flags=re.M))
variables.update(overrides)
now = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
responses = {}

def expand(value):
    value = value.replace('{{$datetime iso8601}}', now)
    value = re.sub(r'\{\{(\w+)\.response\.body\.\$\.(\w+)\}\}', lambda m: str(responses.get(m.group(1), {}).get(m.group(2), '')), value)
    return re.sub(r'\{\{(\w+)\}\}', lambda m: variables.get(m.group(1), m.group(0)), value)

failures = 0
for block in re.split(r'^### ', text, flags=re.M)[1:]:
    lines = block.split('\n')
    title, name, request = lines[0], None, []
    for line in lines[1:]:
        if line.startswith('# @name '):
            name = line.split()[-1]
        elif line.startswith('# ='):
            break
        elif not line.startswith('#'):
            request.append(line)
    while request and not request[0].strip():
        request.pop(0)
    method, url = request[0].split(' ', 1)
    headers, index = {}, 1
    while index < len(request) and request[index].strip():
        key, value = request[index].split(':', 1)
        headers[key.strip()] = expand(value.strip())
        index += 1
    body = expand('\n'.join(request[index + 1:]).strip())
    expected = int(re.search(r': (\d{3})', title).group(1))
    try:
        response = urllib.request.urlopen(urllib.request.Request(expand(url), data=body.encode() if body else None, method=method, headers=headers))
        status, response_headers, content = response.status, response.headers, response.read()
    except urllib.error.HTTPError as error:
        status, response_headers, content = error.code, error.headers, error.read()
    if name and content:
        responses[name] = json.loads(content)
    failures += status != expected
    location = response_headers.get('Location')
    print('PASS' if status == expected else 'FAIL', status, title[:95], f'| Location: {location}' if location else '')
print('failures:', failures)
sys.exit(1 if failures else 0)
