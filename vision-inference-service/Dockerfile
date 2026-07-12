# H16 fix: pin the ffmpeg base image to an exact tag so we don't silently
# rebuild against a different ffmpeg release (which has previously broken
# the WHIP muxer).
FROM mwader/static-ffmpeg:7.0.2 AS ffmpeg

FROM python:3.10-slim

WORKDIR /app

# Make pip more resilient to transient network issues in CI/container builds
ENV PIP_DISABLE_PIP_VERSION_CHECK=1 \
    PIP_DEFAULT_TIMEOUT=120 \
    PIP_RETRIES=20

# Install system dependencies.
# Note: libsm6, libxext6, and libgl1 are intentionally excluded — they pull
# in the entire mesa/vulkan/X11 graphics stack (~45 packages, ~60 MB) but are
# NOT required by opencv-python-headless. build-essential + cmake are kept
# because face_recognition depends on dlib, which compiles from source.
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y \
    build-essential \
    cmake \
    libglib2.0-0 \
    ca-certificates \
    curl \
    git \
    && rm -rf /var/lib/apt/lists/*

# Copy recent FFmpeg directly from the static container (Fixes 404 tarball issues)
COPY --from=ffmpeg /ffmpeg /usr/local/bin/ffmpeg
COPY --from=ffmpeg /ffprobe /usr/local/bin/ffprobe

RUN pip install --upgrade pip "setuptools<81" wheel
COPY requirements.txt .

# Install CPU-only versions of torch/vision/audio to keep the image small and fast
RUN pip install --no-cache-dir torch==2.5.1 torchvision==0.20.1 torchaudio==2.5.1 --index-url https://download.pytorch.org/whl/cpu

RUN set -eux; \
    pip install --no-cache-dir --retries ${PIP_RETRIES} --timeout ${PIP_DEFAULT_TIMEOUT} -r requirements.txt

# ultralytics depends on "opencv-python>=4.6.0" (not the headless variant),
# so it gets pulled in transitively and silently overwrites the pinned
# opencv-python-headless cv2 module with one that requires libGL.so.1 —
# which we deliberately don't install (see apt-get step above). Force the
# headless build back in as the very last step so its cv2 .so wins.
RUN pip uninstall -y opencv-python || true \
    && pip install --no-cache-dir --force-reinstall --no-deps opencv-python-headless==4.10.0.84

COPY . .

# H16 fix: run as an unprivileged user so a container compromise can't
# trivially modify the image filesystem or escalate via shared volumes.
# Create the user, hand it /app and the writable model cache, then drop.
RUN groupadd --system --gid 1001 vision \
    && useradd --system --uid 1001 --gid vision --create-home --shell /usr/sbin/nologin vision \
    && mkdir -p /tmp/models /home/vision/.cache /home/vision/.aws \
    && chown -R vision:vision /app /tmp/models /home/vision
USER vision

# H16 fix: liveness probe so orchestrators (compose, k8s, ECS) restart the
# container when /health stops responding. 30s start period gives the model
# load time to finish before the first probe fires.
# Uses $PORT (defaults to 8000, matching config.py's PORT default) — this was
# previously hardcoded to 8001 (human-reid's port), which made Docker report
# this container unhealthy even while it was serving correctly on 8000.
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD sh -c 'curl --fail --silent --show-error http://127.0.0.1:${PORT:-8000}/health' || exit 1

# Main entrypoint
CMD ["python", "main.py"]
