# ─────────────────────────────────────────────────────────────────────────────
# Human Re-ID Service
#
# Multi-stage: the build toolchain never reaches the runtime image. Previously
# gcc/g++/cmake/build-essential (~443 MB, 101 packages) were installed to compile
# dlib from source and then shipped in the final image. dlib now arrives as a
# prebuilt wheel (see requirements.txt), so the runtime stage needs no compiler
# at all.
# ─────────────────────────────────────────────────────────────────────────────

# ── builder ──────────────────────────────────────────────────────────────────
FROM python:3.11-slim AS builder

ENV PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1

# Self-contained venv so the runtime stage can take it wholesale.
RUN python -m venv /opt/venv
ENV PATH="/opt/venv/bin:$PATH"

WORKDIR /app
COPY requirements.txt requirements-face.txt ./

RUN pip install -r requirements.txt \
 && pip install --no-deps -r requirements-face.txt

# ── runtime ──────────────────────────────────────────────────────────────────
FROM python:3.11-slim AS runtime

# dlib's compiled extension links libstdc++; psycopg2-binary bundles its own
# libpq and Pillow bundles its own image codecs, so nothing else is needed.
# (libstdc++6 used to arrive as a side effect of build-essential.)
RUN apt-get update \
 && apt-get install -y --no-install-recommends libstdc++6 \
 && rm -rf /var/lib/apt/lists/*

COPY --from=builder /opt/venv /opt/venv

ENV PATH="/opt/venv/bin:$PATH" \
    PYTHONPATH=/app \
    PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1

WORKDIR /app
COPY . .

# Fail the BUILD, not the first enrolment request, if the prebuilt dlib cannot
# load in the slim runtime. Importing face_recognition also loads the detector,
# pose predictor and encoder models, so this exercises the whole stack.
RUN python -c "import dlib, face_recognition; print('dlib', dlib.__version__, '+ face_recognition OK')"

EXPOSE 8001

# Render injects $PORT; default to 8001 for local docker runs.
# Shell form is required so the variable expands at container start.
CMD ["sh", "-c", "python -m uvicorn app.main:app --host 0.0.0.0 --port ${PORT:-8001}"]
