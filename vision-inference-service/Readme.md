# Vision Inference Service

FastAPI service for CCTV inference and violation dispatch.

## Core Runtime

1. The service fetches active cameras from the Violation API.
2. `CameraStreamManager` starts one RTSP client per camera.
3. Each client samples frames at the configured camera FPS.
4. `InferenceEngine` runs only the models required by the camera's active SOP rules.
5. `rules.evaluator` maps direct model detections to configured violation labels.
6. `ViolationManager` applies temporal hysteresis and deduplication.
7. New violations are posted back to the Violation API.

## Restaurant PPE Architecture

Restaurant hairnet and mask compliance now uses a dedicated trained YOLOv11 model:

```text
model id:       restaurant-ppe-v1
weights path:   /tmp/models/restaurant-ppe-yolo11.pt
violation labels:
  - no-hairnet
  - no-mask
```

The previous zero-shot hygiene fallback and person-box spatial estimates are no longer used for restaurant PPE. The model must emit violation detections directly. Positive labels like `mask`, `hairnet`, `person`, `back-of-head`, or `compliant` may exist in training/validation, but they are intentionally ignored by the runtime and must not create violation events.

`restaurant-hygiene-v1` is still accepted as a temporary alias so existing camera rules can continue during migration, but new SOP rules should use `restaurant-ppe-v1`.

## Required Environment

```bash
RESTAURANT_PPE_MODEL_IDENTIFIER=restaurant-ppe-v1
RESTAURANT_PPE_MODEL_PATH=/tmp/models/restaurant-ppe-yolo11.pt
RESTAURANT_PPE_IMAGE_SIZE=960
MIN_CONFIDENCE_RESTAURANT_PPE=0.60
```

The Aspire AppHost mounts `./.model_cache/models` to `/tmp/models`, so put the Roboflow-exported YOLOv11 weights at:

```text
surveilance-app-host/AppHost1/.model_cache/models/restaurant-ppe-yolo11.pt
```

## Dataset Requirements

Train the Roboflow project on real restaurant CCTV footage from:

- kitchens
- food prep stations
- serving counters
- dishwashing and storage areas
- entry/exit points where workers move between zones

The dataset must include:

- front-facing, side-facing, and back-facing workers
- masked and unmasked visible faces
- back-of-head examples that are not annotated as mask violations
- hairnets under caps, varied hairstyles, partial occlusion, steam, blur, and poor lighting
- tilted CCTV views, partial bodies, overlapping people, and crowded scenes
- multiple workers per frame

Annotation contract:

- Annotate `no-mask` only when a face or mask-wearing region is actually visible and the model can decide from pixels.
- Do not annotate `no-mask` for back-facing heads.
- Annotate `no-hairnet` around the visible head/hair region when hairnet compliance is missing.
- Prefer tight boxes around the violation evidence area, not full-body person boxes.

## Acceptance Targets

Before enabling production alerts, validate on a holdout set from target cameras:

- per-class precision/recall for `no-hairnet` and `no-mask`
- false positive rate for back-facing people
- performance under motion blur and low light
- multi-person crowded frame behavior
- inference latency at the configured `TARGET_FPS`

Recommended initial gates:

- `no-mask` precision >= 0.95 on back/side/front mixed CCTV validation
- `no-hairnet` precision >= 0.90 and recall >= 0.85
- zero confirmed `no-mask` detections on back-of-head-only validation clips

Tune `MIN_CONFIDENCE_RESTAURANT_PPE` per deployment after validation.
