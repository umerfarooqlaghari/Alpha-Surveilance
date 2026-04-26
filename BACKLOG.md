# Alpha Surveillance — Technical Backlog

> Generated from the system audit on 2026-03-04.
> For items marked with 🔴 tackle them before a production / customer demo.

---

## 🔴 High Priority

### 1. Outbox Dead-Letter Handling

**File:** `BackgroundServices/OutboxProcessorService.cs`

The outbox processor retries failed outbox messages forever. Poison messages (e.g., a malformed hub notification) will block continuous processing.

**Fix:** After `RetryCount >= 5`, mark the message as `DeadLettered` and skip it in the main query. Surface these in a SuperAdmin "system health" page or just log them at `Error` level.

---

### 2. Email-Alert Cooldown Survives Only In-Process

**File:** `Services/ViolationService.cs`

```csharp
memoryCache.Set(cacheKey, true, TimeSpan.FromMinutes(5));
```

The 5-minute per-camera email cooldown is stored in `IMemoryCache`, which is cleared on every service restart. A machine restart could send duplicate alert emails immediately after.

**Fix:** Store the cooldown in the DB (`LastAlertSentAt` column on `CameraViolationType`) or use Redis via `IDistributedCache`.

---

### 3. `TriggerLabels` Stored as Raw Comma-Separated String

**Files:** `Core/Entities/SopViolationType.cs`, `Core/Entities/CameraViolationType.cs`

Storing labels as `"helmet, vest, glove"` means searching by label requires `LIKE` queries, and a whitespace/case typo silently breaks filtering.

**Fix:** Normalise on write (already partially in place on the frontend), and consider a proper `SopViolationLabel` normalisation table in a future schema revision. At minimum add a DB-level `CHECK` constraint and an EF `ValueConverter` to normalise on save.

---

### 4. `CameraViolationType.TriggerLabels` Fallback Could Silently Break

**File:** `Controllers/CamerasController.cs`

If `SopViolationType.TriggerLabels` is `null` and `CameraViolationType.TriggerLabels` is also null, the Vision Service receives an empty string and matches 0 labels.

**Fix:** Add a defensive check in the internal endpoint: if both are null, pass a sentinel value (e.g. `"*"`) to instruct the Vision Service to match everything — and handle that in `main.py`.

---

### 5. Internal Camera API Uses `[AllowAnonymous]`

**File:** `Controllers/CamerasController.cs`

The `/api/cameras/internal/active` endpoint relies solely on middleware API-key validation. If the middleware is ever bypassed or misconfigured, decrypted RTSP URLs are exposed.

**Fix:** Add a dedicated `[Authorize(Policy = "InternalServiceOnly")]` policy using a custom `IAuthorizationHandler` backed by the API key header, so the controller layer is independently guarded.

---

## 🟡 Medium Priority

### 6. `ViolationService.GetViolationAsync` N+1 Camera Fetch

**File:** `Services/ViolationService.cs` ~line 43

Single-violation lookups fetch ALL cameras for a tenant just to resolve a camera name.

**Fix:** Store `CameraName` directly on the `Violation` record at write time in `ProcessViolationsBulkAsync` (camera data is already available there).

---

### 7. Two `ProcessViolationsBulkAsync` Overloads Diverge Silently

**File:** `Services/ViolationService.cs` ~line 136 & 230

The `ViolationPayload` overload performs SOP enrichment; the `ViolationRequest` overload does not. Calling the wrong one saves violations without `SopViolationTypeId`.

**Fix:** Unify into a single overload and extract enrichment into a shared private method.

---

### 8. No `ModelIdentifier` Validation in the SOP Form

**File:** `surveilance-ui/src/app/admin/sops/components/ViolationFormModal.tsx`

A SuperAdmin can type any string as the model identifier. A typo means silently zero detections.

**Fix:** Fetch a list of registered model identifiers from the Vision Service's `/api/models` endpoint and populate a dropdown or at least add a validation warning if the value doesn't match any known model.

---

### 9. Camera Config Hot-Reload Latency

**File:** `vision-inference-service/main.py` (polling loop)

The Vision Service polls for camera config at a fixed interval. Removing or pausing a camera in the UI doesn't take effect until the next poll, which could be 60+ seconds.

**Fix:** Add a lightweight HTTP webhook endpoint to the Vision Service (e.g. `POST /reload-cameras`) that the Violation Management API calls immediately when camera state changes.

---

### 10. Vision Service `SimpleIouTracker` State Is In-Memory Only

**File:** `vision-inference-service/rtsp/violation_manager.py`

Track state resets on every restart, potentially causing a burst of false "new violation" notifications immediately after a restart.

**Fix:** For production, persist active track state (or at minimum clear the hysteresis window state) to Redis. Alternatively, add a startup grace period before reporting violations.

---

## 🟢 UX / Frontend

### 11. No Persistent Notification Inbox

Users who are offline when a violation fires have no record of missed WebSocket alerts.

**Fix:** Add a `NotificationInbox` table (linked to `HubNotification` outbox messages), a bell icon with an unread count in the nav bar, and a slide-out panel listing recent alerts.

---

### 12. No Confirmation for SOP-Association Removal

Removing an SOP from a tenant silently disables all camera violation models using that SOP — with no warning or impact summary shown in the UI.

**Fix:** Before confirming the removal, query and display how many cameras and violation models will be affected.

---

### 13. Camera Table: TriggerLabels Not Visible at a Glance

In the SuperAdmin camera management table, you can see active violation models but not what labels are set (default vs. overridden).

**Fix:** Add a hover tooltip or expandable row showing label overrides per model in the camera table.

---

### 14. `BulkUploadModal.tsx` Has a Pre-Existing Type Error

**File:** `surveilance-ui/src/components/employees/BulkUploadModal.tsx` ~line 41-42

`data` property doesn't exist on type `BulkImportResponse`. This will likely cause a runtime error when bulk-uploading employees.

**Fix:** Update the `BulkImportResponse` type to include the `data` property, or remove the invalid destructure.

---

## 🔵 Architecture / Long-Term

### 15. Single DB Context for Everything

All entities (config, violations, outbox, employees) share `AppViolationDbContext`. As violation volume grows, the high-write `Violations` and `OutboxMessages` tables will contend with config table reads.

**Fix:** Extract `Violations` + `OutboxMessages` into a dedicated `ViolationsDbContext` and database (or at minimum a separate schema).

---

### 16. No Health-Check Endpoint on Vision Service

The Vision Service has no `/health` endpoint. Aspire can't report if streams are actually running vs. the process just started.

**Fix:** Add a FastAPI `/health` route returning `{ "status": "ok", "active_streams": N, "models_loaded": ["model_a", "model_b"] }`.

---

### 17. JWT Tokens in `localStorage`

If JWTs are stored in `localStorage` they are accessible to any XSS payload. This is a security risk for a surveillance product.

**Fix:** Migrate to `httpOnly` cookies for token storage on a dedicated auth endpoint. Update the Next.js middleware to read from the cookie instead.

---

## 🧠 Advanced AI & Deep Learning (Future Iterations)

### 18. Object Tracking & Re-Identification (ReID)

**Context:** The current AI pipeline uses a stateless YOLO model with a naive Intersection-over-Union (IoU) track builder. This causes the same person to trigger multiple violations if they leave the camera's angle of view (40° -> 150°) and return.
*   **Fix 1 (Tracking):** Implement SOTA tracking algorithms like **ByteTrack** or **BoT-SORT** in `main.py` to maintain consistent IDs through erratic movement and partial occlusions (e.g. walking behind pillars).
*   **Fix 2 (Deep ReID):** Introduce a "Feature Vector" extraction model to generate digital fingerprints of intruders base on clothing/build. If a person leaves standard frame bounds and returns 5 minutes later, the system will recognize the fingerprint and deduplicate the alert automatically.

---

### 19. Virtual Fencing & Zone-Based Logic

**Context:** Raw angle matrices are brittle in varied surveillance environments. 
*   **Virtual Polygons:** Update the `surveilance-ui` to allow users to draw physical Polygon Zones over the camera feed preview.
*   **Entry/Exit State:** Pass these polygon coordinates to the Python Vision Inference service. Trigger violations on **Line Crossing Events** or **Dwell Time Analysis** (e.g., "Trigger only if `ID_01` stays in Zone A for >10 seconds") rather than instantaneous detection presence.

---

### 20. Multi-SOP Bounding Box Colors

**Context:** Currently, `cv2.rectangle` in `main.py` is hardcoded to Red `(0, 0, 255)`. If a person triggers "Missing Helmet" and "Restricted Area" simultaneously, the red boxes overlap completely in the S3 snapshot.
*   **Improvement:** Assign unique RGB hexadecimal colors to different SOP Violation Types (e.g., Red for Security, Orange for PPE, Yellow for Loitering). 
*   **Implementation:** When the system evaluates multiple violations for a single tracked detection, it should draw the bounding box using the highest severity color, or tile the labels at the top of the box.

---

### 21. Violation Manager State Persistence (Redis)

**Context:** Currently, `violation_manager.py` stores the Cooldown State Machine (dict of `Track_ID -> Status`) in ephemeral Python RAM.
*   **Flaw:** If the Docker container restarts, the memory is wiped. A person who was secretly on a 5-minute cooldown will instantly trigger a brand-new violation notification when the container reboots.
*   **Improvement:** Move the `camera_states` and `_global_last_trigger` dictionaries out of Python memory and into a centralized Redis instance (which can be spun up easily via `.NET Aspire`).
