from transformers import pipeline
import json

MODEL_REGISTRY = {
    "human-detection-v1": "hustvl/yolos-tiny",
    "restaurant-hygiene-v1": "keremberke/yolov8m-protective-equipment"
}

def test_labels():
    results = {}
    for name, path in MODEL_REGISTRY.items():
        print(f"Loading {name}...")
        pipe = pipeline("object-detection", model=path)
        labels = []
        if hasattr(pipe, "model") and hasattr(pipe.model, "config") and hasattr(pipe.model.config, "id2label"):
            labels = list(pipe.model.config.id2label.values())
        results[name] = labels
        print(f"Labels for {name}: {labels}")
    
    with open("/tmp/model_labels.json", "w") as f:
        json.dump(results, f)

if __name__ == "__main__":
    test_labels()
