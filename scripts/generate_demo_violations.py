import urllib.request
import json
import uuid
import random
from datetime import datetime
import sys

# ==============================================================================
# 🎯 ALPHA SURVEILLANCE — DEMO DATA GENERATOR (NO DEPENDENCIES)
# ==============================================================================
# Uses standard urllib to ensure it works on any Python 3 environment.
# ==============================================================================

# --- CONFIGURATION ---
API_URL = "http://localhost:5001/api/violations/internal"
API_KEY = "alpha-vision-internal-8f3a2c91d7e4b056f1a9c3d8e2f74b01"

TENANT_ID = "477701e8-a261-4722-ab2a-d7841492d4e2"
CAMERA_ID = "145bd27a-1666-4a32-af8e-ef341bf5e752"
CAMERA_NAME = "CAM-001"

VIOLATION_TEMPLATES = [
    {
        "name": "Unauthorized Person in Restricted Zone",
        "model": "hustvl/yolos-tiny",
        "label": "person",
        "image_url": "https://images.unsplash.com/photo-1541888946425-d81bb19480c5?q=100&w=1200&auto=format&fit=crop",
        "severity": "High"
    },
    {
        "name": "Safety Violation: Missing Hardhat",
        "model": "construction-site-safety/1",
        "label": "no-hardhat",
        "image_url": "https://images.unsplash.com/photo-1504307651254-35680f356dfd?q=100&w=1200&auto=format&fit=crop",
        "severity": "Critical"
    },
    {
        "name": "Safety Violation: Missing Safety Vest",
        "model": "construction-site-safety/1",
        "label": "no-safety vest",
        "image_url": "https://images.unsplash.com/photo-1589939705384-5185137a7f0f?q=100&w=1200&auto=format&fit=crop",
        "severity": "Medium"
    }
]

def generate_violation():
    template = random.choice(VIOLATION_TEMPLATES)
    metadata = {
        "label": template["label"],
        "score": round(random.uniform(0.85, 0.99), 4),
        "box": { "xmin": 120, "ymin": 150, "xmax": 450, "ymax": 500 },
        "violation_type": template["name"],
        "severity": template["severity"]
    }
    return {
        "tenantId": TENANT_ID,
        "cameraId": CAMERA_ID,
        "modelIdentifier": template["model"],
        "correlationId": str(uuid.uuid4()),
        "timestamp": datetime.utcnow().isoformat(),
        "framePath": template["image_url"],
        "metadataJson": json.dumps(metadata)
    }

def send_burst(count=3):
    print(f"🚀 Prompting demo burst for {CAMERA_NAME}...")
    
    payloads = [generate_violation() for _ in range(count)]
    data = json.dumps(payloads).encode('utf-8')
    
    req = urllib.request.Request(API_URL, data=data, method='POST')
    req.add_header('Content-Type', 'application/json')
    req.add_header('X-Internal-Api-Key', API_KEY)

    try:
        with urllib.request.urlopen(req) as response:
            res_body = response.read().decode('utf-8')
            print(f"✅ Success! API Response: {res_body}")
            print("🔔 Check your dashboard Live Alerts now.")
    except Exception as e:
        print(f"❌ Error: {str(e)}")

if __name__ == "__main__":
    count = 3
    if len(sys.argv) > 1:
        try: count = int(sys.argv[1])
        except: pass
    send_burst(count)
