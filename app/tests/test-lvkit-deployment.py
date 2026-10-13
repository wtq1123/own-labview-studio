"""Verify an installed lvkit stdio server against a finite generated VI."""
import argparse
import asyncio
import hashlib
import json
import os
from pathlib import Path
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


async def verify(executable, vi_path, report_path):
    path = Path(vi_path).resolve(strict=True)
    before = hashlib.sha256(path.read_bytes()).hexdigest()
    params = StdioServerParameters(command=executable, env={**os.environ, 'PYTHONUTF8': '1', 'PYTHONIOENCODING': 'utf-8'}, cwd=str(path.parent))
    async with stdio_client(params) as (reader, writer):
        async with ClientSession(reader, writer) as session:
            await session.initialize()
            tools = await session.list_tools()
            names = [tool.name for tool in tools.tools]
            assert 'read_vi' in names, names
            result = await session.call_tool('read_vi', {'vi_path': str(path)})
            assert not getattr(result, 'is_error', getattr(result, 'isError', False)), result
            assert result.content, 'No VI result'
    assert before == hashlib.sha256(path.read_bytes()).hexdigest(), 'Source changed'
    report = {'passed': True, 'tool_names': names, 'vi_path': str(path), 'source_sha256': before, 'source_unchanged': True, 'source_executed': False, 'result_bytes': sum(len(getattr(item, 'text', '')) for item in result.content)}
    Path(report_path).write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('executable')
    parser.add_argument('vi_path')
    parser.add_argument('report_path')
    args = parser.parse_args()
    asyncio.run(verify(args.executable, args.vi_path, args.report_path))
