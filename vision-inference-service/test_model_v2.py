from transformers import pipeline

try:
    print("Attempting to load keremberke/yolov8m-protective-equipment-detection...")
    pipe = pipeline("object-detection", model="keremberke/yolov8m-protective-equipment-detection")
    print("✅ Successfully loaded with transformers pipeline!")
except Exception as e:
    print(f"❌ Failed to load with transformers pipeline: {e}")
    
    print("\nAttempting fallback to facebook/detr-resnet-50_finetuned_cppe5...")
    try:
        pipe = pipeline("object-detection", model="facebook/detr-resnet-50_finetuned_cppe5")
        print("✅ Successfully loaded CPPE-5 fallback!")
    except Exception as e2:
        print(f"❌ Failed fallback: {e2}")
