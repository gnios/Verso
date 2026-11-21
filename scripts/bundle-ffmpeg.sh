#!/usr/bin/env bash
set -euo pipefail

publish_dir=$(cd "$1" && pwd)
rid=$2
version=8.1.3
archive="ffmpeg-$version.tar.xz"
sha256=7138d28c96d9d3e3af4ee3d8cad72741f8ffb40da90c1112235dea3ecd3178a3
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
if ! command -v make >/dev/null; then
  echo 'GNU Make é necessário. No Windows, use o make do MSYS2.' >&2
  exit 1
fi
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT

extension=
platform=()
case "$rid" in
  win-x64)
    extension=.exe
    platform=(--target-os=mingw64 --arch=x86_64 --extra-ldflags=-static)
    ;;
  linux-x64) ;;
  osx-arm64) platform=(--cc=clang --arch=arm64) ;;
  *) echo "RID sem build do FFmpeg: $rid" >&2; exit 1 ;;
esac

cd "$work"
curl -fsSL --retry 3 "https://ffmpeg.org/releases/$archive" -o "$archive"
if command -v sha256sum >/dev/null; then
  printf '%s  %s\n' "$sha256" "$archive" | sha256sum -c -
else
  printf '%s  %s\n' "$sha256" "$archive" | shasum -a 256 -c -
fi
tar -xf "$archive"
cd "ffmpeg-$version"

# Use native codecs and only the outputs needed by AudioLoader.
./configure "${platform[@]}" --disable-autodetect --disable-gpl --disable-nonfree \
  --disable-doc --disable-debug --disable-ffplay --disable-network --disable-avdevice \
  --disable-x86asm --disable-encoders --enable-encoder=pcm_s16le,pcm_f32le \
  --disable-muxers --enable-muxer=pcm_s16le,pcm_f32le
jobs=$(getconf _NPROCESSORS_ONLN 2>/dev/null || sysctl -n hw.logicalcpu)
make -j "$jobs" ffmpeg"$extension" ffprobe"$extension"

# Verify that both PCM formats used by the app can be written.
for format in s16le f32le; do
  head -c 4096 /dev/zero | "./ffmpeg$extension" -nostdin -v error \
    -f s16le -ar 44100 -ac 2 -i pipe:0 -ar 16000 -ac 1 -f "$format" pipe:1 > "$format.pcm"
  test -s "$format.pcm"
done

dest="$publish_dir/engine/ffmpeg"
mkdir -p "$dest"
cp "ffmpeg$extension" "ffprobe$extension" COPYING.LGPLv2.1 LICENSE.md "$dest/"
cp "../$archive" "$script_dir/bundle-ffmpeg.sh" "$dest/"
"$dest/ffmpeg$extension" -version
"$dest/ffprobe$extension" -version
