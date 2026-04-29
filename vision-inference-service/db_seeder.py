import asyncio
import os
import uuid
import datetime
import json
import asyncpg
from dotenv import load_dotenv

load_dotenv()

async def run_seed():
    # Read DSN from env (set ConnectionStrings__violations or VIOLATIONS_DB_URL)
    dsn = os.getenv("VIOLATIONS_DB_URL") or os.getenv("ConnectionStrings__violations")
    if not dsn:
        raise RuntimeError(
            "No database connection string found. "
            "Set VIOLATIONS_DB_URL or ConnectionStrings__violations (see .env.example)."
        )
    print(f"Connecting to DB...")
    
    try:
        conn = await asyncpg.connect(dsn)
        
        # 1. Ensure a SOP exists
        sop_id = "00000000-0000-0000-0000-000000000001"
        await conn.execute('''
            INSERT INTO "Sops" ("Id", "Name", "Description", "IsDeleted", "CreatedAt")
            VALUES ($1, 'Human Detection SOP', 'Auto-generated', false, $2)
            ON CONFLICT ("Id") DO NOTHING
        ''', sop_id, datetime.datetime.now())

        # 2. Insert the Violation Type with TriggerLabels
        sop_viol_id = "00000000-0000-0000-0000-000000000002"
        await conn.execute('''
            INSERT INTO "SopViolationTypes" 
            ("Id", "SopId", "Name", "ModelIdentifier", "TriggerLabels", "Description", "IsDeleted", "CreatedAt")
            VALUES ($1, $2, 'Unauthorized Person', 'hustvl/yolos-tiny', '["person"]', 'A person entered the frame', false, $3)
            ON CONFLICT ("Id") DO UPDATE SET "ModelIdentifier" = 'hustvl/yolos-tiny', "TriggerLabels" = '["person"]'
        ''', sop_viol_id, sop_id, datetime.datetime.now())

        # 3. Get CAM-001 Internal UUID
        cam_id_row = await conn.fetchrow('''SELECT "Id" FROM "Cameras" WHERE "CameraId" = 'CAM-001' ''')
        
        if cam_id_row:
            internal_camera_id = cam_id_row['Id']
            # 4. Link camera to violation type
            await conn.execute('''
                INSERT INTO "CameraViolationTypes" ("CameraId", "SopViolationTypeId")
                VALUES ($1, $2)
                ON CONFLICT ("CameraId", "SopViolationTypeId") DO NOTHING
            ''', internal_camera_id, sop_viol_id)
            print("Successfully linked CAM-001 to hustvl/yolos-tiny model detecting 'person'.")
            
            # 5. Tell Python Service to hot-reload
            print("Database seeded. Within 60 seconds, CAM-001 will auto-detect the new rule and begin triggering violations!")
        else:
            print("ERROR: CAM-001 not found in the database. Has the Camera been created in the UI?")
            
        await conn.close()
    except Exception as e:
        print(f"Database error: {e}")

if __name__ == "__main__":
    asyncio.run(run_seed())
