# Alpha Surveillance - Developer Setup Guide

This guide outlines the steps required to set up the development environment for a new developer.

## 1. Prerequisites
*   **Code Editors:** VS Code or Visual Studio 2022.
*   **.NET SDK:** .NET 8.0 or later.
*   **Python:** Python 3.10+ (for Vision Inference Service).
*   **Docker Desktop:** Required for .NET Aspire orchestration (PostgreSQL, Redis).
*   **AWS CLI:** Installed and configured (`aws configure`).

## 2. AWS Configuration (IMPORTANT)
### Credentials
The application uses the `default` AWS profile configured on your machine. Ensure you have valid credentials:
```bash
aws configure
# Enter Access Key, Secret Key, Region (e.g., us-east-1)
```

### SQS (Message Queue)
*   **How it works:** The SQS queue (`violation-queue`) is **Infrastructure-as-Code**.
*   **Does it need sharing?** **NO.** When you run the project via .NET Aspire, it automatically provisions the queue in *your* configured AWS account using CloudFormation.
*   **Access:** You have full access to this queue because it is created in your account.

### S3 (Object Storage)
*   **How it works:** Usage is configured via `BucketName`.
*   **Does it need sharing?** **YES (The Name).**
*   **Config:** You must set the `BucketName` in `surveilance-app-host/AppHost1/appsettings.json`.
*   **Access Control:**
    *   **Option A (Shared Bucket):** Use the existing bucket name (e.g., `alphasurveilance-dev-1`). **Condition:** Your AWS IAM User must be granted `AmazonS3FullAccess` (or specific R/W permissions) to this specific bucket by the bucket owner.
    *   **Option B (Private Bucket):** Create your own unique bucket (e.g., `alpha-dev-[yourname]`) and update the config. This is often easier for isolated testing.

## 3. Configuration Files & Secrets
You need to create/update `appsettings.json` files with your secrets. **DO NOT COMMIT THESE TO GIT.**

### A. AppHost (Orchestrator)
**File:** `surveilance-app-host/AppHost1/appsettings.json`
```json
{
  "AWS": {
    "Region": "us-east-1"
  },
  "S3Config": {
    "BucketName": "alphasurveilance-dev-1" 
  }
}
```

### B. Violation Management API
**File:** `violation-management-service-api/violation-management-api/appsettings.json`
*   **Database:** `ConnectionStrings` are injected automatically by Aspire.
*   **Keys:** Fill in `Jwt`, `Encryption`, `Cloudinary`, and `Brevo` sections.
*   **Note:** Ask the lead developer for the shared `keys.txt` securely (e.g., via 1Password/LastPass) to ensure you can generate compatible tokens.

### C. BFF (Backend for Frontend)
**File:** `alpha-surveilance-bff/appsettings.json`
*   **Keys:** Ensure the `Jwt:SecretKey` matches the one in the Violation API.

## 4. Running the Project
1.  **Start Docker Desktop.**
2.  Navigate to `surveilance-app-host/AppHost1`.
3.  Run:
    ```bash
    dotnet run
    ```
4.  Open the **Aspire Dashboard** URL printed in the console to view all services.

## 5. Vision Inference Service (Python)
Dependencies should install automatically via `pip` if you use the standard workflow, but if setting up manually:
```bash
cd vision-inference-service
pip install -r requirements.txt
```
The service port and AWS config are injected automatically by AppHost.
