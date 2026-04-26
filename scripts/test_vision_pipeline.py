import urllib.request
import urllib.parse
import json
import os
import io

# --- CONFIGURATION ---
# The Vision Inference Service (Python FastAPI)
VISION_API_URL = "http://localhost:8000/analyze"

# Use the same IDs that worked for you in the other script
TENANT_ID = "97db6efb-5545-4152-96ff-5da731fa17d5"
CAMERA_ID = "CAM-001" # The Vision API /analyze endpoint uses the CameraId slug to look up rules

# Demo Images (Direct from your templates)
IMAGES = [
    "https://images.unsplash.com/photo-1541888946425-d81bb19480c5?q=100&w=1200&auto=format&fit=crop", # Person
    "https://images.unsplash.com/photo-1504307651254-35680f356dfd?q=100&w=1200&auto=format&fit=crop", # Hardhat
    "https://images.unsplash.com/photo-1589939705384-5185137a7f0f?q=100&w=1200&auto=format&fit=crop"  # Vest
]

def send_to_vision(image_url):
    print(f"📥 Downloading frame: {image_url[:60]}...")
    
    try:
        # 1. Download the image
        with urllib.request.urlopen(image_url) as response:
            image_data = response.read()

        # 2. Prepare Multipart Form Data
        boundary = '----WebKitFormBoundary7MA4YWxkTrZu0gW'
        parts = []
        
        # Add camera_id field
        parts.append(f'--{boundary}')
        parts.append('Content-Disposition: form-data; name="camera_id"')
        parts.append('')
        parts.append(CAMERA_ID)
        
        # Add tenant_id field
        parts.append(f'--{boundary}')
        parts.append('Content-Disposition: form-data; name="tenant_id"')
        parts.append('')
        parts.append(TENANT_ID)
        
        # Add the file
        parts.append(f'--{boundary}')
        parts.append('Content-Disposition: form-data; name="file"; filename="frame.jpg"')
        parts.append('Content-Type: image/jpeg')
        parts.append('')
        parts.append(image_data)
        
        parts.append(f'--{boundary}--')
        parts.append('')

        # Encode body
        body = b''
        for part in parts:
            if isinstance(part, str):
                body += part.encode('utf-8') + b'\r\n'
            else:
                body += part + b'\r\n'

        # 3. Send Request
        print(f"🚀 Sending frame to Vision Service for AI analysis...")
        req = urllib.request.Request(VISION_API_URL, data=body, method='POST')
        req.add_header('Content-Type', f'multipart/form-data; boundary={boundary}')
        
        with urllib.request.urlopen(req) as response:
            result = json.loads(response.read().decode())
            print(f"✅ Vision Service Response: Violation={result.get('violation_detected')}")
            print(f"   Detections: {[d['label'] for d in result.get('detections', [])]}")
            
    except Exception as e:
        print(f"❌ Error: {e}")

if __name__ == "__main__":
    print("🎯 ALPHA SURVEILLANCE — END-TO-END VISION TEST")
    print("-" * 50)
    for url in IMAGES:
        send_to_vision(url)
        print("-" * 50)
    print("\n🏁 Test complete. Check your dashboard for AI-processed alerts!")
