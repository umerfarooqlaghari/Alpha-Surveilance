"""
inference/model_loader.py
=========================
Downloads model weights from S3 at startup when they are not present locally.

S3 bucket: restaurant-ppe-yolo11-pt4-v1--use1-az4--x-s3  (S3 Express One Zone)
S3 key:    models/restaurant-ppe-yolo11.pt

Usage
-----
    from inference.model_loader import ensure_model_local
    local_path = ensure_model_local()   # blocks until file is ready

Environment variables (read via config.py):
    MODEL_S3_BUCKET   — S3 bucket that holds the model weights
    MODEL_S3_KEY      — object key inside the bucket (default: models/restaurant-ppe-yolo11.pt)
    RESTAURANT_PPE_MODEL_PATH — local path to write/check (default: /tmp/models/restaurant-ppe-yolo11.pt)
    AWS_REGION        — us-east-1 (or wherever the bucket lives)
    TESTING_MODE      — when True the download is skipped; a warning is logged
"""

import logging
import os
import threading

import boto3
from botocore.exceptions import ClientError, NoCredentialsError

import config

logger = logging.getLogger("vision-service.model-loader")

_download_lock = threading.Lock()


def ensure_model_local(
    local_path: str = None,
    bucket: str = None,
    s3_key: str = None,
) -> str:
    """
    Guarantee that the model file exists at *local_path*.

    1. If the file already exists locally → return immediately (no network call).
    2. If TESTING_MODE is True → skip download, return path (model may be absent).
    3. Otherwise → download from S3 into local_path (thread-safe, one attempt).

    Returns the local path whether or not the file was successfully obtained.
    Callers should check os.path.exists(result) before loading the model.
    """
    local_path = local_path or config.RESTAURANT_PPE_MODEL_PATH
    bucket = bucket or config.MODEL_S3_BUCKET
    s3_key = s3_key or config.MODEL_S3_KEY

    # Fast path: file already present
    if os.path.exists(local_path):
        size_mb = os.path.getsize(local_path) / (1024 * 1024)
        logger.info("Model already cached locally at %s (%.1f MB)", local_path, size_mb)
        return local_path

    if config.TESTING_MODE:
        logger.warning(
            "TESTING_MODE=true — skipping S3 model download. "
            "Restaurant PPE detector will be unavailable unless %s exists.",
            local_path,
        )
        return local_path

    if not bucket:
        logger.error(
            "MODEL_S3_BUCKET is not set. Cannot download model weights. "
            "Set MODEL_S3_BUCKET in your .env or environment."
        )
        return local_path

    with _download_lock:
        # Re-check inside lock (another thread may have downloaded it)
        if os.path.exists(local_path):
            return local_path

        os.makedirs(os.path.dirname(local_path), exist_ok=True)
        _download_from_s3(bucket, s3_key, local_path)

    return local_path


def _download_from_s3(bucket: str, s3_key: str, local_path: str) -> None:
    """Download *s3_key* from *bucket* to *local_path*, with progress logging."""
    logger.info("Downloading model from s3://%s/%s → %s", bucket, s3_key, local_path)

    try:
        s3 = boto3.client("s3", region_name=config.AWS_REGION or "us-east-1")

        # Get size for user-facing log message
        try:
            head = s3.head_object(Bucket=bucket, Key=s3_key)
            size_bytes = head.get("ContentLength", 0)
            size_mb = size_bytes / (1024 * 1024)
            logger.info("Model size: %.1f MB — starting download...", size_mb)
        except ClientError:
            logger.warning("Could not retrieve model size (head_object failed). Proceeding with download.")
            size_mb = 0

        tmp_path = local_path + ".download"
        s3.download_file(bucket, s3_key, tmp_path)
        os.replace(tmp_path, local_path)  # atomic rename

        final_mb = os.path.getsize(local_path) / (1024 * 1024)
        logger.info("✅ Model downloaded successfully: %s (%.1f MB)", local_path, final_mb)

    except NoCredentialsError:
        logger.error(
            "AWS credentials not found. Set AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, "
            "and AWS_REGION to enable model download from S3."
        )
    except ClientError as e:
        code = e.response.get("Error", {}).get("Code", "Unknown")
        logger.error(
            "S3 download failed [%s]: s3://%s/%s — %s",
            code, bucket, s3_key, e,
        )
    except Exception as e:
        logger.error("Unexpected error during model download: %s", e)
        # Clean up incomplete download
        tmp_path = local_path + ".download"
        if os.path.exists(tmp_path):
            os.remove(tmp_path)


def upload_model_to_s3(
    local_path: str = None,
    bucket: str = None,
    s3_key: str = None,
) -> bool:
    """
    Upload the local model file to S3.
    Returns True on success, False on failure.
    """
    local_path = local_path or config.RESTAURANT_PPE_MODEL_PATH
    bucket = bucket or config.MODEL_S3_BUCKET
    s3_key = s3_key or config.MODEL_S3_KEY

    if not os.path.exists(local_path):
        logger.error("Local model file not found: %s", local_path)
        return False

    if not bucket:
        logger.error("MODEL_S3_BUCKET is not set.")
        return False

    size_mb = os.path.getsize(local_path) / (1024 * 1024)
    logger.info("Uploading %.1f MB model to s3://%s/%s ...", size_mb, bucket, s3_key)

    try:
        s3 = boto3.client("s3", region_name=config.AWS_REGION or "us-east-1")
        s3.upload_file(local_path, bucket, s3_key)
        logger.info("✅ Model uploaded: s3://%s/%s", bucket, s3_key)
        return True
    except NoCredentialsError:
        logger.error("AWS credentials not found. Cannot upload model.")
        return False
    except ClientError as e:
        logger.error("S3 upload failed: %s", e)
        return False
