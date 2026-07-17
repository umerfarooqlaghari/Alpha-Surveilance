// REMOVED (dead code): the root DashboardController that lived here mapped
// /api/dashboard/violations/{id} and /api/dashboard/violations/recent using an
// ExternalViolationDto with `int Type` / `int? Severity` fields that do not
// exist on the Violation Management API's ViolationResponse (it returns string
// names via SopName/ViolationTypeName), so every mapped value was garbage.
//
// No UI route calls /api/dashboard/* — the live endpoints are the tenant-scoped
// controllers in Controllers/Tenant (e.g. /api/tenant/dashboard/violations/recent).
//
// The file is kept as a tombstone only because the workspace disallowed file
// deletion; it intentionally contains no types. Safe to delete outright.
