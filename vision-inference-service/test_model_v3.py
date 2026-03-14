from transformers import pipeline
import os

results_file = "model_results.txt"
with open(results_file, "w") as f:
    f.write("Starting Model Verification...\n")
    
    # 1. Keremberke
    try:
        f.write("\nAttempting: keremberke/yolov8m-protective-equipment-detection\n")
        pipe = pipeline("object-detection", model="keremberke/yolov8m-protective-equipment-detection")
        f.write("SUCCESS: keremberke model loaded.\n")
    except Exception as e:
        f.write(f"FAILURE: keremberke model failed: {str(e)[:200]}...\n")
        
    # 2. CPPE-5 
    try:
        f.write("\nAttempting: facebook/detr-resnet-50_finetuned_cppe5\n")
        pipe = pipeline("object-detection", model="facebook/detr-resnet-50_finetuned_cppe5")
        f.write("SUCCESS: CPPE-5 model loaded.\n")
    except Exception as e:
        f.write(f"FAILURE: CPPE-5 model failed: {str(e)[:200]}...\n")

print("Done. See model_results.txt")
