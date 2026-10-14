# 반응형 외부 루버 검토 도구

**BIMension** · BIM-IT 과정 팀 프로젝트 (2026)

1784 사옥의 반응형 외부 루버가 시간과 계절에 따라 어떻게 열리고 닫히는지, 그리고 창으로 들어오는 일사와 냉난방 에너지에 어떤 영향을 주는지 검토하는 도구입니다. 웹에서 주소를 넣으면 다른 건물에도 같은 규칙을 적용해 볼 수 있습니다.

- 웹 도구: https://emmiart7-png.github.io/bimention-bim-it-teamproject/
- 보고서: [docs/](docs/)

## 주요 결과 (9층 기준)

| 지표 | 결과 |
|---|---|
| 냉방기 창 일사 감소 (반응형) | 서 70% · 동 66% · 남 65% |
| 난방기 일사 손실 (겨울 모드) | 남 17% · 동 32% · 서 34% |
| 실내 직달 햇빛 면적 (하지 15:30) | 73% → 16% |
| 규칙 검증 | 870건 일치 |

## 폴더 구조

| 폴더 | 내용 |
|---|---|
| `docs/` | 보고서, 개발계획서, 발표 자료 (PDF) |
| `web/` | 웹 도구 안내 (코드는 위 웹 저장소에 있음) |
| `unity/` | Unity 스크립트, 빌드 가이드 |
| `analysis/` | Radiance · EnergyPlus 분석 스크립트 |
| `modeling/` | 외피 · 루버 모델 (Rhino, Grasshopper) |
| `interior/` | 대표 실 모델 (SketchUp) |
| `results/` | 요약 CSV, 최종 그림 |
| `tools/` | 올리기 전 검사 스크립트 |

## 팀 구성

| 역할 | 담당 업무 | GitHub |
|---|---|---|
| 총괄 · 웹 · 보고서 | 웹 검토 도구, 웹-Unity 연동, 경제성, 보고서 · 발표 자료 | @ |
| 분석 · 개발 | 운영 규칙, Radiance · EnergyPlus 분석, Unity 스크립트, 검증 | @ |
| 외피 모델링 | 1784 외피, 수직 · 수평 루버 모델링 | @ |
| 대지 · 시각화 | 대지 · 주변 건물, Unity 장면, WebGL 빌드 | @ |
| 실내 구현 | 대표 실 모델, 조도 · 마감 반사율 자료 | @ |

## 처음 한 번만 하는 설정

저장소를 내려받은(Clone) 뒤, 아래 명령을 한 번 실행하세요. 커밋할 때마다 올리면 안 되는 파일이 있는지 자동으로 검사합니다.

```
git config core.hooksPath .githooks
```

GitHub Desktop을 쓴다면 메뉴의 **Repository → Open in Command Prompt**(또는 Git Bash)에서 실행하면 됩니다.

**올리기 전에 반드시 [CONTRIBUTING.md](CONTRIBUTING.md)를 읽어 주세요.**
