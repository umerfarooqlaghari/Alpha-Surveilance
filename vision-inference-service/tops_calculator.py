"""
tops_calculator.py — TOPS (Tera-Operations Per Second) estimator
=================================================================
Estimates the compute demand of the Alpha Surveillance vision pipeline
based on number of cameras, model variant, FPS, resolution, and
concurrent-rule fan-out.

Usage:
    python tops_calculator.py                   # uses defaults from config
    python tops_calculator.py --cameras 20 --fps 5 --model yolo11m
    python tops_calculator.py --help
"""

import argparse
import math

# ─── Model GFLOP table ───────────────────────────────────────────────────────
# GFLOPs per single forward pass at imgsz=640×640 (FP32 equivalent).
# Source: Ultralytics docs + published benchmarks.
# INT8 quantisation roughly halves effective compute cost vs FP32.
MODEL_GFLOPS: dict[str, float] = {
    # YOLOv11 family
    "yolo11n":   6.5,
    "yolo11s":  21.5,
    "yolo11m":  68.0,
    "yolo11l": 107.0,
    "yolo11x": 194.0,
    # YOLOv8 family (used for hygiene-monitor / yolov8s-worldv2)
    "yolov8n":  8.7,
    "yolov8s":  28.6,
    "yolov8m":  78.9,
    "yolov8l": 165.2,
    "yolov8x": 257.8,
    # YOLO-World (zero-shot, heavier backbone)
    "yolov8s-worldv2": 47.0,
    "yolov8m-worldv2": 89.0,
    # Legacy / transformer-based fallbacks
    "yolos-tiny":      9.6,    # HuggingFace YOLOS-tiny
    "owlvit-base":   170.0,    # OWL-ViT base-patch32 (heavy!)
    # Heavy transformer detectors — 6 GB-class checkpoints
    "owlvit-large":    420.0,   # OWL-ViT large (~1.7 GB FP32)
    "grounding-dino-large": 600.0,  # Grounding DINO Large (~1.5 GB FP32)
    "custom-6gb-detector":  800.0,  # placeholder for a 6 GB-class model
}

# Approximate disk size (MB, FP32) — for "GB per model" context
MODEL_SIZE_MB_FP32: dict[str, float] = {
    "yolo11n":     11,    "yolo11s":    37,    "yolo11m":   78,
    "yolo11l":    103,    "yolo11x":   220,
    "yolov8n":     12,    "yolov8s":    44,    "yolov8m":   99,
    "yolov8l":    166,    "yolov8x":   260,
    "yolov8s-worldv2":     56,
    "yolov8m-worldv2":    104,
    "yolos-tiny":  26,
    "owlvit-base": 600,
    "owlvit-large":      1700,
    "grounding-dino-large": 1500,
    "custom-6gb-detector":  6000,
}

# ─── Quantisation multipliers ─────────────────────────────────────────────────
QUANT_MULTIPLIER: dict[str, float] = {
    "fp32": 1.00,
    "fp16": 0.50,   # same ops, each op is half the arithmetic bandwidth
    "int8": 0.25,   # typical HW TOPS rating for INT8 is 4× that of FP32
    "int4": 0.125,
}

# ─── Resolution scaling ───────────────────────────────────────────────────────
# YOLO FLOP cost scales roughly as (H×W) / (640×640).
# Our pipeline pre-resizes to 640×480 then YOLO letterboxes to 640×640.
REFERENCE_PIXELS = 640 * 640   # model benchmark baseline

# ─── Current pipeline defaults (from config.py + main.py) ────────────────────
DEFAULTS = {
    "cameras":     10,        # typical tenant deployment
    "fps":          1.0,      # TARGET_FPS default in config.py
    "input_w":    640,        # cv2.resize target in main.py
    "input_h":    480,        # cv2.resize target in main.py
    "main_model": "yolo11n",  # loaded as 'human-detection-v1'
    "aux_model":  "yolov8s-worldv2",  # loaded as 'hygiene-monitor'
    "quant":      "fp32",     # no quantisation applied yet
    "rules_per_camera": 3,    # average active rules triggering inference per camera
    "overhead_factor": 1.15,  # 15% overhead: pre/post-processing, Python, I/O
}


def estimate_tops(
    cameras: int,
    fps: float,
    input_w: int,
    input_h: int,
    main_model: str,
    aux_model: str,
    quant: str,
    rules_per_camera: int,
    overhead_factor: float,
    aux_trigger_rate: float = 1.0,
) -> dict:
    """
    Returns a detailed TOPS breakdown for one inference tick.

    Formula per model per frame:
        effective_gflops = model_gflops
                         × (input_pixels / reference_pixels)   # resolution scaling
                         × quant_multiplier                     # precision
                         × overhead_factor                      # system overhead

    Total TOPS = Σ(effective_gflops × fps × cameras_served) / 1000
    """
    if main_model not in MODEL_GFLOPS:
        raise ValueError(f"Unknown main model '{main_model}'. Choices: {list(MODEL_GFLOPS)}")
    if aux_model not in MODEL_GFLOPS:
        raise ValueError(f"Unknown aux model '{aux_model}'. Choices: {list(MODEL_GFLOPS)}")
    if quant not in QUANT_MULTIPLIER:
        raise ValueError(f"Unknown quant '{quant}'. Choices: {list(QUANT_MULTIPLIER)}")

    input_pixels   = input_w * input_h
    res_scale      = input_pixels / REFERENCE_PIXELS
    q_mult         = QUANT_MULTIPLIER[quant]

    # Main model — runs once per frame per camera (human/person detection)
    gflops_main    = MODEL_GFLOPS[main_model] * res_scale * q_mult * overhead_factor
    tops_main      = (gflops_main * fps * cameras) / 1_000

    # Aux model (YOLO-World / hygiene) — runs per rule per camera
    # rules_per_camera represents how many rule evaluations use the aux model
    # aux_trigger_rate: fraction of frames that actually invoke the aux model
    #   1.0  = every frame (worst case, both models always run)
    #   0.2  = aux runs only on 20% of frames (e.g., gated by main detection)
    gflops_aux     = MODEL_GFLOPS[aux_model] * res_scale * q_mult * overhead_factor
    tops_aux       = (gflops_aux * fps * cameras * rules_per_camera * aux_trigger_rate) / 1_000

    tops_total     = tops_main + tops_aux

    return {
        "cameras":              cameras,
        "fps":                  fps,
        "resolution":           f"{input_w}×{input_h}",
        "main_model":           main_model,
        "aux_model":            aux_model,
        "quantisation":         quant,
        "rules_per_camera":     rules_per_camera,
        "res_scale":            round(res_scale, 4),
        "quant_multiplier":     q_mult,
        "gflops_main_per_frame":  round(gflops_main, 3),
        "gflops_aux_per_frame":   round(gflops_aux, 3),
        "tops_main":            round(tops_main, 4),
        "tops_aux":             round(tops_aux, 4),
        "tops_total":           round(tops_total, 4),
        # Reference hardware headroom
        "jetson_orin_nx_16g_tops":  100,   # INT8 TOPS
        "jetson_agx_orin_tops":     275,   # INT8 TOPS
        "a10g_gpu_tops":            250,   # INT8 TOPS (AWS g5)
        "headroom_vs_orin_nx_%": round((1 - tops_total / (100 * (1 if quant == "int8" else 4))) * 100, 1),
    }


def sensitivity_table(base: dict) -> None:
    """Print a cameras × fps sensitivity matrix."""
    cam_range = [1, 5, 10, 20, 50, 100, 200, 500]
    fps_range = [1, 2, 5, 10, 25, 30]

    print(f"\n{'TOPS sensitivity: cameras × fps':^72}")
    print(f"Model: {base['main_model']} + {base['aux_model']} | "
          f"Quant: {base['quant']} | "
          f"Rules/cam: {base['rules_per_camera']}")
    print("-" * 72)

    # Header
    header = f"{'Cameras':>10}" + "".join(f"  {f:>5}fps" for f in fps_range)
    print(header)
    print("-" * len(header))

    for cams in cam_range:
        row = f"{cams:>10}"
        for fps in fps_range:
            r = estimate_tops(
                cameras=cams,
                fps=fps,
                input_w=base["input_w"],
                input_h=base["input_h"],
                main_model=base["main_model"],
                aux_model=base["aux_model"],
                quant=base["quant"],
                rules_per_camera=base["rules_per_camera"],
                overhead_factor=base["overhead_factor"],
            )
            val = r["tops_total"]
            # Flag values exceeding Jetson Orin NX headroom
            flag = "⚠" if val > 25 else ("🔴" if val > 100 else " ")
            row += f"  {val:>6.3f}{flag}"
        print(row)

    print("\nLegend: ⚠ > 25 TOPS (single Jetson edge limit)  🔴 > 100 TOPS (full Orin NX limit)")


def main() -> None:
    p = argparse.ArgumentParser(description="Alpha Surveillance TOPS estimator")
    p.add_argument("--cameras",          type=int,   default=DEFAULTS["cameras"])
    p.add_argument("--fps",              type=float, default=DEFAULTS["fps"])
    p.add_argument("--input-w",          type=int,   default=DEFAULTS["input_w"])
    p.add_argument("--input-h",          type=int,   default=DEFAULTS["input_h"])
    p.add_argument("--model",            type=str,   default=DEFAULTS["main_model"],
                   choices=list(MODEL_GFLOPS), dest="main_model")
    p.add_argument("--aux-model",        type=str,   default=DEFAULTS["aux_model"],
                   choices=list(MODEL_GFLOPS))
    p.add_argument("--quant",            type=str,   default=DEFAULTS["quant"],
                   choices=list(QUANT_MULTIPLIER))
    p.add_argument("--rules-per-camera", type=int,   default=DEFAULTS["rules_per_camera"])
    p.add_argument("--overhead",         type=float, default=DEFAULTS["overhead_factor"])
    p.add_argument("--aux-trigger-rate", type=float, default=1.0,
                   help="Fraction of frames that invoke the aux model (1.0=every frame, 0.2=gated)")
    p.add_argument("--sensitivity",      action="store_true",
                   help="Print full cameras × fps sensitivity table")
    args = p.parse_args()

    result = estimate_tops(
        cameras=args.cameras,
        fps=args.fps,
        input_w=args.input_w,
        input_h=args.input_h,
        main_model=args.main_model,
        aux_model=args.aux_model,
        quant=args.quant,
        rules_per_camera=args.rules_per_camera,
        overhead_factor=args.overhead,
        aux_trigger_rate=args.aux_trigger_rate,
    )

    print("\n" + "=" * 60)
    print("  Alpha Surveillance — TOPS Requirement Estimate")
    print("=" * 60)
    print(f"  Cameras             : {result['cameras']}")
    print(f"  FPS (per camera)    : {result['fps']}")
    print(f"  Input resolution    : {result['resolution']} → 640×640 (YOLO letterbox)")
    print(f"  Resolution scale    : {result['res_scale']}  (vs 640×640 baseline)")
    print(f"  Main model          : {result['main_model']}  ({MODEL_GFLOPS[result['main_model']]} GFLOPs/frame raw)")
    print(f"  Aux model           : {result['aux_model']}  ({MODEL_GFLOPS[result['aux_model']]} GFLOPs/frame raw)")
    print(f"  Quantisation        : {result['quantisation']}  (×{result['quant_multiplier']} FLOP cost)")
    print(f"  Rules/camera        : {result['rules_per_camera']}")
    print(f"  Overhead factor     : ×{args.overhead}")
    print("-" * 60)
    print(f"  GFLOPs/frame (main) : {result['gflops_main_per_frame']}")
    print(f"  GFLOPs/frame (aux)  : {result['gflops_aux_per_frame']}")
    print("-" * 60)
    print(f"  TOPS — main model   : {result['tops_main']:.4f} TOPS")
    print(f"  TOPS — aux model    : {result['tops_aux']:.4f} TOPS")
    print(f"  ► TOTAL TOPS        : {result['tops_total']:.4f} TOPS")
    print("=" * 60)
    print("  Reference hardware:")
    print(f"    Jetson Orin NX 16GB : {result['jetson_orin_nx_16g_tops']} TOPS (INT8)")
    print(f"    Jetson AGX Orin     : {result['jetson_agx_orin_tops']} TOPS (INT8)")
    print(f"    AWS g5 (A10G)       : {result['a10g_gpu_tops']} TOPS (INT8)")
    print("=" * 60)

    if args.sensitivity:
        sensitivity_table(vars(args) | DEFAULTS)


if __name__ == "__main__":
    main()
