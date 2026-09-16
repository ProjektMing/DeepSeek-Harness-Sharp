#!/usr/bin/env bash
# 在 Linux(含 WSL) 上后台启动 GUI 并截图: 用 Xvfb 虚拟显示, 不抢占任何真实桌面/焦点。
# 用法: bash scripts/verify-gui-linux.sh [--home <dsh home>] [--target <dsh-gui 路径>] [--out <png>] [--keep]
# 依赖: xvfb, x11-utils, imagemagick, 一个窗口管理器(openbox), 以及中文字体(fonts-noto-cjk)。
set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
home_dir="${DSH_HOME:-$HOME/dsh-home}"
target=""
out="$repo_root/artifacts/gui-screenshots/linux-gui.png"
display="${DSH_VERIFY_DISPLAY:-:99}"
keep=0
wait_seconds=18

while [ $# -gt 0 ]; do
  case "$1" in
    --home) home_dir="$2"; shift 2 ;;
    --target) target="$2"; shift 2 ;;
    --out) out="$2"; shift 2 ;;
    --display) display="$2"; shift 2 ;;
    --wait) wait_seconds="$2"; shift 2 ;;
    --keep) keep=1; shift ;;
    *) echo "未知参数: $1" >&2; exit 2 ;;
  esac
done

missing=()
for tool in Xvfb import xwininfo xdpyinfo xprop; do
  command -v "$tool" >/dev/null 2>&1 || missing+=("$tool")
done
command -v openbox >/dev/null 2>&1 || missing+=("openbox")
if [ ${#missing[@]} -gt 0 ]; then
  echo "缺少依赖: ${missing[*]}" >&2
  echo "Ubuntu/Debian: sudo apt-get install -y xvfb openbox x11-utils imagemagick fonts-noto-cjk" >&2
  exit 1
fi

if [ -z "$target" ]; then
  for candidate in \
    "$repo_root/DshGuiHost/bin/Release/net10.0/dsh-gui" \
    "$repo_root/DshGuiHost/bin/Debug/net10.0/dsh-gui"; do
    if [ -x "$candidate" ]; then target="$candidate"; break; fi
  done
fi
if [ -z "$target" ] || [ ! -x "$target" ]; then
  echo "找不到 dsh-gui; 先构建 DshGuiHost 或用 --target 指定" >&2
  exit 1
fi

if ! xdpyinfo -display "$display" >/dev/null 2>&1; then
  Xvfb "$display" -screen 0 1600x1000x24 >/tmp/dsh-xvfb.log 2>&1 &
  sleep 2
fi
if ! xprop -display "$display" -root _NET_SUPPORTING_WM_CHECK >/dev/null 2>&1; then
  DISPLAY="$display" openbox >/dev/null 2>&1 &
  sleep 2
fi

log="$(mktemp)"
if [ -z "${DOTNET_ROOT:-}" ]; then
  for candidate in "$HOME/.dotnet" /usr/share/dotnet /usr/lib/dotnet; do
    if [ -x "$candidate/dotnet" ]; then DOTNET_ROOT="$candidate"; break; fi
  done
fi
export DOTNET_ROOT
DISPLAY="$display" DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 nohup "$target" --home "$home_dir" >"$log" 2>&1 &
gui_pid=$!
sleep "$wait_seconds"

if kill -0 "$gui_pid" 2>/dev/null; then
  echo "GUI 进程存活 (pid=$gui_pid)"
else
  echo "GUI 提前退出" >&2
fi

echo "--- 窗口列表 ---"
DISPLAY="$display" xwininfo -root -tree 2>/dev/null | grep -iE "deepseek|harness|dsh" | head -5 || echo "(没有匹配的窗口)"

mkdir -p "$(dirname "$out")"
if DISPLAY="$display" import -window root "$out"; then
  echo "截图: $out ($(stat -c%s "$out") bytes)"
else
  echo "截图失败" >&2
fi

echo "--- GUI 日志(最后 20 行) ---"
tail -20 "$log"

if [ "$keep" -eq 0 ]; then
  kill "$gui_pid" 2>/dev/null
  wait "$gui_pid" 2>/dev/null
fi
