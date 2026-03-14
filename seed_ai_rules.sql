-- Remove strict DO block, use raw flat inserts
-- Ensure an SOP exists so the FK works
INSERT INTO "Sops" ("Id", "Name", "Description", "IsDeleted", "CreatedAt")
VALUES ('00000000-0000-0000-0000-000000000001', 'Human Detection SOP', 'Auto-generated', false, NOW())
ON CONFLICT ("Id") DO NOTHING;

-- Insert Violation Type linked directly to that known SOP
INSERT INTO "SopViolationTypes" ("Id", "SopId", "Name", "ModelIdentifier", "TriggerLabels", "Description", "IsDeleted", "CreatedAt")
VALUES ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000001', 'Unauthorized Person', 'hustvl/yolos-tiny', '["person"]', 'A person entered the frame', false, NOW())
ON CONFLICT ("Id") DO UPDATE SET "ModelIdentifier" = 'hustvl/yolos-tiny', "TriggerLabels" = '["person"]';

-- Link to CAM-001
INSERT INTO "CameraViolationTypes" ("CameraId", "SopViolationTypeId")
SELECT "Id", '00000000-0000-0000-0000-000000000002'
FROM "Cameras"
WHERE "CameraId" = 'CAM-001'
ON CONFLICT ("CameraId", "SopViolationTypeId") DO NOTHING;
