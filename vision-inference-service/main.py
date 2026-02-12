import os
import io
import json
import uuid
import boto3
import time
from datetime import datetime
from fastapi import FastAPI, UploadFile, File, Form
from fastapi.responses import HTMLResponse, JSONResponse
from transformers import pipeline
from PIL import Image, ImageDraw, ImageFont
from dotenv import load_dotenv

load_dotenv()

app = FastAPI()

# Configuration from Environment Variables
SQS_QUEUE_URL = os.environ.get("SQS_QUEUE_URL", "")
S3_BUCKET_NAME = os.environ.get("S3_BUCKET_NAME", "")
AWS_REGION = os.environ.get("AWS_REGION", "")

if not S3_BUCKET_NAME or not AWS_REGION:
    print("WARNING: S3_BUCKET_NAME or AWS_REGION not set. Functionality may be limited.")

print(f"Config: Queue={SQS_QUEUE_URL}, Bucket={S3_BUCKET_NAME}, Region={AWS_REGION}")

# Initialize AWS Clients
s3_client = boto3.client("s3", region_name=AWS_REGION)
sqs_client = boto3.client("sqs", region_name=AWS_REGION)

# Initialize the object detection pipeline
print("Loading model... please wait.")
detector = pipeline("object-detection", model="facebook/detr-resnet-50")
print("Model loaded.")

@app.get("/", response_class=HTMLResponse)
async def read_root():
    return """
    <!DOCTYPE html>
    <html>
    <head>
        <title>Vision Inference Test</title>
        <style>
            body { font-family: sans-serif; max-width: 800px; margin: 0 auto; padding: 20px; }
            .container { border: 1px solid #ccc; padding: 20px; border-radius: 8px; }
            #result { margin-top: 20px; white-space: pre-wrap; background: #f5f5f5; padding: 10px; }
            img { max-width: 100%; margin-top: 20px; border: 1px solid #ddd; }
            .form-group { margin-bottom: 15px; }
            label { display: block; margin-bottom: 5px; font-weight: bold; }
            input[type="text"] { width: 100%; padding: 8px; box-sizing: border-box; }
        </style>
    </head>
    <body>
        <h1>Vision Inference API Tester</h1>
        <div class="container">
            <h3>Upload Frame</h3>
            <div class="form-group">
                <label>Camera ID:</label>
                <input type="text" id="cameraId" value="TestCamera1">
            </div>
            <div class="form-group">
                <label>Tenant ID:</label>
                <input type="text" id="tenantId" value="d34493e3-612e-42d2-b896-37fa72e53ee0">
            </div>
            <input type="file" id="fileInput" accept="image/*">
            <button onclick="uploadImage()">Analyze & Report</button>
            
            <div id="preview"></div>
            <h3>Result:</h3>
            <div id="result">No result yet.</div>
        </div>

        <script>
            async function uploadImage() {
                const fileInput = document.getElementById('fileInput');
                const cameraId = document.getElementById('cameraId').value;
                const tenantId = document.getElementById('tenantId').value;
                const resultDiv = document.getElementById('result');
                const previewDiv = document.getElementById('preview');

                if (fileInput.files.length === 0) {
                    alert("Please select a file first.");
                    return;
                }

                const file = fileInput.files[0];
                
                // Show preview
                const reader = new FileReader();
                reader.onload = function(e) {
                    previewDiv.innerHTML = `<img src="${e.target.result}" alt="Preview">`;
                }
                reader.readAsDataURL(file);

                const formData = new FormData();
                formData.append("file", file);
                formData.append("camera_id", cameraId);
                formData.append("tenant_id", tenantId);

                resultDiv.textContent = "Analyzing and Sending to SQS...";

                try {
                    const response = await fetch("/analyze", {
                        method: "POST",
                        body: formData
                    });
                    
                    const data = await response.json();
                    resultDiv.textContent = JSON.stringify(data, null, 2);
                } catch (error) {
                    resultDiv.textContent = "Error: " + error.message;
                }
            }
        </script>
    </body>
    </html>
    """

@app.post("/analyze")
async def analyze_image(
    file: UploadFile = File(...),
    camera_id: str = Form("TestCamera1"),
    tenant_id: str = Form("d34493e3-612e-42d2-b896-37fa72e53ee0")
):
    try:
        # 1. Read image contents
        image_data = await file.read()
        image = Image.open(io.BytesIO(image_data))
        
        # 2. Run inference
        results = detector(image)
        
        # 3. Process Detections (Simple Logic: If Person detected -> Security Violation)

# ... (inside analyze_image) ...

        # 3. Process Detections & Draw Bounding Boxes
        draw = ImageDraw.Draw(image)
        violations = []
        has_violation = False
        
        for detection in results:
            score = detection['score']
            label = detection['label']
            box = detection['box'] # {'xmin': 10, 'ymin': 20, 'xmax': 100, 'ymax': 200}

            # Draw box for ALL detections for debug/visibility, but flag violation only for 'person'
            if score > 0.7:
                # Draw Red Box for Person (Violation), Blue/Green for others
                color = "red" if label == 'person' else "green"
                draw.rectangle([box['xmin'], box['ymin'], box['xmax'], box['ymax']], outline=color, width=3)
                
                # Draw Label
                text = f"{label} {score:.2f}"
                draw.text((box['xmin'], box['ymin']), text, fill=color)

                if label == 'person':
                    has_violation = True
        
        frame_url = ""
        
        if has_violation:
            # 4. Upload to S3 (Annotated Image)
            filename = f"violations/{tenant_id}/{camera_id}/{datetime.now().strftime('%Y-%m-%d')}/{uuid.uuid4()}.jpg"
            
            # Save annotated image to in-memory buffer
            output_buffer = io.BytesIO()
            image.save(output_buffer, format="JPEG")
            output_buffer.seek(0)

            try:
                # Upload to S3
                s3_client.put_object(
                    Bucket=S3_BUCKET_NAME,
                    Key=filename,
                    Body=output_buffer, # Upload the modified buffer
                    ContentType="image/jpeg"
                )
                # Construct URL (Assuming public or standard format)
                frame_url = f"https://{S3_BUCKET_NAME}.s3.{AWS_REGION}.amazonaws.com/{filename}"
                print(f"Uploaded frame to: {frame_url}")
            except Exception as e:
                print(f"S3 Upload Error: {e}")
                return JSONResponse(status_code=500, content={"error": f"S3 Upload Failed: {str(e)}"})

            # 5. Send message to SQS
            if SQS_QUEUE_URL:
                payload = {
                    "TenantId": tenant_id,
                    "Type": "Security",  # Hardcoded based on 'person' detection logic
                    "CorrelationId": str(uuid.uuid4()),
                    "Timestamp": datetime.utcnow().isoformat(),
                    "FramePath": frame_url,
                    "CameraId": camera_id,
                    "Severity": "High", # Hardcoded
                    "Status": "Pending",
                    "MetadataJson": json.dumps(results)
                }
                
                try:
                    sqs_client.send_message(
                        QueueUrl=SQS_QUEUE_URL,
                        MessageBody=json.dumps(payload)
                    )
                    print("Sent message to SQS")
                except Exception as e:
                    print(f"SQS Error: {e}")
                    return JSONResponse(status_code=500, content={"error": f"SQS Send Failed: {str(e)}"})
            else:
                print("SQS_QUEUE_URL not set, skipping message send.")

        return {
            "filename": file.filename, 
            "detections": results, 
            "violation_detected": has_violation,
            "frame_url": frame_url,
            "sqs_status": "Sent" if has_violation and SQS_QUEUE_URL else "Skipped"
        }
        
    except Exception as e:
        return JSONResponse(status_code=500, content={"error": str(e)})

if __name__ == "__main__":
    import uvicorn
    port = int(os.environ.get("PORT", "8000"))
    uvicorn.run(app, host="0.0.0.0", port=port)
