import asyncio
import json
import httpx
import uuid
from datetime import datetime

base_url = "http://localhost:5001"
api_key = os.environ.get("INTERNAL_API_KEY", "")

async def test():
    # 1. Fetch cameras to get a valid tenant/camera ID
    async with httpx.AsyncClient() as client:
        res = await client.get(
            f"{base_url}/api/cameras/internal/active",
            headers={"X-Internal-Api-Key": api_key}
        )
        cameras = res.json()
        print("Cameras:", json.dumps(cameras, indent=2))
        if not cameras:
            print("No active cameras found.")
            return

        cam = cameras[0]
        tenant_id = cam["tenantId"]
        camera_db_id = cam["id"]

        # 2. Simulate violation to see what error the API gives
        payload = {
            "TenantId": tenant_id,
            "CameraId": camera_db_id,
            "Type": "hustvl/yolos-tiny",  # or whatever model
            "CorrelationId": str(uuid.uuid4()),
            "TrackId": 43, # extra field
            "Timestamp": datetime.utcnow().isoformat(),
            "FramePath": "https://example.com/test.jpg",
            "Severity": "High",
            "Status": "Pending", # extra field
            "MetadataJson": '{"box": {"xmin": 0, "ymin": 0, "xmax": 100, "ymax": 100}, "label": "person", "score": 0.99}'
        }

        url = f"{base_url}/api/violations/internal"
        headers = {
            "X-Internal-Api-Key": api_key,
            "Content-Type": "application/json",
        }
        post_res = await client.post(url, json=[payload], headers=headers)
        print(f"\nPOST Status: {post_res.status_code}")
        print(f"POST Response: {post_res.text}")

asyncio.run(test())
