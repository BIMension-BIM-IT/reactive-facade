# 분석

담당: 분석 · 개발

루버 운영 규칙을 만들고, 그 규칙이 창 일사와 냉난방 에너지에 주는 효과를 Radiance와 EnergyPlus로 계산한 스크립트입니다.

| 폴더 | 내용 |
|---|---|
| `rule/` | 겨울 모드 달력, 규칙 정답표(테스트 벡터), 층별 결과 생성, Ladybug 교차 검증 |
| `radiance/` | 3D 루버 날 모델로 창면 일사 계산 (월별, 11~3월, 층별, 컷오프 민감도, 날씨 다양성, 수평 루버, 보호 유리) |
| `radiance/context/` | 주변 건물 형상 (9층 창 높이 기준 좌표) |
| `energyplus/` | 블라인드 모델로 냉난방 에너지 계산, Radiance 결과에 맞춘 보정 |
| `figures/` | 보고서 그림 생성 (웹 색상) |

## 주요 규칙

- 수직 루버: 컷오프 30/60/45/30°(날짜 보간), 각도 = asin(s·cosθ / w) − |θ|, 날 폭 w = 300 mm, 간격 s = 350 mm
- 겨울 모드: 난방일(7일 평균 12°C 미만)에는 해와 나란하게 열어 햇빛을 들임
- 수평 루버: 프로파일 각 기준, 컷오프 15°

## 실행 환경

- Python 3, ladybug-tools (ladybug, honeybee-energy), Radiance, EnergyPlus, matplotlib
- 기상 데이터: Seoul-Seongnam TMYx (https://climate.onebuilding.org 에서 내려받기)

## 참고

스크립트 안의 파일 경로는 작성자 컴퓨터 기준입니다. 다시 실행하려면 경로를 자기 환경에 맞게 바꿔 주세요.

## 올리지 않는 것

- 시간별 결과 CSV, .oct, eplusout 등 중간 결과
- 기상 파일 원본

## 출처

- 주변 건물 형상(`radiance/context/`): 브이월드 (국토교통부) 공개 데이터와 팀 대지 모델을 바탕으로 작성
- 기상 데이터: Seoul-Seongnam TMYx, climate.onebuilding.org
- 계산 도구: Ladybug Tools, Radiance, EnergyPlus
