"""Local desktop controls and read-only AI account/session observations."""
import concurrent.futures
import ctypes
import json
import os
import queue
import re
import shutil
import sqlite3
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

import psutil

ROOT = Path(__file__).resolve().parent
DATA = ROOT / 'data'
CONFIG = ROOT / 'settings.json'
HOME = Path.home()
NO_WINDOW = 0x08000000
CODEX_BIN = shutil.which('codex.exe') or shutil.which('codex') or 'codex'


def read_json(path, default=None):
    try:
        return json.loads(Path(path).read_text(encoding='utf-8-sig'))
    except (OSError, ValueError):
        return default


def save_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix('.tmp')
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
    temp.replace(path)


def default_config():
    return {'topmost': True, 'monitor_ai': False, 'workbenches': []}


def config():
    return read_json(CONFIG, default_config())


def fetch(url, headers=None, timeout=3):
    req = urllib.request.Request(url, headers=headers or {})
    with urllib.request.urlopen(req, timeout=timeout) as response:
        return response.read(2_000_000)


def port_of(item):
    return urllib.parse.urlparse(item['url']).port or 80


def listener(port):
    for entry in psutil.net_connections(kind='tcp4'):
        if entry.status == psutil.CONN_LISTEN and entry.laddr.port == port and entry.laddr.ip in ('127.0.0.1', '0.0.0.0'):
            return entry.pid
    return None


def matches_process(item, pid):
    try:
        proc = psutil.Process(pid)
        args = proc.cmdline()
        base = Path(item['cwd']).resolve()
        try:
            cwd = Path(proc.cwd()).resolve()
        except psutil.AccessDenied:  # 從 AI 對話啟動的程序讀不到工作目錄，改比對指令裡的路徑與連接埠
            cwd = None
        named = lambda p: any(str(x).lower() in a.lower() for x in (p, p.resolve()) for a in args)
        here = cwd == base or named(Path(item['cwd'])) or (
            cwd is None and str(port_of(item)) in args and named(Path(item['cwd']).parent))
        if 'command' in item:
            return here and any(item['match'].lower() in a.lower() for a in args)
        return here
    except (psutil.Error, OSError):
        return False


def service_state(item, ports=None):
    pid = ports.get(port_of(item)) if ports is not None else listener(port_of(item))
    running = False
    detail = '已關閉'
    if pid:
        if matches_process(item, pid):
            try:
                body = fetch(item['url'].rstrip('/') + item.get('health', '/'), timeout=2)
                running, detail = True, '運行中'
                if item['id'] == 'youtube':
                    state = json.loads(fetch(item['url'] + 'api/dashboard', timeout=2))
                    if not state['status']['key_configured']:
                        detail = '運行中 · 待 API Key'
            except Exception:
                detail = '程式存活 · 未就緒'
        else:
            detail = '連接埠由其他程式使用'
    return {**{k: item.get(k) for k in ('id', 'name', 'icon', 'color', 'url')}, 'running': running, 'pid': pid, 'detail': detail}


def detached(command, cwd, log):
    DATA.mkdir(exist_ok=True)
    with (DATA / log).open('a', encoding='utf-8') as output:
        return subprocess.Popen(command, cwd=cwd, stdin=subprocess.DEVNULL, stdout=output, stderr=output,
                                creationflags=NO_WINDOW | subprocess.CREATE_NEW_PROCESS_GROUP)


def launch_script(path, cwd):
    p = Path(path).resolve()
    if not p.is_file() or p.suffix.lower() not in ('.cmd', '.bat'):
        raise ValueError('啟動檔不存在，請選擇 CMD 或 BAT')
    command = '"' + os.environ.get('COMSPEC', r'C:\Windows\System32\cmd.exe') + '" /d /s /c ""' + str(p) + '""'
    return detached(command, cwd, 'services.log')


def start(item):
    pid = listener(port_of(item))
    if pid:
        if not matches_process(item, pid):
            raise ValueError('連接埠已被其他程式使用，未變更該程式')
        return
    if item.get('command'):
        detached(item['command'], item['cwd'], 'services.log')
    else:
        launch_script(item['launcher'], item['cwd'])
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline:
        if service_state(item)['running']:
            return
        time.sleep(.6)
    raise ValueError('工作檯尚未就緒，請查看設定的啟動檔；詳細錯誤在 data/services.log')


def stop(item):
    pid = listener(port_of(item))
    if not pid:
        return
    if not matches_process(item, pid):
        raise ValueError('目前占用者不是這個工作檯，未關閉任何程式')
    if item['id'] == 'comfyui':
        jobs = json.loads(fetch(item['url'].rstrip('/') + '/queue'))
        if jobs['queue_running'] or jobs['queue_pending']:
            raise ValueError('ComfyUI 仍有生成任務，請先在畫布完成或取消任務再關閉')
    if item['id'] == 'table':
        sessions = json.loads(fetch(item['url'] + 'api/sessions'))
        if any(not s.get('archived') and s.get('state') in ('busy', 'running', 'executing', 'discussing', 'building') for s in sessions):
            raise ValueError('AI 同桌仍有任務執行，請先在同桌結束任務再關閉')
    if item['id'] == 'youtube':
        req = urllib.request.Request(item['url'] + 'api/monitor', data=b'{"enabled":false}', headers={'Content-Type': 'application/json'})
        urllib.request.urlopen(req, timeout=3).close()
        deadline = time.monotonic() + 30
        while json.loads(fetch(item['url'] + 'api/dashboard'))['status']['running']:
            if time.monotonic() > deadline:
                raise ValueError('本輪資料仍在儲存；已暫停後續收集，稍後再關閉')
            time.sleep(.5)
    if item.get('stop_launcher'):
        p = launch_script(item['stop_launcher'], item['cwd'])
        try:
            p.wait(timeout=15)
        except subprocess.TimeoutExpired:
            raise ValueError('停止檔尚未結束')
    else:
        # Terminate only the verified listener, never all Python/Node processes or AI children.
        process = psutil.Process(pid)
        if matches_process(item, pid):
            process.terminate()
            process.wait(timeout=8)
    if listener(port_of(item)):
        raise ValueError('工作檯仍在運行，停止檔未完成關閉')


def codex_limits():
    proc = subprocess.Popen([str(CODEX_BIN), 'app-server'], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                            stderr=subprocess.DEVNULL, text=True, encoding='utf-8', creationflags=NO_WINDOW)
    replies = queue.Queue()
    def reader():
        for line in proc.stdout:
            try:
                replies.put(json.loads(line))
            except ValueError:
                pass
    threading.Thread(target=reader, daemon=True).start()
    def rpc(i, method, params=None):
        msg = {'id': i, 'method': method}
        if params is not None:
            msg['params'] = params
        proc.stdin.write(json.dumps(msg) + '\n'); proc.stdin.flush()
        end = time.monotonic() + 10
        while time.monotonic() < end:
            reply = replies.get(timeout=max(.1, end - time.monotonic()))
            if reply.get('id') == i:
                if 'error' in reply:
                    raise ValueError('Codex 額度無法讀取，請確認登入狀態')
                return reply['result']
        raise ValueError('Codex 額度讀取逾時')
    try:
        rpc(0, 'initialize', {'clientInfo': {'name': 'workbench-hub', 'version': '1.0'}})
        proc.stdin.write('{"method":"initialized"}\n'); proc.stdin.flush()
        data = rpc(1, 'account/rateLimits/read')
        limits = data.get('rateLimitsByLimitId', {}).get('codex') or data.get('rateLimits', {})
        out = {'five': None, 'week': None, 'source': 'Codex 官方帳號介面'}
        for value in (limits.get('primary'), limits.get('secondary')):
            if value:
                field = {300: 'five', 10080: 'week'}.get(value.get('windowDurationMins'))
                if field:
                    out[field] = {'remaining': max(0, min(100, 100 - value['usedPercent'])), 'reset': value.get('resetsAt')}
        return out
    finally:
        proc.terminate()
        try: proc.wait(timeout=3)
        except subprocess.TimeoutExpired: proc.kill()


def claude_limits():
    auth = read_json(HOME / '.claude/.credentials.json', {}).get('claudeAiOauth', {})
    if auth.get('expiresAt', 0) / 1000 < time.time() + 60:
        raise ValueError('Claude 登入已過期，請自行使用官方程式登入')
    if not auth.get('accessToken'):
        raise ValueError('Claude Code 尚未登入')
    headers = {'Authorization': 'Bearer ' + auth['accessToken'], 'anthropic-beta': 'oauth-2025-04-20', 'User-Agent': 'claude-code/2.1.302'}
    data = json.loads(fetch('https://api.anthropic.com/api/oauth/usage', headers, timeout=12))
    out = {'source': 'Claude 帳號額度'}
    for src, dst in (('five_hour', 'five'), ('seven_day', 'week')):
        value = data.get(src)
        out[dst] = {'remaining': max(0, min(100, 100 - value['utilization'])),
                    'reset': datetime.fromisoformat(value['resets_at'].replace('Z', '+00:00')).timestamp()} if value else None
    return out


def quotas(refresh=False):
    result = read_json(DATA / 'quota.json', {})
    for name, method in (('claude', claude_limits), ('codex', codex_limits)):
        value = result.get(name, {})
        if refresh or time.time() - value.get('fetched', 0) > 120:
            try:
                value = {**method(), 'fetched': time.time(), 'error': None}
            except Exception as error:
                code = getattr(error, 'code', None) if isinstance(error, urllib.error.HTTPError) else None
                message = {401: '登入已過期', 429: '查詢太頻繁，稍後自動重試'}.get(code, '額度暫時讀取不到')
                value = {**value, 'fetched': time.time(), 'error': message}
            result[name] = value
    save_json(DATA / 'quota.json', result)
    # Keep previous observations identified as stale, never silently turn missing data into 0%.
    return result


def claude_status():
    sessions = []
    for file in (HOME / '.claude/sessions').glob('*.json'):
        item = read_json(file, {})
        try:
            proc = psutil.Process(item['pid'])
            if proc.name().lower() == 'claude.exe':
                sessions.append(item.get('status', 'unknown'))
        except (KeyError, psutil.Error):
            pass
    count = sum(s in ('running', 'busy', 'working', 'processing') for s in sessions)
    waiting = sum(s in ('waiting', 'waiting_for_permission', 'waitingForApproval', 'needs_attention') for s in sessions)
    unknown = sum(s not in ('idle', 'running', 'busy', 'working', 'processing', 'waiting', 'waiting_for_permission', 'waitingForApproval', 'needs_attention') for s in sessions)
    return {'running': bool(sessions), 'active': count, 'task': '等待確認' if waiting else '執行中' if count else '狀態未知' if unknown else '待命',
            'source': 'Claude Code 工作階段狀態', 'sessions': len(sessions)}


def last_turn_event(path):
    """Read only lifecycle event types from the bounded tail, not conversation text."""
    with Path(path).open('rb') as f:
        f.seek(0, 2); size = f.tell(); start = max(0, size - 4_000_000); f.seek(start)
        lines = f.read().splitlines()
    if start:
        lines = lines[1:]
    for line in reversed(lines):
        # Avoid decoding unrelated content and messages.
        if b'"event_msg"' not in line or not any(x in line for x in (b'"task_started"', b'"task_complete"', b'"turn_aborted"', b'"task_aborted"')):
            continue
        try:
            record = json.loads(line)
            kind = record.get('payload', {}).get('type')
            if record.get('type') == 'event_msg' and kind in ('task_started', 'task_complete', 'turn_aborted', 'task_aborted'):
                return kind
        except ValueError:
            pass
    return None


def codex_status():
    app = any('openai.codex_' in (p.info.get('exe') or '').lower() and p.info.get('name', '').lower() == 'chatgpt.exe'
              for p in psutil.process_iter(['name', 'exe']))
    if not app:
        return {'running': False, 'active': 0, 'task': '已關閉', 'source': '桌面程序'}
    cached = read_json(DATA / 'codex-events.json', {})
    found = {}
    try:
        database = HOME / '.codex/state_5.sqlite'
        with sqlite3.connect(database.as_uri() + '?mode=ro', uri=True, timeout=2) as db:
            rows = db.execute("SELECT id,rollout_path FROM threads WHERE archived=0 AND source NOT LIKE '%subagent%' AND updated_at>? ORDER BY updated_at DESC LIMIT 100", (int(time.time()) - 86400,)).fetchall()
        for identifier, path in rows:
            try:
                stamp = Path(path).stat().st_mtime_ns
                old = cached.get(identifier, {})
                event = old.get('event') if old.get('mtime') == stamp else last_turn_event(path)
                found[identifier] = {'mtime': stamp, 'event': event}
            except OSError:
                pass
        save_json(DATA / 'codex-events.json', found)
        active = sum(v['event'] == 'task_started' for v in found.values())
        unknown = any(v['event'] is None for v in found.values())
        return {'running': app, 'active': active, 'task': '執行中' if active else '狀態未知' if unknown else '待命',
                'source': '本機工作階段開始／完成事件（近 24 小時，最多 100 個）'}
    except (OSError, sqlite3.Error):
        return {'running': app, 'active': None, 'task': '狀態未知', 'source': '工作階段狀態暫時不可讀'}


def activate_ai(name):
    # Bringing forward a native app does not submit prompts or start model work.
    user32 = ctypes.windll.user32
    pids = set()
    for proc in psutil.process_iter(['pid', 'name', 'exe']):
        exe = (proc.info.get('exe') or '').lower()
        if (name == 'codex' and 'openai.codex_' in exe and proc.info['name'].lower() == 'chatgpt.exe') or (name == 'claude' and 'windowsapps\\claude_' in exe):
            pids.add(proc.pid)
    windows = []
    callback_type = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
    def visit(hwnd, _):
        pid = ctypes.c_ulong()
        user32.GetWindowThreadProcessId(ctypes.c_void_p(hwnd), ctypes.byref(pid))
        if pid.value in pids and user32.IsWindowVisible(ctypes.c_void_p(hwnd)) and user32.GetWindowTextLengthW(ctypes.c_void_p(hwnd)) > 0:
            windows.append(hwnd)
        return True
    user32.EnumWindows(callback_type(visit), None)
    if windows:
        hwnd = ctypes.c_void_p(windows[0]); user32.ShowWindow(hwnd, 9); user32.SetForegroundWindow(hwnd)
        return
    # Existing desktop apps are identified from their installed Start-menu entries.
    app_name = 'Codex' if name == 'codex' else 'Claude'
    script = "$a=Get-StartApps | Where-Object {$_.Name -eq '" + app_name + "'} | Select-Object -First 1; if($a){Start-Process ('shell:AppsFolder\\'+$a.AppID)}else{exit 2}"
    p = subprocess.run(['powershell.exe', '-NoProfile', '-Command', script], creationflags=NO_WINDOW, capture_output=True)
    if p.returncode:
        raise ValueError('找不到已安裝的 ' + app_name + ' 桌面入口')


LONG_CONTEXT = 400_000  # 約 Codex 上下文上限一半；之後每次請求都重送很長內容（Claude 上限未實測，暫用同值）
MANY_SHOTS = 30
TOKEN_KEYS = ('fresh', 'cache_read', 'cache_write', 'output')


def _new_lines(path, entry):
    """Read only bytes appended since the last poll, so large logs are parsed once."""
    with Path(path).open('rb') as f:
        f.seek(entry['offset']); chunk = f.read()
    end = chunk.rfind(b'\n') + 1
    entry['offset'] += end
    return chunk[:end].splitlines()


def _first_text(text):
    text = re.split(r'^## My request[^:\n]*:', text, flags=re.M)[-1].strip()  # Codex 附檔時，真正的提問在這行之後
    return '' if text.startswith(('<', '# AGENTS', '# Files')) else ' '.join(text.split())[:40]


def _codex_lines(entry, lines):
    for line in lines:
        try:
            if b'call_output' in line:
                entry['shots'] += line.count(b'input_image')
            elif b'"token_count"' in line and b'total_token_usage' in line:
                info = json.loads(line)['payload']['info']
                u = info['total_token_usage']  # Codex 記的是累計值，輸入含快取
                entry.update(fresh=u['input_tokens'] - u.get('cached_input_tokens', 0), cache_read=u.get('cached_input_tokens', 0),
                             cache_write=u.get('cache_write_input_tokens', 0), output=u['output_tokens'],
                             context=(info.get('last_token_usage') or {}).get('input_tokens', 0))
            elif not entry['title'] and b'"role":"user"' in line:
                p = json.loads(line).get('payload', {})
                entry['title'] = _first_text(''.join(c.get('text', '') for c in p.get('content', []) if isinstance(c, dict)))
        except (ValueError, KeyError, TypeError):
            pass


def _claude_lines(entry, lines):
    for line in lines:
        try:
            if line.startswith(b'{"') and b'"type":"assistant"' in line and b'"usage"' in line:
                m = json.loads(line).get('message') or {}
                u = m.get('usage') or {}
                now = dict(fresh=u.get('input_tokens', 0), cache_read=u.get('cache_read_input_tokens', 0),
                           cache_write=u.get('cache_creation_input_tokens', 0), output=u.get('output_tokens', 0))
                # 同一則回覆會分多行寫入，只算最後一行
                before = entry['last'] if m.get('id') == entry['last_id'] else dict.fromkeys(TOKEN_KEYS, 0)
                for k in TOKEN_KEYS:
                    entry[k] += now[k] - before[k]
                entry.update(last_id=m.get('id'), last=now, context=now['fresh'] + now['cache_read'] + now['cache_write'])
            elif b'"type":"custom-title"' in line:
                entry['name'] = json.loads(line).get('customTitle', '')  # 側欄列表的名稱
            elif b'"type":"user"' in line:
                entry['shots'] += line.count(b'"type":"image"')
                if not entry['title']:
                    c = (json.loads(line).get('message') or {}).get('content')
                    entry['title'] = _first_text(c if isinstance(c, str) else ''.join(
                        x.get('text', '') for x in c or [] if isinstance(x, dict) and x.get('type') == 'text'))
        except (ValueError, KeyError, TypeError):
            pass


def token_signs(entry):
    signs = []
    if entry['context'] > LONG_CONTEXT:
        signs.append({'tag': '換對話', 'why': f"上下文 {entry['context'] // 1000}k，建議換新對話並附便條"})
    if entry['shots'] > MANY_SHOTS:
        signs.append({'tag': '截圖多', 'why': f"截圖 {entry['shots']} 張，改讀文字"})
    return signs


def token_usage():
    """Today's per-conversation usage from local Codex/Claude logs; read-only, refreshed at most every 30 s."""
    saved = read_json(DATA / 'tokens.json', {})
    if time.time() - saved.get('fetched', 0) < 30 and 'result' in saved:
        return saved['result']
    midnight = datetime.now().replace(hour=0, minute=0, second=0, microsecond=0).timestamp()
    old, files = saved.get('files', {}), {}
    for tool, paths, parse in (('Codex', HOME.glob('.codex/sessions/*/*/*/*.jsonl'), _codex_lines),
                               ('Claude', HOME.glob('.claude/projects/*/*.jsonl'), _claude_lines)):
        for path in paths:
            stat = path.stat()
            if stat.st_mtime < midnight:
                continue
            entry = old.get(str(path))
            if not entry or entry['offset'] > stat.st_size:
                entry = dict(tool=tool, title='', offset=0, shots=0, context=0, last_id=None, last=None, **dict.fromkeys(TOKEN_KEYS, 0))
            if entry['offset'] < stat.st_size:
                parse(entry, _new_lines(path, entry))
            entry['updated'] = stat.st_mtime
            files[str(path)] = entry
    names, archived = {}, set()  # 側欄列表的名稱與封存狀態：Codex 在資料庫，Claude 桌面 App 在 claude-code-sessions
    try:
        with sqlite3.connect((HOME / '.codex/state_5.sqlite').as_uri() + '?mode=ro', uri=True, timeout=2) as db:
            for p, n, a in db.execute('SELECT rollout_path,name,archived FROM threads'):
                p = os.path.normcase(p.removeprefix('\\\\?\\'))
                if n:
                    names[p] = n
                if a:
                    archived.add(p)
    except (OSError, sqlite3.Error):
        pass
    # Store 版 Claude 的資料實際在 Packages 底下，浮窗看不到 Roaming 的轉址，兩處都找
    for meta in [*HOME.glob('AppData/Roaming/Claude/claude-code-sessions/*/*/*.json'),
                 *HOME.glob('AppData/Local/Packages/Claude_*/LocalCache/Roaming/Claude/claude-code-sessions/*/*/*.json')]:
        item = read_json(meta, {})
        if item.get('cliSessionId') and item.get('title'):
            names[item['cliSessionId']] = item['title']
        if item.get('cliSessionId') and item.get('isArchived'):
            archived.add(item['cliSessionId'])
    keys = {path: os.path.normcase(path) if e['tool'] == 'Codex' else Path(path).stem for path, e in files.items()}
    for path, e in files.items():
        e['name'] = names.get(keys[path], e.get('name', ''))
    rows = sorted((e for path, e in files.items() if keys[path] not in archived and any(e[k] for k in TOKEN_KEYS)), key=lambda e: -e['updated'])
    result = {'rows': [{k: e[k] for k in ('tool', 'context', 'shots', 'updated', *TOKEN_KEYS)} | {'title': e['name'] or e['title'], 'signs': token_signs(e)} for e in rows],
              'total': {k: sum(e[k] for e in rows) for k in TOKEN_KEYS}}
    save_json(DATA / 'tokens.json', {'fetched': time.time(), 'files': files, 'result': result})
    return result


def state(refresh=False):
    cfg = config()
    ports = {c.laddr.port: c.pid for c in psutil.net_connections(kind='tcp4') if c.status == psutil.CONN_LISTEN and c.laddr.ip in ('127.0.0.1', '0.0.0.0')}
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        services = list(pool.map(lambda i: service_state(i, ports), cfg['workbenches']))
    if not cfg.get('monitor_ai', False):
        status = {'running': False, 'active': None, 'task': '未啟用', 'source': 'AI 監控尚未啟用'}
        return {'services': services, 'claude': status, 'codex': status,
                'quotas': {}, 'tokens': {'error': 'AI 監控尚未啟用'}, 'updated': time.time()}
    try:
        tokens = token_usage()
    except Exception:
        tokens = {'error': '用量暫時讀取不到'}
    return {'services': services, 'claude': claude_status(), 'codex': codex_status(), 'quotas': quotas(refresh), 'tokens': tokens, 'updated': time.time()}



def execute(request):
    action = request.get('action', 'status')
    if action in ('status', 'refresh'):
        return state(action == 'refresh')
    if action == 'open_ai':
        if request.get('id') not in ('claude', 'codex'):
            raise ValueError('不支援的 AI 程式')
        activate_ai(request['id']); return {'ok': True}
    cfg = config()
    if action == 'add':
        name = str(request.get('name', '')).strip()
        path = Path(request.get('launcher', '')).resolve()
        url = urllib.parse.urlparse(request.get('url', ''))
        if not name or len(name) > 40 or not path.is_file() or path.suffix.lower() not in ('.cmd', '.bat'):
            raise ValueError('請填名稱並選擇有效 CMD／BAT')
        if url.scheme != 'http' or url.hostname not in ('127.0.0.1', 'localhost') or not url.port:
            raise ValueError('請填本機網址，例如 http://127.0.0.1:9000/')
        stop_path = request.get('stop_launcher', '')
        if stop_path and (not Path(stop_path).is_file() or Path(stop_path).suffix.lower() not in ('.cmd', '.bat')):
            raise ValueError('停止檔必須是有效 CMD／BAT')
        if any(port_of(s) == url.port for s in cfg['workbenches']):
            raise ValueError('這個連接埠已加入')
        item = {'id': 'custom-' + str(time.time_ns()), 'name': name, 'url': request['url'], 'launcher': str(path),
                'cwd': str(path.parent), 'stop_launcher': stop_path, 'icon': '▣', 'color': '#7090E7'}
        cfg['workbenches'].append(item); save_json(CONFIG, cfg); return {'ok': True}
    item = next((i for i in cfg['workbenches'] if i['id'] == request.get('id')), None)
    if not item:
        raise ValueError('找不到工作檯')
    if action in ('start', 'open'):
        start(item)
        url = item['url']
        if action != 'start':
            if item.get('browser'):
                subprocess.Popen([item['browser'], url], creationflags=NO_WINDOW)
            else:
                os.startfile(url)
    elif action == 'stop':
        stop(item)
    else:
        raise ValueError('不支援的操作')
    return {'ok': True}


if __name__ == '__main__':
    try:
        request = json.loads(sys.stdin.read() or '{}')
        print(json.dumps(execute(request), ensure_ascii=True))
    except Exception as error:
        message = str(error) if isinstance(error, ValueError) else '操作未完成：' + type(error).__name__
        print(json.dumps({'error': message}, ensure_ascii=True))
        sys.exit(1)
