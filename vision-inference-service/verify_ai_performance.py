"""
verify_ai_performance.py
Benchmark and verification script for the upgraded AI Inference Engine.
Tests: MPS support, YOLOv11 latency, YOLO-World zero-shot, and Data Collection.
"""
import time
import torch
import numpy as np
from PIL import Image
import logging
import os

# Set up logging to see what's happening
logging.basicConfig(level=logging.INFO, format="%(levelname)s: %(message)s")
logger = logging.getLogger("verify-ai")

# Mocking config for the engine
class MockRule:
    def __init__(self, model_id, labels):
        self.model_identifier = model_id
        self.trigger_labels = labels

def run_verification():
    logger.info("🎬 Starting Alpha-Surveillance AI Verification...")
    
    # 1. Check Device Support
    logger.info("-" * 40)
    logger.info(f"PyTorch Version: {torch.__version__}")
    mps_available = torch.backends.mps.is_available()
    logger.info(f"Apple Silicon MPS Available: {mps_available}")
    
    # 2. Initialize Engine
    from inference.inference_engine import InferenceEngine
    from data_collector import DataCollector
    
    engine = InferenceEngine()
    collector = DataCollector(base_path="test_captured_data")
    
    # 3. Create Test Image (Dummy frame)
    test_img = Image.fromarray(np.random.randint(0, 255, (480, 640, 3), dtype=np.uint8))
    
    # 4. Benchmark YOLOv11 (Standard Detection)
    logger.info("-" * 40)
    logger.info("⏱️ Benchmarking YOLOv11 (Standard Detection)...")
    rules = [MockRule("human-detection-v1", ["person"])]
    
    # Warmup
    engine.run_inference(test_img, rules)
    
    start = time.time()
    iterations = 5
    for _ in range(iterations):
        results = engine.run_inference(test_img, rules)
    
    avg_latency = (time.time() - start) / iterations * 1000
    fps = 1000 / avg_latency
    logger.info(f"✅ YOLOv11 Avg Latency: {avg_latency:.2f}ms ({fps:.1f} FPS)")
    
    # 5. Benchmark YOLO-World (Zero-Shot)
    logger.info("-" * 40)
    logger.info("⏱️ Benchmarking YOLO-World (Zero-Shot)...")
    world_rules = [MockRule("hygiene-monitor", ["helmet", "safety vest", "gloves"])]
    
    # Warmup
    engine.run_inference(test_img, world_rules)
    
    start = time.time()
    for _ in range(iterations):
        results = engine.run_inference(test_img, world_rules)
        
    avg_latency = (time.time() - start) / iterations * 1000
    fps = 1000 / avg_latency
    logger.info(f"✅ YOLO-World Avg Latency: {avg_latency:.2f}ms ({fps:.1f} FPS)")
    
    # 6. Verify Data Collection
    logger.info("-" * 40)
    logger.info("📸 Verifying Data Collection Pipeline...")
    
    # Create an 'interesting' result (low confidence)
    interesting_results = [{"label": "person", "score": 0.45, "box": {"xmin": 0, "ymin": 0, "xmax": 100, "ymax": 100}}]
    collector.collect_inference_event(test_img, interesting_results, "CAM-TEST", "TENANT-TEST")
    
    # Check if files were created
    if os.path.exists("test_captured_data/metadata"):
        files = os.listdir("test_captured_data/metadata")
        if len(files) > 0:
            logger.info(f"✅ Data Collector saved {len(files)} event(s).")
            # Verify feedback logic
            event_id = files[0].replace(".json", "")
            collector.handle_user_feedback(event_id, is_correct=False, corrected_label="human")
            logger.info("✅ Feedback loop verified.")
        else:
            logger.error("❌ Data Collector failed to save interesting event.")
    else:
        logger.error("❌ Data Collector directory not found.")

    logger.info("-" * 40)
    logger.info("🎉 Verification Complete!")

if __name__ == "__main__":
    run_verification()
