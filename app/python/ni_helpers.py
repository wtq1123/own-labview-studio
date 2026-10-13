"""Trusted local NI-helper compatibility API for existing analysis scripts.

This is not an MCP endpoint. Mutation calls must target new isolated test VIs;
the caller owns its COM session and sequencing. No vendor package is loaded.
"""
from pathlib import Path
import hashlib
import json
import re

APP_ROOT = Path(__file__).resolve().parents[1]


def style_catalog():
    data = json.loads((APP_ROOT / 'compat' / 'styles.json').read_text(encoding='utf-8-sig'))
    return dict(zip(data['enum'], data['ring']))


def run_helper(app, name, values, outputs=()):
    if not re.fullmatch(r'[A-Za-z][A-Za-z0-9_]{0,100}', name):
        raise ValueError('Invalid helper name')
    manifest = json.loads((APP_ROOT / 'recipes' / 'helper-provenance.json').read_text(encoding='utf-8-sig'))['helpers']
    if name not in manifest:
        raise ValueError(f'Helper is not pinned in this installation: {name}')
    path = APP_ROOT / 'recipes' / 'helpers' / (name + '.vi')
    expected = manifest[name]['sha256'].lower()
    if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
        raise RuntimeError(f'Helper hash mismatch: {name}')
    vi = app.GetVIReference(str(path), '', False, 0)
    actual = Path(vi.Path).resolve()
    if actual != path.resolve():
        raise RuntimeError(f'NI loaded a helper from another installation: {actual}')
    if hashlib.sha256(actual.read_bytes()).hexdigest() != expected:
        raise RuntimeError('Loaded helper bytes changed')
    for method in ('SetControlValue', 'GetControlValue', 'Run'):
        vi._FlagAsMethod(method)
    for key, value in values.items():
        vi.SetControlValue(key, value)
    vi.Run(False)
    error = vi.GetControlValue('error out')
    if error[0]:
        raise RuntimeError(f'{name}: {error}')
    return {key: vi.GetControlValue(key) for key in outputs}
