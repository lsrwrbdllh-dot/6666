"""Download the pinned, public Qwen3 quantization and verify its published LFS hash."""
from pathlib import Path
import argparse
import hashlib
import time
import urllib.request

SHA = '9acfc1e001311f34b4252001b626f2e466d592a42065f66571bff3790d4e1b14'
URL = 'https://huggingface.co/bartowski/Qwen_Qwen3-0.6B-GGUF/resolve/60b85c0e3d8fe0f6474f406922a26d12aca4550d/Qwen_Qwen3-0.6B-Q4_K_M.gguf?download=true'
SIZE = 484220320

def valid(path):
    if not path.exists() or path.stat().st_size != SIZE: return False
    with path.open('rb') as file: return hashlib.file_digest(file,'sha256').hexdigest() == SHA

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('destination')
    args = parser.parse_args()
    path = Path(args.destination)
    path.parent.mkdir(parents=True,exist_ok=True)
    if not valid(path):
        stage = path.with_suffix('.download')
        try:
            with urllib.request.urlopen(URL+'&download_session='+str(int(time.time())),timeout=60) as source, stage.open('wb') as target:
                total = 0
                while block := source.read(1024*1024):
                    total += len(block)
                    if total > SIZE: raise ValueError('Unexpected model size')
                    target.write(block)
            if not valid(stage): raise ValueError('Model checksum mismatch')
            stage.replace(path)
        finally: stage.unlink(missing_ok=True)
    print('Verified Qwen3 Q4_K_M model:',path)
