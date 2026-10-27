r"""Run with .venv\Scripts\python.exe test_initial.py; no account access or services."""
import json
import subprocess
import tempfile
from pathlib import Path
from unittest.mock import patch

import backend


def check():
    with tempfile.TemporaryDirectory(prefix='hub initial ') as temporary:
        root = Path(temporary)
        config = root / 'settings.json'
        with patch.object(backend, 'CONFIG', config), \
             patch.object(backend, 'DATA', root / 'data'), \
             patch.object(backend.psutil, 'net_connections', return_value=[]), \
             patch.object(backend, 'quotas', side_effect=AssertionError('account access')), \
             patch.object(backend, 'token_usage', side_effect=AssertionError('session access')), \
             patch.object(backend.subprocess, 'Popen', side_effect=AssertionError('auto launch')):
            assert backend.config()['workbenches'] == []
            state = backend.execute({'action': 'refresh'})
            assert state['services'] == [] and state['quotas'] == {}
            assert state['codex']['task'] == '未啟用'
            launcher = root / '測試 啟動.cmd'
            marker = root / 'marker.txt'
            launcher.write_bytes(b'@echo off\r\necho ready>"%~dp0marker.txt"\r\n')
            request = dict(action='add', name='空白測試', launcher=str(launcher),
                           url='http://127.0.0.1:19099/')
            assert backend.execute(request) == {'ok': True}
            saved = json.loads(config.read_text(encoding='utf-8'))
            assert len(saved['workbenches']) == 1
            assert saved['workbenches'][0]['launcher'] == str(launcher)
            try:
                backend.execute(request)
            except ValueError:
                pass
            else:
                raise AssertionError('duplicate port accepted')
        with patch.object(backend, 'DATA', root / 'data'):
            process = backend.launch_script(launcher, root)
            assert process.wait(timeout=10) == 0
            assert marker.read_text().strip() == 'ready'
    result = subprocess.run([str(backend.ROOT / 'Start-hub.cmd'), '--check'],
                            cwd=tempfile.gettempdir(), capture_output=True, text=True)
    assert result.returncode == 0, result.stdout + result.stderr
    print('PASS: empty state, no account access/auto launch, add, duplicate port, CMD paths, launcher check')


if __name__ == '__main__':
    check()
