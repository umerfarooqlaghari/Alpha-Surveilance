// REMOVED (dead code): the legacy <LiveFeed /> component that lived here was
// imported nowhere (verified via repo-wide grep) and fetched the removed BFF
// endpoint /api/dashboard/violations/recent. The live implementation is
// src/app/tenant/live-feed/page.tsx, which uses /api/tenant/dashboard/*.
//
// The file is kept as a tombstone only because the workspace disallowed file
// deletion; it exports nothing. Safe to delete outright.
export {};
