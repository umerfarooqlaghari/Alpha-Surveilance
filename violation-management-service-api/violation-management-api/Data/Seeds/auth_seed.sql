-- =============================================
-- Authentication System - Seed Data
-- =============================================

-- Create SuperAdmin User
-- Email: admin@alphasurveillance.com
-- Password: Admin@123
INSERT INTO "Users" ("Id", "TenantId", "FullName", "Email", "PasswordHash", "PhoneNumber", "EmployeeCode", "Designation", "IsActive", "CreatedAt", "LastLoginAt")
VALUES (
    gen_random_uuid(),
    NULL,
    'Super Administrator',
    'admin@alphasurveillance.com',
    '$2a$11$vZ7qF6xKZJ8YQX5yH5yH5uO5yH5yH5yH5yH5yH5yH5yH5yH5yH5yH.',  -- BCrypt hash of 'Admin@123'
    '+1-555-0100',
    'SA001',
    'System Administrator',
    true,
    NOW(),
    NULL
)
ON CONFLICT ("Email") DO NOTHING;

-- Create a Test Tenant (if not exists)
INSERT INTO "Tenants" ("Id", "TenantName", "Slug", "EmployeeCount", "Address", "City", "Country", "Industry", "Status", "CreatedAt")
VALUES (
    gen_random_uuid(),
    'Acme Corporation',
    'acme-corp',
    150,
    '123 Main Street',
    'New York',
    'USA',
    'Manufacturing',
    0, -- Active
    NOW()
)
ON CONFLICT ("Slug") DO NOTHING;

-- Create TenantAdmin User for Acme Corp
-- Email: john@acme.com
-- Password: Password@123
-- Tenant Slug: acme-corp
INSERT INTO "Users" ("Id", "TenantId", "FullName", "Email", "PasswordHash", "PhoneNumber", "EmployeeCode", "Designation", "IsActive", "CreatedAt", "LastLoginAt")
SELECT 
    gen_random_uuid(),
    t."Id",
    'John Doe',
    'john@acme.com',
    '$2a$11$xY9qG7yLaK9ZRY6zI6zI6vP6zI6zI6zI6zI6zI6zI6zI6zI6zI6zI.',  -- BCrypt hash of 'Password@123'
    '+1-555-0200',
    'EMP001',
    'Security Manager',
    true,
    NOW(),
    NULL
FROM "Tenants" t
WHERE t."Slug" = 'acme-corp'
ON CONFLICT ("Email") DO NOTHING;

-- Verify the created users
SELECT 
    u."Id",
    u."FullName",
    u."Email",
    CASE WHEN u."TenantId" IS NULL THEN 'SuperAdmin' ELSE 'TenantAdmin' END AS "Role",
    t."TenantName",
    t."Slug" AS "TenantSlug"
FROM "Users" u
LEFT JOIN "Tenants" t ON u."TenantId" = t."Id"
WHERE u."Email" IN ('admin@alphasurveillance.com', 'john@acme.com');
