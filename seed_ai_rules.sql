-- Ensure OWLv2 (locate-anything-v1) is purged from AiModels dictionary
DELETE FROM "AiModels" WHERE "ModelKey" = 'locate-anything-v1' OR "Id" = 'a0000000-0000-0000-0000-000000000005';

-- Register YOLO26e Medium Open-Vocabulary model in the AI model library
INSERT INTO "AiModels" (
	"Id", "ModelKey", "DisplayName", "Description",
	"ModelType", "Status", "DownloadUrl", "S3Bucket", "S3Key",
	"LocalPath", "Version", "FileSizeBytes", "Sha256Checksum", "MinConfidence",
	"RequiresCropping", "RequiresHumanPresence", "CreatedAt", "IsDeleted"
)
VALUES (
	'a0000000-0000-0000-0000-000000000027',
	'yolo26e-medium',
	'YOLO26e Medium (Open-Vocabulary)',
	'Ultralytics YOLOE-26 Medium open-vocabulary vision model supporting zero-shot detection and segmentation with text and visual prompts.',
	'OpenVocabGrounding',
	'Available',
	'https://github.com/ultralytics/assets/releases/download/v8.4.0/yoloe-26m-seg.pt',
	'alpha-surveilance-assets',
	'models/yoloe-26m-seg.pt',
	'/tmp/models/yoloe-26m-seg.pt',
	'26.0',
	70443185,
	'585f5ec9028fd358035da8d860c27c56be285a795cba2076fba536a4391c2c83',
	0.80,
	false,
	false,
	NOW() AT TIME ZONE 'UTC',
	false
)
ON CONFLICT ("ModelKey") DO UPDATE
SET "DisplayName" = EXCLUDED."DisplayName",
	"Description" = EXCLUDED."Description",
	"ModelType" = EXCLUDED."ModelType",
	"Status" = EXCLUDED."Status",
	"DownloadUrl" = EXCLUDED."DownloadUrl",
	"S3Bucket" = EXCLUDED."S3Bucket",
	"S3Key" = EXCLUDED."S3Key",
	"LocalPath" = EXCLUDED."LocalPath",
	"Version" = EXCLUDED."Version",
	"FileSizeBytes" = EXCLUDED."FileSizeBytes",
	"Sha256Checksum" = EXCLUDED."Sha256Checksum",
	"MinConfidence" = EXCLUDED."MinConfidence",
	"RequiresCropping" = EXCLUDED."RequiresCropping",
	"RequiresHumanPresence" = EXCLUDED."RequiresHumanPresence",
	"IsDeleted" = EXCLUDED."IsDeleted";

INSERT INTO "AiModels" (
	"Id", "ModelKey", "DisplayName", "Description",
	"ModelType", "Status", "DownloadUrl", "S3Bucket", "S3Key",
	"LocalPath", "Version", "RequiresCropping", "RequiresHumanPresence", "CreatedAt", "IsDeleted"
)
VALUES (
	'a0000000-0000-0000-0000-000000000006',
	'construction-site-safety/1',
	'Construction Site Safety (Roboflow)',
	'Roboflow-hosted construction PPE detector for hardhat, vest and mask checks.',
	'RoboflowCloud',
	'Available',
	NULL,
	NULL,
	NULL,
	NULL,
	'1.0',
	false,
	false,
	NOW() AT TIME ZONE 'UTC',
	false
)
ON CONFLICT ("ModelKey") DO UPDATE
SET "DisplayName" = EXCLUDED."DisplayName",
	"Description" = EXCLUDED."Description",
	"ModelType" = EXCLUDED."ModelType",
	"Status" = EXCLUDED."Status",
	"DownloadUrl" = EXCLUDED."DownloadUrl",
	"S3Bucket" = EXCLUDED."S3Bucket",
	"S3Key" = EXCLUDED."S3Key",
	"LocalPath" = EXCLUDED."LocalPath",
	"Version" = EXCLUDED."Version",
	"RequiresCropping" = EXCLUDED."RequiresCropping",
	"RequiresHumanPresence" = EXCLUDED."RequiresHumanPresence",
	"IsDeleted" = EXCLUDED."IsDeleted";

INSERT INTO "Sops" ("Id", "Name", "Description", "IsDeleted", "CreatedAt")
VALUES (
	'00000000-0000-0000-0000-000000000010',
	'Open Operations',
	'Experimental open-vocabulary SOP for broad operational activity detection.',
	false,
	NOW()
)
ON CONFLICT ("Id") DO UPDATE
SET "Name" = EXCLUDED."Name",
	"Description" = EXCLUDED."Description",
	"IsDeleted" = EXCLUDED."IsDeleted";

INSERT INTO "SopViolationTypes" (
	"Id", "SopId", "Name", "ModelIdentifier", "TriggerLabels", "Description", "IsDeleted", "AiModelId"
)
SELECT
	'00000000-0000-0000-0000-000000000011',
	'00000000-0000-0000-0000-000000000010',
	'Open Operations Activity',
	'yolo26e-medium',
	'["person"]',
	'Ultralytics YOLOE-26 Medium open-vocabulary activity detection.',
	false,
	m."Id"
FROM "AiModels" m
WHERE m."ModelKey" = 'yolo26e-medium'
ON CONFLICT ("Id") DO UPDATE
SET "ModelIdentifier" = EXCLUDED."ModelIdentifier",
	"TriggerLabels" = EXCLUDED."TriggerLabels",
	"Description" = EXCLUDED."Description",
	"IsDeleted" = EXCLUDED."IsDeleted",
	"AiModelId" = EXCLUDED."AiModelId";

-- Remove strict DO block, use raw flat inserts
-- Ensure an SOP exists so the FK works
INSERT INTO "Sops" ("Id", "Name", "Description", "IsDeleted", "CreatedAt")
VALUES ('00000000-0000-0000-0000-000000000001', 'Human Detection SOP', 'Auto-generated', false, NOW())
ON CONFLICT ("Id") DO NOTHING;

-- Insert Violation Type linked directly to that known SOP (using YOLO26 Medium)
INSERT INTO "SopViolationTypes" ("Id", "SopId", "Name", "ModelIdentifier", "TriggerLabels", "Description", "IsDeleted")
VALUES ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000001', 'Unauthorized Person', 'yolo26m', '["person"]', 'A person entered the frame', false)
ON CONFLICT ("Id") DO UPDATE SET "ModelIdentifier" = 'yolo26m', "TriggerLabels" = '["person"]';



-- Link to CAM-001
INSERT INTO "CameraViolationTypes" ("CameraId", "SopViolationTypeId")
SELECT "Id", '00000000-0000-0000-0000-000000000002'
FROM "Cameras"
WHERE "CameraId" = 'CAM-001'
ON CONFLICT ("CameraId", "SopViolationTypeId") DO NOTHING;

-- Register YOLO26m-pose model in the AI model library
INSERT INTO "AiModels" (
	"Id", "ModelKey", "DisplayName", "Description",
	"ModelType", "Status", "DownloadUrl", "S3Bucket", "S3Key",
	"LocalPath", "Version", "RequiresCropping", "RequiresHumanPresence", "CreatedAt", "IsDeleted"
)
VALUES (
	'a0000000-0000-0000-0000-000000000026',
	'yolo26m-pose',
	'YOLO26 Medium Pose (Ergonomics & Posture)',
	'High-accuracy pose estimation model for REBA and NIOSH ergonomic risk auditing.',
	'YoloPose',
	'Available',
	'https://github.com/ultralytics/assets/releases/download/v8.3.0/yolo11m-pose.pt',
	'restaurant-ppe-yolo11-pt4-v1--use1-az4--x-s3',
	'models/yolo26m-pose.pt',
	'/tmp/models/yolo26m-pose.pt',
	'1.0',
	false,
	true,
	NOW() AT TIME ZONE 'UTC',
	false
)
ON CONFLICT ("ModelKey") DO UPDATE
SET "DisplayName" = EXCLUDED."DisplayName",
	"Description" = EXCLUDED."Description",
	"ModelType" = EXCLUDED."ModelType",
	"Status" = EXCLUDED."Status",
	"DownloadUrl" = EXCLUDED."DownloadUrl",
	"S3Bucket" = EXCLUDED."S3Bucket",
	"S3Key" = EXCLUDED."S3Key",
	"LocalPath" = EXCLUDED."LocalPath",
	"Version" = EXCLUDED."Version",
	"RequiresCropping" = EXCLUDED."RequiresCropping",
	"RequiresHumanPresence" = EXCLUDED."RequiresHumanPresence",
	"IsDeleted" = EXCLUDED."IsDeleted";

-- Ergonomics SOP
INSERT INTO "Sops" ("Id", "Name", "Description", "IsDeleted", "CreatedAt")
VALUES (
	'00000000-0000-0000-0000-000000000020',
	'Ergonomic Health & Safety',
	'Continuous real-time posture risk and NIOSH lifting evaluation for workstation operators.',
	false,
	NOW()
)
ON CONFLICT ("Id") DO UPDATE
SET "Name" = EXCLUDED."Name",
	"Description" = EXCLUDED."Description",
	"IsDeleted" = EXCLUDED."IsDeleted";

-- Ergonomics Violation Type
INSERT INTO "SopViolationTypes" (
	"Id", "SopId", "Name", "ModelIdentifier", "TriggerLabels", "Description", "IsDeleted", "AiModelId"
)
SELECT
	'00000000-0000-0000-0000-000000000021',
	'00000000-0000-0000-0000-000000000020',
	'Ergonomic Posture & Lifting Risk',
	'yolo26m-pose',
	'["person", "ergonomic-risk"]',
	'Excessive REBA postural score or unsafe NIOSH lifting index detected.',
	false,
	m."Id"
FROM "AiModels" m
WHERE m."ModelKey" = 'yolo26m-pose'
ON CONFLICT ("Id") DO UPDATE
SET "ModelIdentifier" = EXCLUDED."ModelIdentifier",
	"TriggerLabels" = EXCLUDED."TriggerLabels",
	"Description" = EXCLUDED."Description",
	"IsDeleted" = EXCLUDED."IsDeleted",
	"AiModelId" = EXCLUDED."AiModelId";


-- Register YOLO26 Medium (Standard Detection) in the AI model library
INSERT INTO "AiModels" (
	"Id", "ModelKey", "DisplayName", "Description",
	"ModelType", "Status", "DownloadUrl", "S3Bucket", "S3Key",
	"LocalPath", "Version", "FileSizeBytes", "Sha256Checksum",
	"RequiresCropping", "RequiresHumanPresence", "CreatedAt", "IsDeleted"
)
VALUES (
	'a0000000-0000-0000-0000-000000000028',
	'yolo26m',
	'Human Detection YOLO 26m',
	'State-of-the-art YOLO26 Medium end-to-end NMS-free model for multi-person detection and surveillance.',
	'YoloLocal',
	'Available',
	'https://github.com/ultralytics/assets/releases/download/v8.4.0/yolo26m.pt',
	'alpha-surveilance-assets',
	'models/yolo26m.pt',
	'/tmp/models/yolo26m.pt',
	'26.0',
	44255705,
	'401cea9ab23ad19246ff7744859816bc599f350e93c9dd30367b6f0a0745d0b7',
	false,
	false,
	NOW() AT TIME ZONE 'UTC',
	false
)
ON CONFLICT ("ModelKey") DO UPDATE
SET "DisplayName" = EXCLUDED."DisplayName",
	"Description" = EXCLUDED."Description",
	"ModelType" = EXCLUDED."ModelType",
	"Status" = EXCLUDED."Status",
	"DownloadUrl" = EXCLUDED."DownloadUrl",
	"S3Bucket" = EXCLUDED."S3Bucket",
	"S3Key" = EXCLUDED."S3Key",
	"LocalPath" = EXCLUDED."LocalPath",
	"Version" = EXCLUDED."Version",
	"FileSizeBytes" = EXCLUDED."FileSizeBytes",
	"Sha256Checksum" = EXCLUDED."Sha256Checksum",
	"RequiresCropping" = EXCLUDED."RequiresCropping",
	"RequiresHumanPresence" = EXCLUDED."RequiresHumanPresence",
	"IsDeleted" = EXCLUDED."IsDeleted";

-- Back-fill FK on all SopViolationTypes to match registered AiModels
UPDATE "SopViolationTypes" svt
SET "AiModelId" = m."Id"
FROM "AiModels" m
WHERE svt."ModelIdentifier" = m."ModelKey"
  AND svt."AiModelId" IS NULL;




