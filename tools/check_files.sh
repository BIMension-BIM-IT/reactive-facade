#!/usr/bin/env bash
# 올리면 안 되는 파일 검사
# 사용: bash tools/check_files.sh         커밋 대기 중인 파일 검사
#       bash tools/check_files.sh --all   저장소 전체 검사
set -u
cd "$(git rev-parse --show-toplevel)" || exit 1

LIMIT=$((50 * 1024 * 1024))
WARN=$((10 * 1024 * 1024))
SELF="tools/check_files.sh"

if [ "${1:-}" = "--all" ]; then
  files=$(git ls-files)
else
  files=$(git diff --cached --name-only --diff-filter=ACMR)
fi

# 확인을 거친 예외 파일 (.check-allow 에 한 줄에 하나씩)
allowed() { [ -f .check-allow ] && grep -qxF -- "$1" .check-allow; }

err=0
block() { printf '[막음] %s\n' "$1"; err=1; }
warn()  { printf '[주의] %s\n' "$1"; }

while IFS= read -r f; do
  [ -z "$f" ] || [ ! -f "$f" ] && continue
  [ "$f" = "$SELF" ] && continue
  allowed "$f" && continue

  # 1) 크기
  size=$(wc -c < "$f" | tr -d ' ')
  if [ "$size" -gt "$LIMIT" ]; then
    block "50MB 초과, Releases에 올리세요: $f"
  elif [ "$size" -gt "$WARN" ]; then
    warn "10MB 초과, 꼭 필요한 파일인지 확인: $f"
  fi

  # 2) 올리면 안 되는 파일 종류와 위치
  case "$f" in
    *.env|.env*|*/.env*|*.pem|*.key|*.pfx|*.p12|*secret*|*credential*)
      block "인증 관련 파일: $f" ;;
    *KakaoTalk*|*카카오톡*)
      block "카톡으로 받은 파일: $f" ;;
    *.zip|*.7z|*.rar|*.unitypackage)
      block "압축 파일, Releases에 올리세요: $f" ;;
    Library/*|*/Library/*|Temp/*|*/Temp/*|Builds/*|*/Builds/*)
      block "Unity 프로젝트 폴더, 스크립트만 올리세요: $f" ;;
    *.3dmbak|*.rhl|*.skb|*/~\$*|~\$*)
      block "백업 · 임시 파일: $f" ;;
    *.oct|*.ill|*eplusout.*|*.eso|*.mtr)
      block "분석 중간 결과, 요약만 올리세요: $f" ;;
  esac

  # 3) 내용 속 인증키 · 개인정보 (텍스트 파일만)
  if grep -qIE '[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}' "$f" 2>/dev/null; then
    block "인증키(VWorld 등)로 보이는 문자열: $f"
  fi
  if grep -qIiE '(api[_-]?key|apikey|secret|token|password|passwd)["'"'"']?[[:space:]]*[:=][[:space:]]*["'"'"'][^"'"'"' ]{8,}["'"'"']' "$f" 2>/dev/null; then
    block "키 · 비밀번호가 적힌 것으로 보임: $f"
  fi
  if grep -qIE '(ghp_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|sk-[A-Za-z0-9_-]{20,}|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{35})' "$f" 2>/dev/null; then
    block "토큰으로 보이는 문자열: $f"
  fi
  if grep -qIE '01[016789]-?[0-9]{3,4}-?[0-9]{4}' "$f" 2>/dev/null; then
    warn "전화번호로 보이는 숫자: $f"
  fi
done <<< "$files"

if [ "$err" -ne 0 ]; then
  echo ""
  echo "올리면 안 되는 파일이 있습니다. CONTRIBUTING.md를 확인하고 고친 뒤 다시 커밋하세요."
  exit 1
fi
echo "검사 통과"
