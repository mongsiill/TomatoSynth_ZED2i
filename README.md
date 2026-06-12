# Tomato Farm Unity Dataset Generator

Unity HDRP와 Unity Perception을 사용해서 토마토 온실 환경의 ZED 2i 유사 스테레오 카메라 데이터를 생성하는 프로젝트입니다.

생성되는 데이터에는 RGB, depth, 2D bounding box, 3D bounding box, instance segmentation, amodal mask, 그리고 토마토 꼭지를 포함한 별도 depth가 포함됩니다.

<https://www.notion.so/TomatoSynth-37a3f44afd34802a95b2cc7d951d948d?source=copy_link>

변경 사항 참고 노션

## 프로젝트 열기

1. 이 저장소를 clone 합니다.

   ```bash
   git clone <repository-url>
   ```

2. Unity Hub를 실행합니다.
3. `Add` 또는 `Add project from disk`를 선택합니다.
4. 저장소 루트가 아니라 아래 Unity 프로젝트 폴더를 선택합니다.

   ```text
   V2.0 Unity (copy)
   ```

5. Unity Editor 버전은 아래 버전을 사용합니다.

   ```text
   Unity 2022.3.17f1
   ```

6. 처음 열 때 Unity가 `Packages/manifest.json` 기준으로 패키지를 복원합니다. HDRP, Perception, Recorder 등의 패키지 import가 끝날 때까지 기다립니다.

## 실행 방법

1. Unity에서 `Assets/Scenes/MainScene.unity`를 엽니다.
2. Hierarchy에서 `ZED 2i` 오브젝트를 확인합니다.
3. 필요하면 `ZED 2i` 오브젝트의 `Zed2iStereoCameraRig` 컴포넌트에서 `Setup ZED2i Stereo Rig` context menu를 실행합니다.
4. Play 버튼을 누르면 데이터 수집이 시작됩니다.
5. 기본 출력 위치는 Unity 프로젝트 폴더 아래입니다.

   ```text
   V2.0 Unity (copy)/dataset/
   ```

`dataset/`, `Library/`, `Logs/`, `Temp/`, `.sln`, `.csproj` 파일은 git에 포함하지 않습니다. 새로 생성한 dataset은 필요할 때 별도로 백업하거나 공유해야 합니다.

## 주요 산출물

Unity Perception 기본 출력은 `dataset/solo*/sequence.0/` 아래에 저장됩니다.

대표 파일:

```text
stepN.camera.png
stepN.camera.Depth.exr
stepN.frame_data.json
```

`stepN.frame_data.json`에는 다음 annotation이 들어갑니다.

```text
bounding box
bounding box 3D
instance segmentation
Depth
```

커스텀 exporter 출력:

```text
dataset/left_amodal_mask/annotations_000000.json
dataset/left_depth_with_body/stepN.camera.DepthWithBody.exr
```

## ZED 2i Rig 주요 파라미터

`ZED 2i` 오브젝트의 `Zed2iStereoCameraRig` 컴포넌트에서 설정합니다.

| 파라미터 | 기본값 | 설명 |
| --- | ---: | --- |
| `datasetRoot` | `dataset` | Unity 프로젝트 폴더 기준 dataset 출력 폴더입니다. |
| `imageWidth` | `1920` | RGB, depth, mask 출력 너비입니다. |
| `imageHeight` | `1080` | RGB, depth, mask 출력 높이입니다. |
| `baselineMeters` | `0.12` | Left RGB와 Right RGB 사이 baseline입니다. ZED 2i 유사 stereo 기준입니다. |
| `horizontalFovDeg` | `110` | 카메라 horizontal FOV입니다. intrinsics의 `fx` 계산에 사용됩니다. |
| `verticalFovDeg` | `70` | 카메라 vertical FOV입니다. Unity camera `fieldOfView`와 `fy` 계산에 사용됩니다. |
| `nearClip` | `0.3` | 카메라 near clipping plane입니다. |
| `rgbFarClip` | `100` | RGB 카메라 far clipping plane입니다. 배경/온실까지 보이도록 GT보다 크게 둡니다. |
| `gtFarClip` | `20` | GT 카메라 far clipping plane입니다. depth, bbox, segmentation 기준이 됩니다. |
| `depthMinMeters` | `0.3` | dataset metadata용 유효 depth 최소값입니다. |
| `depthMaxMeters` | `20` | dataset metadata용 유효 depth 최대값입니다. |
| `autoAddPerceptionCamera` | `true` | 카메라에 `PerceptionCamera` 컴포넌트를 자동 추가/확인합니다. |
| `perceptionStartAtFrame` | `1` | Perception capture 시작 frame입니다. |

`Setup ZED2i Stereo Rig`를 실행하면 다음 카메라가 구성됩니다.

```text
LeftCamera_RGB
RightCamera_RGB
LeftCamera_GT
```

`camera_params.json`도 함께 생성되며, `fx`, `fy`, `cx`, `cy`, baseline, FOV, clip range가 기록됩니다.

## 캡처 이동 파라미터

`ZED 2i` 오브젝트의 `Zed2iAutoLineCaptureController` 컴포넌트에서 설정합니다.

| 파라미터 | 기본값 | 설명 |
| --- | ---: | --- |
| `splitZ` | `70` | 시작 위치가 위/아래 라인 중 어디인지 판단하는 Z 기준입니다. |
| `lowerZLimit` | `-55` | 아래 방향으로 이동하다가 라인 전환을 시작하는 Z 한계입니다. |
| `lineSpacingX` | `9.1` | 일반 라인 전환 시 X 방향 이동량입니다. |
| `fifthLineSpacingX` | `13.8` | 5번째 라인 전환 시 X 방향 이동량입니다. |
| `maxLineMoves` | `10` | 최대 라인 전환 횟수입니다. |
| `stoppedFramesBeforeMove` | `2` | 라인 전환 전 정지 frame 수입니다. |
| `settleFramesAfterMove` | `6` | 라인 전환 후 안정화 frame 수입니다. |
| `pausePerceptionDuringMove` | `true` | 라인 전환 중 Perception capture를 일시 정지합니다. |
| `stopVelocityOnComplete` | `true` | 완료 시 카메라 이동 속도를 0으로 만듭니다. |
| `stopPlayModeOnComplete` | `true` | 완료 시 Play Mode를 종료합니다. |

라인이 바뀔 때 Perception SOLO 출력 폴더는 `solo`, `solo_1`, `solo_2`처럼 분리됩니다.

## 카메라 이동 조작

`CameraCmdVelKeyboardController`는 `ZED 2i` 오브젝트의 이동 속도를 제어합니다.

| 키 | 동작 |
| --- | --- |
| `W` / `S` | local Z 속도 증가/감소 |
| `D` / `A` | local X 속도 증가/감소 |
| `E` / `Q` | local Y 속도 증가/감소 |
| `Right` / `Left` | yaw 속도 증가/감소 |
| `Up` / `Down` | pitch 속도 증가/감소 |
| `Space` | 선속도/각속도 정지 |

주요 파라미터:

```text
linearStep
maxLinearSpeed
yawStepDeg
pitchStepDeg
maxAngularSpeedDeg
linearCmd
angularCmd
```

## 토마토 GT 처리 기준

토마토 mesh는 꼭지와 알맹이가 같은 object 안의 submesh/material로 들어 있습니다. 이 프로젝트는 GT annotation에서 꼭지 material인 `Body`를 제외하기 위해 runtime에 fruit-only proxy를 만듭니다.

처리 기준:

```text
RGB: 원본 토마토를 렌더링합니다.
2D bbox / 3D bbox / instance segmentation / amodal mask: Body submesh가 제거된 fruit-only proxy 기준입니다.
Depth: Perception 기본 Depth는 GT 카메라 기준입니다.
DepthWithBody: 원본 토마토 Body까지 포함한 별도 EXR depth입니다.
```

관련 layer:

```text
OriginalTomatoLayer = 29
FruitOnlyGtLayer = 30
```

현재 수집 dataset 기준 토마토 3D bbox 크기는 대략 7.4cm에서 12.4cm 범위이며, 평균은 약 10cm에서 11cm 정도입니다.

## 커스텀 Exporter

### Amodal Mask

컴포넌트:

```text
Zed2iCocoRleMaskJsonExporter
```

주요 파라미터:

```text
OutputFolderName = left_amodal_mask
framesBetweenCaptures = 1
startFrame = 2
```

COCO RLE 형식 JSON을 frame별로 저장합니다.

### Depth With Body

컴포넌트:

```text
Zed2iDepthWithBodyExrExporter
```

주요 파라미터:

```text
OutputFolderName = left_depth_with_body
DepthAnnotationName = DepthWithBody
framesBetweenCaptures = 1
startFrame = 2
useTomatoLayerSplit = true
originalTomatoLayer = 29
fruitOnlyGtLayer = 30
```

Unity Perception의 depth channel과 같은 linear meter depth 경로를 사용하지만, depth 전용 culling을 다시 수행해서 원본 토마토 body까지 포함합니다.

## 주의 사항

- Unity 프로젝트 폴더는 `V2.0 Unity (copy)`입니다. Unity Hub에서 저장소 루트가 아니라 이 폴더를 열어야 합니다.
- 생성된 dataset은 gitignore 대상입니다.
- `DepthWithBody.exr`는 별도 파일로 저장되며 `frame_data.json` annotation에는 추가되지 않습니다.
- 2D bbox는 화면에 실제로 보이는 pixel 영역 기준이고, 3D bbox는 visible object의 fruit-only proxy mesh 전체 기준입니다.
- 3D bbox의 `translation`, `size`, `rotation`은 Unity Perception `BoundingBox3DLabeler` 출력 형식을 따릅니다.
