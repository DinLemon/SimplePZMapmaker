"""Loopback MCP preflight; optionally read an isolated copy of one saved world section.

Never changes a workspace or an existing editor/agent session. Created verification
sessions are closed in finally. Reports contain neither session tokens nor payloads.
"""
import argparse
import datetime
import json
from pathlib import Path
import urllib.parse
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', default='http://127.0.0.1:48779')
    parser.add_argument('--workspace')
    parser.add_argument('--member')
    parser.add_argument('--out', type=Path, default=Path('work/mcp-connection'))
    args = parser.parse_args()
    host = urllib.parse.urlsplit(args.base)
    if host.scheme != 'http' or host.hostname != '127.0.0.1' or host.path not in ('', '/') or host.query or host.fragment or host.username or host.password:
        parser.error('Use a plain loopback http://127.0.0.1:port endpoint.')
    if bool(args.workspace) != bool(args.member):
        parser.error('--workspace and --member must be provided together.')
    if any(value and (len(value) != 32 or any(c not in '0123456789abcdefABCDEF' for c in value)) for value in [args.workspace, args.member]):
        parser.error('Workspace and section IDs must contain 32 hexadecimal characters.')
    base = args.base.rstrip('/')
    http = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    report = {'checkedAtUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'endpoint': base + '/mcp', 'passed': False, 'existingSessionsModified': False, 'workspaceModified': False, 'protocols': []}
    token = None

    def request(path, body=None, headers=None):
        data = None if body is None else json.dumps(body).encode()
        supplied = {'Origin': base, 'Accept': 'application/json'}
        if body is not None:
            supplied['Content-Type'] = 'application/json'
        supplied.update(headers or {})
        with http.open(urllib.request.Request(base + path, data, supplied), timeout=120) as response:
            return response.read(), dict(response.headers)

    def api(path, body=None):
        return json.loads(request(path, body)[0])

    try:
        if args.workspace:
            snapshot = api(f'/api/world/workspaces/{args.workspace}/sections/{args.member}')
            project = snapshot['member']['project']
            original = json.dumps(project, sort_keys=True)
            session = api('/api/agent/session', project)
            token = session['token']
            report.update(workspaceId=args.workspace, memberId=args.member, workspaceRevision=snapshot['revision'], mapCopyContainsSharedWorldGeometry=False)
        for protocol in ['2025-11-25', '2025-06-18']:
            transport_headers = {'Accept': 'application/json, text/event-stream'}
            serial = 0

            def rpc(method, params=None, notification=False):
                nonlocal serial
                body = {'jsonrpc': '2.0', 'method': method}
                if params is not None:
                    body['params'] = params
                if not notification:
                    serial += 1
                    body['id'] = serial
                raw, headers = request('/mcp', body, transport_headers)
                for key, value in headers.items():
                    if key.lower() == 'mcp-session-id':
                        transport_headers['Mcp-Session-Id'] = value
                if notification:
                    return None
                try:
                    response = json.loads(raw)
                except json.JSONDecodeError:
                    messages = [json.loads('\n'.join(line[5:].lstrip() for line in event.splitlines() if line.startswith('data:'))) for event in raw.decode().replace('\r\n', '\n').split('\n\n') if any(line.startswith('data:') for line in event.splitlines())]
                    response = next(value for value in messages if value.get('id') == serial)
                if response.get('id') != serial or 'error' in response:
                    raise RuntimeError('MCP JSON-RPC response failed validation.')
                return response['result']

            initialized = rpc('initialize', {'protocolVersion': protocol, 'capabilities': {}, 'clientInfo': {'name': 'SimplePZMapmaker-connection-preflight', 'version': '1'}})
            if initialized['protocolVersion'] != protocol:
                raise RuntimeError('MCP negotiated a different protocol than the one being verified.')
            transport_headers['MCP-Protocol-Version'] = initialized['protocolVersion']
            rpc('notifications/initialized', notification=True)
            found, cursor = [], None
            while True:
                result = rpc('tools/list', {'cursor': cursor} if cursor else {})
                found.extend(result['tools'])
                cursor = result.get('nextCursor')
                if not cursor:
                    break
            names = sorted(t['name'] for t in found)
            report['discoveredToolNames'] = names
            if not {'project_summary', 'building_draft_create', 'world_buildings', 'assets_search'} <= set(names):
                raise RuntimeError('Required editor MCP tools are missing.')
            check = {'requestedProtocol': protocol, 'negotiatedProtocol': initialized['protocolVersion'], 'tools': len(names), 'toolNames': names}
            if token:
                result = rpc('tools/call', {'name': 'project_summary', 'arguments': {'sessionToken': token}})
                if result.get('isError'):
                    raise RuntimeError('project_summary returned an MCP error.')
                # Match the official SDK smoke client's text representation. Object
                # return types can wrap structuredContent in a result envelope.
                texts = [block['text'] for block in result.get('content', []) if block['type'] == 'text']
                summary = json.loads(texts[0]) if texts else result.get('structuredContent', {})
                if 'result' in summary:
                    summary = summary['result']
                if summary.get('name') != project['name'] or summary.get('revision') != 1:
                    raise RuntimeError('project_summary differs from the isolated source section.')
                check['project'] = {'name': summary['name'], 'revision': summary['revision'], 'buildings': len(summary['buildings']), 'dependencies': summary['dependencies']}
            report['protocols'].append(check)
            print(f'PASS MCP {protocol}: {len(names)} tools' + ('; real saved section summary read' if token else ''), flush=True)
        if token:
            current = api('/api/agent/session/' + token)
            if current['revision'] != 1 or json.dumps(current['project'], sort_keys=True) != original:
                raise RuntimeError('Read-only verification changed its map copy.')
        report['passed'] = True
    except Exception as error:
        report['errorType'] = type(error).__name__
        if isinstance(error, RuntimeError):
            report['error'] = str(error)
        print('FAIL MCP connection: ' + type(error).__name__, flush=True)
    finally:
        if token:
            try:
                api('/api/agent/session/' + token + '/close', {})
                report['verificationSessionClosed'] = True
            except Exception as error:
                report.update(passed=False, verificationSessionClosed=False, cleanupErrorType=type(error).__name__)
        args.out.mkdir(parents=True, exist_ok=True)
        (args.out / 'report.json').write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
