-- Register experimental open-vocab model in the AI model library.
-- The vision service resolves hf:// references through Hugging Face at runtime.
INSERT INTO "AiModels" (
	"Id", "ModelKey", "DisplayName", "Description",
	"ModelType", "Status", "DownloadUrl", "S3Bucket", "S3Key",
	"LocalPath", "Version", "RequiresCropping", "RequiresHumanPresence", "CreatedAt", "IsDeleted"
)
VALUES (
	'a0000000-0000-0000-0000-000000000005',
	'locate-anything-v1',
	'Locate Anything OWLv2 (Experimental)',
	'Experimental open-vocabulary grounding model for trigger-label based camera rules.',
	'OpenVocabGrounding',
	'Registered',
	NULL,
	NULL,
	NULL,
	'hf://google/owlv2-base-patch16-ensemble',
	'1.0-experimental',
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
	'locate-anything-v1',
	'["person"]',
	'Experimental open-vocabulary activity detection using locate-anything-v1.',
	false,
	m."Id"
FROM "AiModels" m
WHERE m."ModelKey" = 'locate-anything-v1'
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

-- Insert Violation Type linked directly to that known SOP
INSERT INTO "SopViolationTypes" ("Id", "SopId", "Name", "ModelIdentifier", "TriggerLabels", "Description", "IsDeleted")
VALUES ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000001', 'Unauthorized Person', 'hustvl/yolos-tiny', '["person"]', 'A person entered the frame', false)
ON CONFLICT ("Id") DO UPDATE SET "ModelIdentifier" = 'hustvl/yolos-tiny', "TriggerLabels" = '["person"]';

-- Link to CAM-001
INSERT INTO "CameraViolationTypes" ("CameraId", "SopViolationTypeId")
SELECT "Id", '00000000-0000-0000-0000-000000000002'
FROM "Cameras"
WHERE "CameraId" = 'CAM-001'
ON CONFLICT ("CameraId", "SopViolationTypeId") DO NOTHING;
