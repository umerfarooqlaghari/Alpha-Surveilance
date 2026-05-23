#!/usr/bin/env python3
"""
scripts/upload_model_to_s3.py
==============================
One-shot helper: upload the exported restaurant-ppe-yolo11.pt weights to S3
so the inference service can auto-download them at startup.

Usage
-----
    # From the vision-inference-service directory (or project root):
    python scripts/upload_model_to_s3.py --file /path/to/restaurant-ppe-yolo11.pt

    # Override bucket / key:
    python scripts/upload_model_to_s3.py \
        --file /path/to/restaurant-ppe-yolo11.pt \
        --bucket restaurant-ppe-yolo11-pt4-v1--use1-az4--x-s3 \
        --key   models/restaurant-ppe-yolo11.pt

Requirements
------------
    pip install boto3 python-dotenv
    AWS credentials must be available (env vars, ~/.aws/credentials, or IAM role).
"""

import argparse
import logging
import os
import sys

# Allow running from repo root or vision-inference-service directory
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from dotenv import load_dotenv
load_dotenv(os.path.join(os.path.dirname(__file__), "..", "vision-inference-service", ".env"), override=False)
load_dotenv(override=False)  # also try cwd

import boto3
from botocore.exceptions import ClientError, NoCredentialsError

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
logger = logging.getLogger("upload-model")

DEFAULT_BUCKET = os.environ.get("MODEL_S3_BUCKET", "restaurant-ppe-yolo11-pt4-v1--use1-az4--x-s3")
DEFAULT_KEY    = os.environ.get("MODEL_S3_KEY",    "models/restaurant-ppe-yolo11.pt")
DEFAULT_REGION = os.environ.get("AWS_REGION",      "us-east-1")


def upload(local_path: str, bucket: str, s3_key: str, region: str) -> None:
    if not os.path.exists(local_path):
        logger.error("File not found: %s", local_path)
        sys.exit(1)

    size_mb = os.path.getsize(local_path) / (1024 * 1024)
    logger.info("File        : %s", local_path)
    logger.info("Size        : %.1f MB", size_mb)
    logger.info("Destination : s3://%s/%s  (region: %s)", bucket, s3_key, region)

    try:
        s3 = boto3.client("s3", region_name=region)
        s3.upload_file(local_path, bucket, s3_key)
        logger.info("✅  Upload complete: s3://%s/%s", bucket, s3_key)
    except NoCredentialsError:
        logger.error("AWS credentials not found. Set AWS_ACCESS_KEY_ID + AWS_SECRET_ACCESS_KEY.")
        sys.exit(1)
    except ClientError as e:
        logger.error("Upload failed: %s", e)
        sys.exit(1)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Upload restaurant PPE model weights to S3.")
    parser.add_argument("--file",   required=True,          help="Path to the .pt model file")
    parser.add_argument("--bucket", default=DEFAULT_BUCKET, help="S3 bucket name")
    parser.add_argument("--key",    default=DEFAULT_KEY,    help="S3 object key")
    parser.add_argument("--region", default=DEFAULT_REGION, help="AWS region")
    args = parser.parse_args()

    upload(args.file, args.bucket, args.key, args.region)
