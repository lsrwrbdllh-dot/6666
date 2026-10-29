"""Build the exact LLamaSharp 0.30 backend for ARMv8-A phones and x64 emulators.

No armv8.7/FP16/dot-product requirement: Galaxy A50 includes older Cortex-A73 cores.
Downloads source only from the pinned upstream commit; weights are not part of APK.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess

COMMIT = '815a2a5915f22ce6a760c676389c5dfe8535c08f'
ROOT = Path(__file__).resolve().parents[1]

def run(*args):
    subprocess.run([str(x) for x in args], check=True)

def verify_elf(path, machine):
    data = path.read_bytes()
    assert data[:5] == b'\x7fELF\x02' and struct.unpack_from('<H', data, 18)[0] == machine, path
    offset = struct.unpack_from('<Q', data, 32)[0]
    size, count = struct.unpack_from('<HH', data, 54)
    for i in range(count):
        p = offset + i*size
        if struct.unpack_from('<I', data, p)[0] == 1:
            assert struct.unpack_from('<Q', data, p+48)[0] >= 16384, f'16 KB pages: {path}'

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--ndk', required=True)
    parser.add_argument('--source', default=str(ROOT/'artifacts/llama-source'))
    parser.add_argument('--cmake', default='cmake')
    parser.add_argument('--abis', nargs='+', default=['arm64-v8a','x86_64'])
    args = parser.parse_args()
    source = Path(args.source).resolve()
    ndk = Path(args.ndk).resolve()
    if not (source/'.git').exists():
        source.mkdir(parents=True, exist_ok=True)
        run('git','init',source)
        run('git','-C',source,'fetch','--depth','1','https://github.com/ggml-org/llama.cpp.git',COMMIT)
        run('git','-C',source,'checkout','--detach','FETCH_HEAD')
    assert subprocess.check_output(['git','-C',str(source),'rev-parse','HEAD']).decode().strip() == COMMIT
    for abi in args.abis:
        if abi not in ['arm64-v8a','x86_64']: raise ValueError('Unsupported ABI')
        build = ROOT/'artifacts/ai-build'/abi
        output = ROOT/'artifacts/ai-native'/abi
        options = ['-DCMAKE_BUILD_TYPE=Release','-DBUILD_SHARED_LIBS=ON',
            f'-DCMAKE_TOOLCHAIN_FILE={ndk}/build/cmake/android.toolchain.cmake',
            f'-DANDROID_ABI={abi}','-DANDROID_PLATFORM=android-26','-DANDROID_STL=c++_shared',
            '-DGGML_NATIVE=OFF','-DGGML_OPENMP=OFF','-DGGML_BACKEND_DL=OFF','-DGGML_CPU_ALL_VARIANTS=OFF',
            '-DGGML_CPU_KLEIDIAI=OFF','-DLLAMA_BUILD_COMMON=OFF','-DLLAMA_BUILD_TESTS=OFF',
            '-DLLAMA_BUILD_EXAMPLES=OFF','-DLLAMA_BUILD_TOOLS=OFF','-DLLAMA_BUILD_SERVER=OFF',
            '-DLLAMA_BUILD_APP=OFF','-DLLAMA_BUILD_MTMD=OFF','-DLLAMA_OPENSSL=OFF',
            '-DCMAKE_SHARED_LINKER_FLAGS=-Wl,-z,max-page-size=16384','-DCMAKE_EXPORT_COMPILE_COMMANDS=ON']
        if abi == 'arm64-v8a': options += ['-DGGML_CPU_ARM_ARCH=armv8-a']
        else: options += ['-DGGML_AVX=OFF','-DGGML_AVX2=OFF','-DGGML_AVX512=OFF','-DGGML_FMA=OFF','-DGGML_F16C=OFF','-DGGML_BMI2=OFF']
        run(args.cmake,'-S',source,'-B',build,*options)
        run(args.cmake,'--build',build,'--parallel','4','--target','llama')
        output.mkdir(parents=True, exist_ok=True)
        strip = ndk/'toolchains/llvm/prebuilt/linux-x86_64/bin/llvm-strip'
        for name in ['libllama.so','libggml.so','libggml-base.so','libggml-cpu.so']:
            candidates = list(build.rglob(name))
            assert candidates, name
            shutil.copy2(candidates[0],output/name)
            run(strip,'--strip-unneeded',output/name)
            verify_elf(output/name,183 if abi=='arm64-v8a' else 62)
        triple = 'aarch64-linux-android' if abi=='arm64-v8a' else 'x86_64-linux-android'
        shutil.copy2(ndk/f'toolchains/llvm/prebuilt/linux-x86_64/sysroot/usr/lib/{triple}/libc++_shared.so',output/'libc++_shared.so')
        verify_elf(output/'libc++_shared.so',183 if abi=='arm64-v8a' else 62)
        (output/'build.json').write_text(json.dumps({'commit':COMMIT,'abi':abi,'baseline':'armv8-a' if abi=='arm64-v8a' else 'x86_64','pageAlignment':16384},indent=2)+'\n')
        print('Verified portable AI backend:',abi)

if __name__=='__main__': main()
