"""
video_annotator.py
Overlay renderer + streaming H.264 writer for /analyze's annotated review video.

/analyze accepts a video, runs every frame through the production pipeline, and
(when rendering is enabled) emits a single MP4 with the pipeline's own reasoning
drawn on top: every raw detection, which of them passed rule evaluation, and
which actually fired a violation through the state machine. That three-tier view
is the point — a QA reviewer needs to see the detection the model made AND the
reason it did or did not become a violation, not just the final boxes.

Frames are piped straight into FFmpeg as they are produced. A 300-frame 1080p
video buffered in memory would be ~1.8GB, so nothing is accumulated: each frame
is drawn, written, and dropped.
"""
import os
import cv2
import logging
import subprocess
import tempfile
import numpy as np
from typing import Any, Dict, List, Optional, Sequence, Tuple

logger = logging.getLogger(__name__)

# BGR. Tiers are colour-coded so the overlay is readable at a glance:
#   steel   = the detector saw it
#   amber   = it passed rule evaluation (but the state machine may suppress it)
#   red     = it fired a violation this frame
COLOR_DETECTION = (140, 120, 90)
COLOR_VALIDATED = (60, 170, 235)
COLOR_ACTION_NEW = (0, 0, 240)
COLOR_ACTION_UPDATE = (70, 70, 225)
COLOR_HUD_BG = (24, 20, 16)
COLOR_TEXT = (255, 255, 255)
COLOR_MUTED = (190, 190, 190)

FONT = cv2.FONT_HERSHEY_SIMPLEX

# OpenCV's Hershey fonts are ASCII-only: cv2.putText renders anything above
# U+007F as '?'. A '\u00b7' separator silently became '??' in every label on
# every rendered frame. Everything drawn goes through this first.
_ASCII_FALLBACK = {
    "\u00b7": "|", "\u2022": "*", "\u2013": "-", "\u2014": "-",
    "\u2018": "'", "\u2019": "'", "\u201c": '"', "\u201d": '"',
    "\u2192": "->", "\u00b0": "deg", "\u00d7": "x", "\u2026": "...",
}


def _ascii(text: str) -> str:
    """Makes a label safe for cv2.putText; unmapped non-ASCII becomes '?'."""
    out = str(text)
    for bad, good in _ASCII_FALLBACK.items():
        out = out.replace(bad, good)
    return out.encode("ascii", "replace").decode("ascii")


def _box_of(item: Any) -> Optional[Dict]:
    """Accepts a detection dict or a violation action and returns its box dict."""
    if not isinstance(item, dict):
        return None
    box = item.get("box")
    if isinstance(box, dict):
        return box
    meta = item.get("Metadata")
    if isinstance(meta, dict) and isinstance(meta.get("box"), dict):
        return meta["box"]
    return None


def _label_of(item: Dict) -> str:
    meta = item.get("Metadata") if isinstance(item.get("Metadata"), dict) else {}
    return str(item.get("label") or meta.get("label") or item.get("Label") or "object")


def _score_of(item: Dict) -> float:
    meta = item.get("Metadata") if isinstance(item.get("Metadata"), dict) else {}
    raw = item.get("score", meta.get("score", item.get("Score", 0.0)))
    try:
        return float(raw or 0.0)
    except (TypeError, ValueError):
        return 0.0


def _track_of(item: Dict) -> Optional[Any]:
    meta = item.get("Metadata") if isinstance(item.get("Metadata"), dict) else {}
    return item.get("track_id", meta.get("track_id", item.get("TrackId")))


def _key_of(item: Dict) -> Optional[Tuple]:
    """
    Identity for tier de-duplication. A detection that fired a violation appears
    in all three lists; it must be drawn once, at its highest tier.
    """
    box = _box_of(item)
    if not box:
        return None
    try:
        coords = (
            round(float(box.get("xmin", 0))),
            round(float(box.get("ymin", 0))),
            round(float(box.get("xmax", 0))),
            round(float(box.get("ymax", 0))),
        )
    except (TypeError, ValueError):
        return None
    return (_label_of(item), coords)


def _clamp_box(box: Dict, w: int, h: int) -> Optional[Tuple[int, int, int, int]]:
    try:
        xmin = int(max(0, min(w - 2, float(box.get("xmin", 0)))))
        ymin = int(max(0, min(h - 2, float(box.get("ymin", 0)))))
        xmax = int(max(xmin + 1, min(w - 1, float(box.get("xmax", w)))))
        ymax = int(max(ymin + 1, min(h - 1, float(box.get("ymax", h)))))
    except (TypeError, ValueError):
        return None
    return xmin, ymin, xmax, ymax


def _draw_box(
    frame: np.ndarray,
    box: Dict,
    text: str,
    color: Tuple[int, int, int],
    thickness_scale: float = 1.0,
    label_below: bool = False,
) -> None:
    h, w = frame.shape[:2]
    clamped = _clamp_box(box, w, h)
    if clamped is None:
        return
    xmin, ymin, xmax, ymax = clamped

    thickness = max(1, int(min(w, h) / 500 * thickness_scale))
    cv2.rectangle(frame, (xmin, ymin), (xmax, ymax), color, thickness)

    if not text:
        return
    text = _ascii(text)

    font_scale = max(0.45, min(w, h) / 1250.0)
    font_thickness = max(1, int(font_scale * 2))
    (tw, th), _ = cv2.getTextSize(text, FONT, font_scale, font_thickness)

    if label_below or ymin - th - 6 < 0:
        banner_top, banner_bottom = ymax, min(h - 1, ymax + th + 6)
        text_y = banner_bottom - 3
    else:
        banner_top, banner_bottom = max(0, ymin - th - 6), ymin
        text_y = banner_bottom - 3

    cv2.rectangle(frame, (xmin, banner_top), (min(w - 1, xmin + tw + 8), banner_bottom), color, -1)
    cv2.putText(frame, text, (xmin + 4, text_y), FONT, font_scale, COLOR_TEXT, font_thickness, cv2.LINE_AA)


def _draw_hud(
    frame: np.ndarray,
    lines: Sequence[Tuple[str, Tuple[int, int, int]]],
    anchor: str = "top",
) -> None:
    """Draws a translucent bar with coloured text segments."""
    h, w = frame.shape[:2]
    font_scale = max(0.5, min(w, h) / 1100.0)
    thickness = max(1, int(round(font_scale * 2)))
    pad = max(5, int(min(w, h) / 140))

    lines = [(_ascii(t), c) for t, c in lines]
    sizes = [cv2.getTextSize(t, FONT, font_scale, thickness)[0] for t, _ in lines]
    if not sizes:
        return
    text_h = max(s[1] for s in sizes)
    total_w = min(w, sum(s[0] for s in sizes) + pad * (len(sizes) + 1))
    bar_h = min(h, text_h + pad * 2)

    top = 0 if anchor == "top" else max(0, h - bar_h)
    # Blend only the bar's own region. Copying the whole frame twice per frame
    # (once per HUD) costs ~12MB of allocation per 1080p frame and shows up as
    # real time across a 300-frame render.
    roi = frame[top:top + bar_h, 0:total_w]
    if roi.size:
        cv2.addWeighted(np.full_like(roi, COLOR_HUD_BG), 0.72, roi, 0.28, 0, roi)

    x = pad
    for (text, color), (tw, _) in zip(lines, sizes):
        cv2.putText(frame, text, (x, top + bar_h - pad), FONT, font_scale, color, thickness, cv2.LINE_AA)
        x += tw + pad


def draw_analysis_overlay(
    frame: np.ndarray,
    detections: Optional[List[Dict]] = None,
    validated: Optional[List[Dict]] = None,
    actions: Optional[List[Dict]] = None,
    *,
    frame_index: int = 0,
    media_time_sec: float = 0.0,
    camera_label: str = "",
    violations_so_far: int = 0,
) -> np.ndarray:
    """
    Draws the three-tier overlay onto ``frame`` IN PLACE and returns it.

    Tiers, lowest to highest — each subject is drawn once, at its highest tier:
      steel  detection   the detector saw it
      amber  validated   it passed rule evaluation
      red    action      it fired a violation this frame (NEW or UPD)
    """
    detections = detections or []
    validated = validated or []
    actions = actions or []
    h, w = frame.shape[:2]

    action_keys = {k for k in (_key_of(a) for a in actions) if k}
    validated_keys = {k for k in (_key_of(v) for v in validated) if k}

    # Tier 1 — raw detections that never progressed.
    for det in detections:
        key = _key_of(det)
        if key is None or key in action_keys or key in validated_keys:
            continue
        box = _box_of(det)
        if not box:
            continue
        _draw_box(frame, box, f"{_label_of(det)} {_score_of(det):.2f}", COLOR_DETECTION, 0.8)

    # Tier 2 — passed the rules but the state machine did not act (cooldown,
    # hysteresis, dwell not yet met). This is the tier that explains a "why
    # didn't it fire?" question.
    for viol in validated:
        key = _key_of(viol)
        if key is None or key in action_keys:
            continue
        box = _box_of(viol)
        if not box:
            continue
        tid = _track_of(viol)
        prefix = f"ID:{tid} " if tid is not None else ""
        _draw_box(frame, box, f"{prefix}{_label_of(viol)} {_score_of(viol):.2f} | RULE-PASS", COLOR_VALIDATED, 1.0)

    # Tier 3 — actually fired.
    fired_new = 0
    for action in actions:
        box = _box_of(action)
        if not box:
            continue
        is_new = action.get("StateStatus") == "New"
        fired_new += 1 if is_new else 0
        color = COLOR_ACTION_NEW if is_new else COLOR_ACTION_UPDATE
        tid = _track_of(action)
        prefix = f"ID:{tid} " if tid is not None else ""
        tag = "NEW" if is_new else "UPD"
        _draw_box(frame, box, f"{prefix}{_label_of(action)} {_score_of(action):.2f} | {tag}", color, 1.6)

    if fired_new:
        border = max(2, int(min(w, h) / 220))
        cv2.rectangle(frame, (0, 0), (w - 1, h - 1), COLOR_ACTION_NEW, border)

    _draw_hud(
        frame,
        [
            (f"#{frame_index:05d}", COLOR_TEXT),
            (f"T+{media_time_sec:06.2f}s", COLOR_MUTED),
            (camera_label or "-", COLOR_MUTED),
            (f"det {len(detections)}", COLOR_DETECTION),
            (f"rule-pass {len(validated)}", COLOR_VALIDATED),
            (f"fired {len(actions)}", COLOR_ACTION_NEW if actions else COLOR_MUTED),
            (f"total {violations_so_far}", COLOR_TEXT),
        ],
        anchor="top",
    )
    _draw_hud(
        frame,
        [
            ("DETECTION", COLOR_DETECTION),
            ("RULE-PASS", COLOR_VALIDATED),
            ("VIOLATION", COLOR_ACTION_NEW),
        ],
        anchor="bottom",
    )
    return frame


class AnnotatedVideoWriter:
    """
    Streams BGR frames into FFmpeg and yields browser-playable H.264 MP4 bytes.

    Deliberately mirrors the encoder contract in rtsp/clip_recorder.py: stderr
    goes to a temp FILE rather than a pipe (a chatty FFmpeg would otherwise fill
    the 64KB pipe buffer and deadlock the writer), and stdin is closed by us
    before ``wait()`` — never handed to ``communicate()``, which flushes stdin
    itself and raises ValueError on an already-closed pipe. That exact mistake
    silently disabled violation clips, so it is not repeated here.

    Usage:
        with AnnotatedVideoWriter(fps=15) as w:
            for frame in frames:
                w.write(frame)
        mp4 = w.result
    """

    def __init__(
        self,
        fps: float = 15.0,
        max_dimension: int = 1280,
        crf: int = 23,
        preset: str = "veryfast",
        ffmpeg_path: str = "ffmpeg",
        timeout: float = 120.0,
    ):
        self.fps = max(1.0, float(fps))
        self.max_dimension = int(max_dimension)
        self.crf = int(crf)
        self.preset = preset
        self.ffmpeg_path = ffmpeg_path
        self.timeout = float(timeout)

        self._proc: Optional[subprocess.Popen] = None
        self._err_fh = None
        self._out_path: Optional[str] = None
        self._err_path: Optional[str] = None
        self._size: Optional[Tuple[int, int]] = None  # (w, h)
        self._frames_written = 0
        self._broken = False
        self._closed = False
        self.result: Optional[bytes] = None

    # ── sizing ───────────────────────────────────────────────────────────────
    def _target_size(self, frame: np.ndarray) -> Tuple[int, int]:
        h, w = frame.shape[:2]
        if self.max_dimension > 0 and max(h, w) > self.max_dimension:
            if h >= w:
                new_h = self.max_dimension
                new_w = int(w * (new_h / h))
            else:
                new_w = self.max_dimension
                new_h = int(h * (new_w / w))
        else:
            new_w, new_h = w, h
        # H.264 requires even dimensions.
        new_w -= new_w % 2
        new_h -= new_h % 2
        return max(2, new_w), max(2, new_h)

    def _fit(self, frame: np.ndarray) -> np.ndarray:
        assert self._size is not None
        w, h = self._size
        if frame.shape[1] != w or frame.shape[0] != h:
            return cv2.resize(frame, (w, h), interpolation=cv2.INTER_AREA)
        return frame

    # ── lifecycle ────────────────────────────────────────────────────────────
    def _start(self, frame: np.ndarray) -> None:
        self._size = self._target_size(frame)
        w, h = self._size

        fd, self._out_path = tempfile.mkstemp(suffix=".mp4")
        os.close(fd)
        fd, self._err_path = tempfile.mkstemp(suffix=".log")
        os.close(fd)

        cmd = [
            self.ffmpeg_path, "-y",
            "-f", "rawvideo", "-vcodec", "rawvideo",
            "-s", f"{w}x{h}", "-pix_fmt", "bgr24",
            "-r", str(round(self.fps, 3)),
            "-i", "pipe:0",
            "-c:v", "libx264", "-preset", self.preset, "-crf", str(self.crf),
            "-pix_fmt", "yuv420p", "-movflags", "+faststart",
            self._out_path,
        ]
        self._err_fh = open(self._err_path, "wb")
        self._proc = subprocess.Popen(
            cmd, stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=self._err_fh,
        )
        logger.debug("AnnotatedVideoWriter started: %dx%d @ %.2ffps", w, h, self.fps)

    def write(self, frame: np.ndarray) -> bool:
        """Writes one frame. Returns False once the pipe has broken."""
        if self._closed or self._broken or frame is None or frame.size == 0:
            return False
        if self._proc is None:
            self._start(frame)
        try:
            self._proc.stdin.write(self._fit(frame).tobytes())
        except (BrokenPipeError, OSError, ValueError) as err:
            self._broken = True
            logger.warning("Annotated video pipe broke after %d frame(s): %s", self._frames_written, err)
            return False
        self._frames_written += 1
        return True

    def close(self) -> Optional[bytes]:
        """Finalises the MP4 and returns its bytes (None if nothing usable)."""
        if self._closed:
            return self.result
        self._closed = True

        if self._proc is None:
            self._cleanup()
            return None

        try:
            try:
                self._proc.stdin.close()
            except (BrokenPipeError, OSError, ValueError):
                pass

            returncode = self._proc.wait(timeout=self.timeout)

            if self._err_fh is not None:
                self._err_fh.close()
                self._err_fh = None

            if returncode != 0:
                logger.warning(
                    "Annotated video encoding failed (rc=%s): %s", returncode, self._stderr_tail()
                )
                return None
            if self._frames_written == 0:
                logger.warning("Annotated video had no frames to encode")
                return None

            with open(self._out_path, "rb") as fh:
                data = fh.read()
            self.result = data or None
            if self.result:
                logger.info(
                    "Annotated video encoded: %d frames, %dx%d, %.1f KB",
                    self._frames_written, self._size[0], self._size[1], len(self.result) / 1024.0,
                )
            return self.result
        except subprocess.TimeoutExpired:
            logger.warning("Annotated video encoding timed out after %.0fs", self.timeout)
            self._kill()
            return None
        except Exception as err:  # noqa: BLE001
            logger.warning("Annotated video encoding error: %s", err)
            return None
        finally:
            self._cleanup()

    # ── helpers ──────────────────────────────────────────────────────────────
    @property
    def frames_written(self) -> int:
        return self._frames_written

    @property
    def size(self) -> Optional[Tuple[int, int]]:
        return self._size

    def _stderr_tail(self, limit: int = 2000) -> str:
        if not self._err_path or not os.path.exists(self._err_path):
            return ""
        try:
            with open(self._err_path, "rb") as fh:
                return fh.read()[-limit:].decode("utf-8", errors="replace")
        except OSError:
            return ""

    def _kill(self) -> None:
        if self._proc is not None and self._proc.poll() is None:
            try:
                self._proc.kill()
                self._proc.wait(timeout=5)
            except Exception:  # noqa: BLE001
                pass

    def _cleanup(self) -> None:
        self._kill()
        if self._err_fh is not None:
            try:
                self._err_fh.close()
            except Exception:  # noqa: BLE001
                pass
            self._err_fh = None
        for path in (self._out_path, self._err_path):
            if path and os.path.exists(path):
                try:
                    os.remove(path)
                except OSError:
                    pass
        self._out_path = None
        self._err_path = None

    def __enter__(self) -> "AnnotatedVideoWriter":
        return self

    def __exit__(self, *exc) -> bool:
        self.close()
        return False
