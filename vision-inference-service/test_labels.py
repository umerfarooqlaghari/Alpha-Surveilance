"""
Print labels from the configured restaurant PPE YOLO export.

Expected production violation labels are:
  - no-hairnet
  - no-mask
"""
import json
import os

from ultralytics import YOLO


def test_labels():
    model_path = os.environ.get("RESTAURANT_PPE_MODEL_PATH", "/tmp/models/restaurant-ppe-yolo11.pt")
    if not os.path.exists(model_path):
        raise FileNotFoundError(f"Restaurant PPE model not found: {model_path}")

    model = YOLO(model_path)
    labels = list(model.names.values()) if isinstance(model.names, dict) else list(model.names)

    print(f"Labels for restaurant-ppe-v1: {labels}")
    with open("/tmp/model_labels.json", "w") as f:
        json.dump({"restaurant-ppe-v1": labels}, f)


if __name__ == "__main__":
    test_labels()
