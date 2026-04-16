# Fetch the official WebRTC-enabled static FFmpeg image (v8.0+)
FROM mwader/static-ffmpeg:latest AS ffmpeg

FROM python:3.10-slim

WORKDIR /app

# Install system dependencies
RUN apt-get update && apt-get install -y \
    libsm6 \
    libxext6 \
    libgl1 \
    libglib2.0-0 \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Copy recent FFmpeg directly from the static container (Fixes 404 tarball issues)
COPY --from=ffmpeg /ffmpeg /usr/local/bin/ffmpeg
COPY --from=ffmpeg /ffprobe /usr/local/bin/ffprobe

RUN pip install --upgrade pip
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

# Main entrypoint
CMD ["python", "main.py"]
