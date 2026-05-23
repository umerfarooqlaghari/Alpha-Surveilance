'use client';

import React, { useEffect, useMemo, useRef, useState } from 'react';
import { Upload, Camera as CameraIcon, Undo2, Trash2, Code2, MapPin, AlertTriangle } from 'lucide-react';

// Lazy-load the WHEP web component on the client only (matches WebRTCPlayer).
if (typeof window !== 'undefined') {
    // @ts-ignore
    import('@eyevinn/whep-video-component').catch(console.error);
}

type GeofenceMode = 'entry' | 'exit';
type Anchor = 'bottom_center' | 'centroid' | 'top_center';

interface PolygonEditorProps {
    /** Current raw JSON value (round-tripped to/from `ruleConfigurationJson`). */
    value: string;
    /** Called with the new canonical JSON whenever the user edits. */
    onChange: (json: string) => void;
    /** Optional WHEP URL — when present, a "Capture from live" button is shown. */
    whepUrl?: string | null;
    /** Optional preset rule shape — when present, the editor emits dwell instead of geofence. */
    ruleType?: 'geofence' | 'dwell';
    /** Initial dwell duration (seconds) when editing/creating a dwell rule. */
    initialDwellSeconds?: number;
}

interface ParsedConfig {
    points: [number, number][];   // normalized [0..1]
    mode: GeofenceMode;
    anchor: Anchor;
    durationS?: number;            // dwell rules only
    unknownType?: string;          // non-geofence/dwell policy types we can't visualize
    migrated?: boolean;            // true if we auto-converted pixel→normalized from source_frame_size
    raw?: string;                  // pristine raw text for invalid JSON
}

const MAX_VERTICES = 64;

/**
 * Returns true if any two non-adjacent edges of the polygon (when closed by
 * joining the last vertex back to the first) cross each other. Used to warn
 * the SuperAdmin before saving a bowtie polygon — the server would accept it
 * (only topology is checked at the vision worker via Shapely make_valid), but
 * the resulting MultiPolygon is rejected fail-closed and the user gets zero
 * alerts with no UI signal. We flag it here.
 *
 * O(n^2) over a max of 64 vertices = ~2000 ops, trivial on every state change.
 */
function polygonSelfIntersects(pts: [number, number][]): boolean {
    if (pts.length < 4) return false;
    const n = pts.length;
    for (let i = 0; i < n; i++) {
        const a1 = pts[i];
        const a2 = pts[(i + 1) % n];
        for (let j = i + 2; j < n; j++) {
            // Skip adjacent edges (they legitimately share a vertex).
            if ((j + 1) % n === i) continue;
            const b1 = pts[j];
            const b2 = pts[(j + 1) % n];
            if (segmentsIntersect(a1, a2, b1, b2)) return true;
        }
    }
    return false;
}

function segmentsIntersect(
    p1: [number, number], p2: [number, number],
    p3: [number, number], p4: [number, number],
): boolean {
    const d1 = cross(p4, p3, p1);
    const d2 = cross(p4, p3, p2);
    const d3 = cross(p2, p1, p3);
    const d4 = cross(p2, p1, p4);
    if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
        ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) {
        return true;
    }
    return false; // endpoint-touching / collinear are not flagged for our purposes
}

function cross(a: [number, number], b: [number, number], c: [number, number]): number {
    return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
}

function parseValue(raw: string): ParsedConfig {
    const fallback: ParsedConfig = { points: [], mode: 'entry', anchor: 'bottom_center' };
    if (!raw || !raw.trim()) return fallback;
    try {
        const obj = JSON.parse(raw);
        if (!obj || typeof obj !== 'object') return { ...fallback, raw };
        const type = String(obj.type || '').toLowerCase();
        if (type && type !== 'geofence' && type !== 'dwell') {
            return { ...fallback, unknownType: String(obj.type), raw };
        }
        const space = String(obj.coordinate_space || 'pixel').toLowerCase();
        const poly = Array.isArray(obj.polygon) ? obj.polygon : [];

        // Gap #2: legacy pixel-coord migration. If the saved JSON includes
        // `source_frame_size: [w, h]` and is in pixel space, we can convert
        // back to normalized losslessly so existing pixel-coord configs are
        // editable instead of forcing a redraw.
        let frameW: number | undefined;
        let frameH: number | undefined;
        if (Array.isArray(obj.source_frame_size) && obj.source_frame_size.length === 2) {
            const [w, h] = obj.source_frame_size.map(Number);
            if (Number.isFinite(w) && Number.isFinite(h) && w > 0 && h > 0) {
                frameW = w;
                frameH = h;
            }
        }

        const points: [number, number][] = [];
        let migrated = false;
        for (const v of poly) {
            if (!Array.isArray(v) || v.length !== 2) continue;
            const [x, y] = v.map(Number);
            if (!Number.isFinite(x) || !Number.isFinite(y)) continue;
            if (space === 'normalized') {
                points.push([Math.min(1, Math.max(0, x)), Math.min(1, Math.max(0, y))]);
            } else if (frameW && frameH) {
                migrated = true;
                points.push([
                    Math.min(1, Math.max(0, x / frameW)),
                    Math.min(1, Math.max(0, y / frameH)),
                ]);
            }
            // else: pixel coords without frame size — can't recover; skip and flag below.
        }
        return {
            points,
            mode: (obj.mode === 'exit' ? 'exit' : 'entry'),
            anchor: ['centroid', 'top_center', 'bottom_center'].includes(obj.anchor) ? obj.anchor : 'bottom_center',
            durationS: typeof obj.duration_s === 'number' && obj.duration_s > 0 ? obj.duration_s : undefined,
            unknownType: poly.length && !points.length ? 'pixel-coords' : undefined,
            migrated,
            raw,
        };
    } catch {
        return { ...fallback, raw };
    }
}

function serialize(
    state: { points: [number, number][]; mode: GeofenceMode; anchor: Anchor; durationS?: number },
    sourceFrame: { w: number; h: number } | null,
    ruleType: 'geofence' | 'dwell',
): string {
    if (state.points.length < 3) return '';
    const payload: Record<string, unknown> = {
        type: ruleType,
        polygon: state.points.map(([x, y]) => [Number(x.toFixed(4)), Number(y.toFixed(4))]),
        coordinate_space: 'normalized',
        mode: state.mode,
        anchor: state.anchor,
    };
    if (ruleType === 'dwell') {
        payload.duration_s = Math.max(0.5, Math.min(3600, state.durationS ?? 5));
    }
    // Embed source frame size when known so legacy pixel-coord configs are
    // reversible. Pure metadata — vision worker ignores it.
    if (sourceFrame) {
        payload.source_frame_size = [sourceFrame.w, sourceFrame.h];
    }
    return JSON.stringify(payload);
}

export default function PolygonEditor({ value, onChange, whepUrl, ruleType = 'geofence', initialDwellSeconds }: PolygonEditorProps) {
    const initial = useMemo(() => parseValue(value), []); // eslint-disable-line react-hooks/exhaustive-deps
    const [points, setPoints] = useState<[number, number][]>(initial.points);
    const [mode, setMode] = useState<GeofenceMode>(initial.mode);
    const [anchor, setAnchor] = useState<Anchor>(initial.anchor);
    const [durationS, setDurationS] = useState<number>(initial.durationS ?? initialDwellSeconds ?? 10);
    const [imgSrc, setImgSrc] = useState<string | null>(null);
    const [imgDims, setImgDims] = useState<{ w: number; h: number } | null>(null);
    const [showRaw, setShowRaw] = useState(false);
    const [showLive, setShowLive] = useState(false);
    const [captureError, setCaptureError] = useState<string | null>(null);

    // Drag-to-move state (gap #3). `dragIdx` is the vertex being moved;
    // `wasDragged` distinguishes a click (delete) from a drag-end on the
    // same circle, so the user doesn't accidentally lose a vertex after
    // nudging it.
    const dragIdx = useRef<number | null>(null);
    const wasDragged = useRef(false);

    // Tracks whether the user has interacted with the editor. We must NOT
    // emit onChange on the initial mount: if the existing config is a
    // non-geofence policy (anomaly, custom) or used pixel coordinates, the
    // editor cannot reconstruct it visually and would otherwise wipe the
    // stored policy with an empty string before the user does anything.
    const userTouched = useRef(false);
    // Issue #5: keep a stable ref to onChange so the serialisation effect
    // never closes over a stale callback when the parent re-renders.
    const onChangeRef = useRef(onChange);
    useEffect(() => { onChangeRef.current = onChange; });

    const fileInputRef = useRef<HTMLInputElement>(null);
    const liveContainerRef = useRef<HTMLDivElement>(null);
    const svgRef = useRef<SVGSVGElement>(null);

    const selfIntersects = useMemo(() => polygonSelfIntersects(points), [points]);
    const isValid = points.length >= 3 && !selfIntersects;

    // Emit only after the user has actually modified the editor state. When
    // the polygon is invalid (too few vertices OR self-intersecting), emit
    // empty string so the parent form treats it as "no policy" — same fail-
    // closed semantics the server uses for unrepairable polygons.
    useEffect(() => {
        if (!userTouched.current) return;
        if (!isValid) {
            onChangeRef.current('');
            return;
        }
        onChangeRef.current(serialize({ points, mode, anchor, durationS }, imgDims, ruleType));
    }, [points, mode, anchor, durationS, imgDims, isValid, ruleType]);

    const handleFile = (file: File) => {
        if (!file.type.startsWith('image/')) {
            setCaptureError('Please choose an image file.');
            return;
        }
        const reader = new FileReader();
        reader.onload = () => {
            setImgSrc(String(reader.result));
            setCaptureError(null);
        };
        reader.readAsDataURL(file);
    };

    const handleCaptureLive = () => {
        const container = liveContainerRef.current;
        if (!container) {
            setCaptureError('Live preview is not mounted yet.');
            return;
        }
        const video = container.querySelector('video') as HTMLVideoElement | null;
        if (!video || !video.videoWidth || !video.videoHeight) {
            setCaptureError('Live stream not ready. Wait a few seconds and try again.');
            return;
        }
        try {
            const canvas = document.createElement('canvas');
            canvas.width = video.videoWidth;
            canvas.height = video.videoHeight;
            const ctx = canvas.getContext('2d');
            if (!ctx) throw new Error('canvas 2d unavailable');
            ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
            const dataUrl = canvas.toDataURL('image/jpeg', 0.85);
            setImgSrc(dataUrl);
            setCaptureError(null);
            setShowLive(false);
        } catch (e: unknown) {
            const msg = e instanceof Error ? e.message : String(e);
            setCaptureError(
                `Capture failed (${msg}). The stream may be CORS-restricted — upload an image instead.`
            );
        }
    };

    const handleImgLoad = (e: React.SyntheticEvent<HTMLImageElement>) => {
        const img = e.currentTarget;
        setImgDims({ w: img.naturalWidth, h: img.naturalHeight });
    };

    const clientToNorm = (clientX: number, clientY: number): [number, number] | null => {
        if (!svgRef.current) return null;
        const rect = svgRef.current.getBoundingClientRect();
        const x = (clientX - rect.left) / rect.width;
        const y = (clientY - rect.top) / rect.height;
        return [Math.min(1, Math.max(0, x)), Math.min(1, Math.max(0, y))];
    };

    const handleCanvasClick = (e: React.MouseEvent<SVGSVGElement>) => {
        if (!imgSrc) return;
        if (wasDragged.current) {
            wasDragged.current = false;
            return;
        }
        if (points.length >= MAX_VERTICES) {
            setCaptureError(`Polygon vertex cap is ${MAX_VERTICES}.`);
            return;
        }
        const norm = clientToNorm(e.clientX, e.clientY);
        if (!norm) return;
        userTouched.current = true;
        setPoints(prev => [...prev, norm]);
    };

    const handleVertexMouseDown = (e: React.MouseEvent, idx: number) => {
        e.stopPropagation();
        dragIdx.current = idx;
        wasDragged.current = false;
    };

    const handleSvgMouseMove = (e: React.MouseEvent<SVGSVGElement>) => {
        if (dragIdx.current === null) return;
        const norm = clientToNorm(e.clientX, e.clientY);
        if (!norm) return;
        const i = dragIdx.current;
        wasDragged.current = true;
        userTouched.current = true;
        setPoints(prev => prev.map((p, j) => (j === i ? norm : p)));
    };

    const handleSvgMouseUp = () => {
        if (dragIdx.current !== null) {
            dragIdx.current = null;
        }
    };

    const handleVertexClick = (e: React.MouseEvent, idx: number) => {
        e.stopPropagation();
        if (wasDragged.current) {
            wasDragged.current = false;
            return;
        }
        userTouched.current = true;
        setPoints(prev => prev.filter((_, i) => i !== idx));
    };

    const undo = () => {
        userTouched.current = true;
        setPoints(prev => prev.slice(0, -1));
    };
    const clearAll = () => {
        userTouched.current = true;
        setPoints([]);
    };

    const polygonAttr = points.map(([x, y]) => `${x * 100},${y * 100}`).join(' ');
    const strokeColor = selfIntersects ? 'rgb(239, 68, 68)' : 'rgb(59, 130, 246)';
    const fillColor = selfIntersects ? 'rgba(239, 68, 68, 0.2)' : 'rgba(59, 130, 246, 0.2)';

    return (
        <div className="space-y-3">
            {/* Toolbar */}
            <div className="flex flex-wrap items-center gap-2">
                <button
                    type="button"
                    onClick={() => fileInputRef.current?.click()}
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold bg-white border border-gray-300 rounded-lg hover:bg-gray-50 text-gray-700"
                >
                    <Upload className="w-3.5 h-3.5" />
                    Upload image
                </button>
                <input
                    ref={fileInputRef}
                    type="file"
                    accept="image/*"
                    className="hidden"
                    onChange={(e) => {
                        const f = e.target.files?.[0];
                        if (f) handleFile(f);
                        e.target.value = '';
                    }}
                />

                {whepUrl ? (
                    <button
                        type="button"
                        onClick={() => setShowLive(s => !s)}
                        className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold bg-white border border-gray-300 rounded-lg hover:bg-gray-50 text-gray-700"
                    >
                        <CameraIcon className="w-3.5 h-3.5" />
                        {showLive ? 'Hide live' : 'Capture from live'}
                    </button>
                ) : null}

                <div className="flex-1" />

                <button
                    type="button"
                    onClick={undo}
                    disabled={points.length === 0}
                    className="inline-flex items-center gap-1 px-2 py-1.5 text-xs font-semibold bg-white border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed text-gray-700"
                >
                    <Undo2 className="w-3.5 h-3.5" />
                    Undo
                </button>
                <button
                    type="button"
                    onClick={clearAll}
                    disabled={points.length === 0}
                    className="inline-flex items-center gap-1 px-2 py-1.5 text-xs font-semibold bg-white border border-gray-300 rounded-lg hover:bg-red-50 disabled:opacity-40 disabled:cursor-not-allowed text-red-600"
                >
                    <Trash2 className="w-3.5 h-3.5" />
                    Clear
                </button>
                <button
                    type="button"
                    onClick={() => setShowRaw(s => !s)}
                    className="inline-flex items-center gap-1 px-2 py-1.5 text-xs font-semibold bg-white border border-gray-300 rounded-lg hover:bg-gray-50 text-gray-700"
                >
                    <Code2 className="w-3.5 h-3.5" />
                    {showRaw ? 'Hide JSON' : 'Show JSON'}
                </button>
            </div>

            {/* Live preview (mounted only when toggled) */}
            {showLive && whepUrl ? (
                <div className="border border-gray-300 rounded-lg overflow-hidden bg-black">
                    <div ref={liveContainerRef} className="aspect-video">
                        {React.createElement('whep-video', {
                            src: whepUrl,
                            autoplay: 'true',
                            muted: 'true',
                            playsinline: 'true',
                            crossorigin: 'anonymous',
                            style: { width: '100%', height: '100%' },
                        })}
                    </div>
                    <div className="flex justify-end p-2 bg-gray-900">
                        <button
                            type="button"
                            onClick={handleCaptureLive}
                            className="px-3 py-1.5 text-xs font-semibold bg-blue-600 text-white rounded hover:bg-blue-700"
                        >
                            Snap this frame
                        </button>
                    </div>
                </div>
            ) : null}

            {captureError ? (
                <p className="text-[11px] text-red-600 italic">{captureError}</p>
            ) : null}

            {/* Canvas + overlay */}
            {imgSrc ? (
                <div className="relative w-full bg-gray-900 border border-gray-300 rounded-lg overflow-hidden select-none">
                    {/* eslint-disable-next-line @next/next/no-img-element */}
                    <img
                        src={imgSrc}
                        alt="frame"
                        onLoad={handleImgLoad}
                        className="block w-full h-auto pointer-events-none"
                        draggable={false}
                    />
                    <svg
                        ref={svgRef}
                        viewBox="0 0 100 100"
                        preserveAspectRatio="none"
                        onClick={handleCanvasClick}
                        onMouseMove={handleSvgMouseMove}
                        onMouseUp={handleSvgMouseUp}
                        onMouseLeave={handleSvgMouseUp}
                        className="absolute inset-0 w-full h-full cursor-crosshair"
                    >
                        {points.length >= 2 ? (
                            <polyline
                                points={polygonAttr}
                                fill={points.length >= 3 ? fillColor : 'none'}
                                stroke={strokeColor}
                                strokeWidth={0.3}
                                vectorEffect="non-scaling-stroke"
                            />
                        ) : null}
                        {points.length >= 3 ? (
                            <line
                                x1={points[points.length - 1][0] * 100}
                                y1={points[points.length - 1][1] * 100}
                                x2={points[0][0] * 100}
                                y2={points[0][1] * 100}
                                stroke={strokeColor}
                                strokeWidth={0.3}
                                strokeDasharray="1,1"
                                vectorEffect="non-scaling-stroke"
                            />
                        ) : null}
                        {points.map(([x, y], i) => (
                            <circle
                                key={i}
                                cx={x * 100}
                                cy={y * 100}
                                r={1.2}
                                fill="white"
                                stroke={strokeColor}
                                strokeWidth={0.3}
                                vectorEffect="non-scaling-stroke"
                                onMouseDown={(e) => handleVertexMouseDown(e, i)}
                                onClick={(e) => handleVertexClick(e, i)}
                                style={{ cursor: 'grab' }}
                            />
                        ))}
                    </svg>
                </div>
            ) : (
                <div className="border-2 border-dashed border-gray-300 rounded-lg p-8 text-center text-xs text-gray-500 bg-gray-50">
                    Upload an image{whepUrl ? ' or capture from the live stream' : ''} to start drawing the zone.
                </div>
            )}

            <p className="text-[10px] text-gray-500">
                Click to add a vertex. Drag a vertex to reposition it; click without dragging to remove it.
                Minimum 3, maximum {MAX_VERTICES}. Coordinates are stored normalized so the zone survives resolution changes.
                {imgDims ? <> · source frame: {imgDims.w}×{imgDims.h}</> : null}
            </p>

            {/* Self-intersection warning (gap #1). When the polygon edges
                cross, the server-side Shapely make_valid() rejects it as a
                MultiPolygon and the vision worker emits zero alerts. Surface
                this before save so the SuperAdmin notices. */}
            {selfIntersects ? (
                <div className="flex items-start gap-2 p-2.5 bg-red-50 border border-red-200 rounded-lg">
                    <AlertTriangle className="w-4 h-4 text-red-600 flex-shrink-0 mt-0.5" />
                    <div className="text-[11px] text-red-700">
                        <p className="font-semibold">Polygon edges cross — this zone cannot be enforced.</p>
                        <p className="mt-0.5">Drag a vertex until no two edges intersect. The vision worker rejects self-intersecting polygons and emits zero alerts.</p>
                    </div>
                </div>
            ) : null}

            {/* Mode + anchor */}
            <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                <div>
                    <label className="block text-[10px] font-bold text-gray-700 uppercase mb-1.5 flex items-center gap-1">
                        <MapPin className="w-3 h-3" />
                        Zone mode
                    </label>
                    <select
                        value={mode}
                        onChange={(e) => {
                            userTouched.current = true;
                            setMode(e.target.value as GeofenceMode);
                        }}
                        className="w-full px-3 py-2 text-xs bg-white border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-400 outline-none text-black"
                    >
                        <option value="entry">Restricted — alert if INSIDE</option>
                        <option value="exit">Permitted — alert if OUTSIDE</option>
                    </select>
                </div>
                <div>
                    <label className="block text-[10px] font-bold text-gray-700 uppercase mb-1.5">
                        Reference point
                    </label>
                    <select
                        value={anchor}
                        onChange={(e) => {
                            userTouched.current = true;
                            setAnchor(e.target.value as Anchor);
                        }}
                        className="w-full px-3 py-2 text-xs bg-white border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-400 outline-none text-black"
                    >
                        <option value="bottom_center">Bottom-center (feet position — people, vehicles)</option>
                        <option value="centroid">Centroid (general object position)</option>
                        <option value="top_center">Top-center (head position — hairnet, helmet)</option>
                    </select>
                </div>
            </div>

            {/* Dwell duration — only when the parent picked the dwell rule type. */}
            {ruleType === 'dwell' ? (
                <div>
                    <label className="block text-[10px] font-bold text-gray-700 uppercase mb-1.5">
                        Dwell duration (seconds)
                    </label>
                    <input
                        type="number"
                        min={0.5}
                        max={3600}
                        step={0.5}
                        value={durationS}
                        onChange={(e) => {
                            userTouched.current = true;
                            const v = Number(e.target.value);
                            if (Number.isFinite(v) && v > 0) setDurationS(Math.min(3600, Math.max(0.5, v)));
                        }}
                        className="w-full px-3 py-2 text-xs bg-white border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-400 outline-none text-black"
                    />
                    <p className="text-[10px] text-gray-500 mt-1">
                        Alert fires only when a subject stays in the zone continuously for at least this long. Cuts down on transient false positives from people walking through.
                    </p>
                    <p className="text-[10px] text-amber-600 mt-1 font-medium">
                        I-1: Effective first-alert latency is ~{(durationS + 3).toFixed(1)} s — {durationS} s dwell threshold plus 3 confirmation frames (≈3 s at default 1 FPS). Higher per-camera FPS reduces the confirmation delay.
                    </p>
                </div>
            ) : null}

            {/* Validation summary */}
            <div className="flex items-center justify-between text-[11px]">
                <span className={isValid ? 'text-green-700' : selfIntersects ? 'text-red-600' : 'text-amber-600'}>
                    {isValid
                        ? `Polygon ready — ${points.length} vertex${points.length === 1 ? '' : 'es'}${ruleType === 'dwell' ? ` · ${durationS}s dwell` : ''}.`
                        : selfIntersects
                            ? `Polygon is self-intersecting — fix before saving.`
                            : `Need at least 3 vertices (currently ${points.length}).`}
                </span>
                {initial.migrated ? (
                    <span className="text-blue-600 italic">
                        Migrated from legacy pixel coords using stored frame size.
                    </span>
                ) : initial.unknownType ? (
                    <span className="text-amber-600 italic">
                        Existing config uses {initial.unknownType === 'pixel-coords' ? 'pixel coordinates' : `type "${initial.unknownType}"`}; will be rewritten as normalized {ruleType} on save.
                    </span>
                ) : null}
            </div>

            {showRaw ? (
                <pre className="text-[10px] bg-gray-50 border border-gray-200 rounded p-2 overflow-x-auto text-gray-700 font-mono">
                    {isValid ? JSON.stringify(JSON.parse(serialize({ points, mode, anchor, durationS }, imgDims, ruleType)), null, 2) : '— (no polygon yet) —'}
                </pre>
            ) : null}
        </div>
    );
}
