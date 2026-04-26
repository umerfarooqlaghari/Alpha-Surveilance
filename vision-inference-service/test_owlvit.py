import io
import torch
from transformers import pipeline
from PIL import Image
import json

def test_owlvit(image_path, labels):
    print(f"Testing labels: {labels}")
    try:
        image = Image.open(image_path).convert("RGB")
        print("Image loaded successfully.")
    except Exception as e:
        print(f"Failed to load image: {e}")
        return

    print("Loading google/owlvit-base-patch32...")
    model = pipeline("zero-shot-object-detection", model="google/owlvit-base-patch32")
    
    print("Running inference...")
    # Using a very low threshold to see everything
    detections = model(image, candidate_labels=labels, threshold=0.01)
    
    # Sort by score descending
    detections.sort(key=lambda x: x['score'], reverse=True)
    
    print(f"\nTotal detections: {len(detections)}")
    for i, d in enumerate(detections[:20]):
        print(f"[{i+1}/{len(detections)}] Label: '{d['label']}' | Score: {d['score']:.4f} | Box: {d['box']}")

if __name__ == "__main__":
    labels = ["person", "hairnet", "gloves", "glove", "dish", "sink", "floor", "trash", "basket", "apron", "head"]
    test_owlvit("rest1.jpg", labels)

