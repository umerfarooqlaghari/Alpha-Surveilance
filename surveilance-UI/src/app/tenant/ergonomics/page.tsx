'use client';

import React, { useState, useEffect, useMemo } from 'react';
import Image from 'next/image';
import {
    Activity,
    AlertTriangle,
    Shield,
    Camera,
    Play,
    Eye,
    Filter,
    Search,
    RefreshCw,
    Download,
    UserCheck,
    CheckCircle2,
    Sliders,
    Layers,
    Clock,
    X,
    TrendingUp,
    ChevronRight,
    MapPin,
    AlertCircle,
    User,
    Sparkles,
} from 'lucide-react';
import { jsPDF } from 'jspdf';
import {
    getErgonomicRisks,
    getErgonomicsCameraConfigs,
    updateCameraErgonomics,
    ErgonomicRiskItem,
    ErgonomicCameraConfig,
} from '@/lib/api/tenant/ergonomics';
import { getCameras } from '@/lib/api/tenant/cameras';
import type { CameraResponse } from '@/types/admin';

export default function ErgonomicsPage() {
    const [activeTab, setActiveTab] = useState<'risks' | 'cameras' | 'analytics'>('risks');

    // Data States
    const [risks, setRisks] = useState<ErgonomicRiskItem[]>([]);
    const [cameras, setCameras] = useState<ErgonomicCameraConfig[]>([]);
    const [rawCameras, setRawCameras] = useState<CameraResponse[]>([]);
    const [loading, setLoading] = useState(true);
    const [refreshing, setRefreshing] = useState(false);

    // Filter States
    const [searchTerm, setSearchTerm] = useState('');
    const [selectedCameraFilter, setSelectedCameraFilter] = useState('all');
    const [selectedRiskFilter, setSelectedRiskFilter] = useState('all');

    // Media Modal State
    const [selectedRiskModal, setSelectedRiskModal] = useState<ErgonomicRiskItem | null>(null);
    const [mediaType, setMediaType] = useState<'snapshot' | 'video'>('snapshot');

    // Camera Config Modal State
    const [configModalCamera, setConfigModalCamera] = useState<ErgonomicCameraConfig | null>(null);
    const [rebaThreshold, setRebaThreshold] = useState(7);
    const [actualWeightKg, setActualWeightKg] = useState(8.0);
    const [profileGender, setProfileGender] = useState<'Male' | 'Female'>('Male');
    const [savingConfig, setSavingConfig] = useState(false);

    const loadData = async (silent = false) => {
        if (!silent) setLoading(true);
        else setRefreshing(true);

        try {
            const [riskData, camConfigs, rawCams] = await Promise.all([
                getErgonomicRisks(),
                getErgonomicsCameraConfigs(),
                getCameras().catch(() => [] as CameraResponse[]),
            ]);
            setRisks(riskData);
            setCameras(camConfigs);
            setRawCameras(rawCams);
        } catch (err) {
            console.error('Failed to load ergonomics data:', err);
        } finally {
            setLoading(false);
            setRefreshing(false);
        }
    };

    useEffect(() => {
        loadData();
        const interval = setInterval(() => loadData(true), 15000);
        return () => clearInterval(interval);
    }, []);

    // Filtered risks
    const filteredRisks = useMemo(() => {
        return risks.filter(r => {
            const empName = r.employee ? `${r.employee.firstName} ${r.employee.lastName}` : '';
            const matchesSearch =
                r.cameraName.toLowerCase().includes(searchTerm.toLowerCase()) ||
                r.riskReason.toLowerCase().includes(searchTerm.toLowerCase()) ||
                empName.toLowerCase().includes(searchTerm.toLowerCase()) ||
                (r.employee?.employeeId && r.employee.employeeId.toLowerCase().includes(searchTerm.toLowerCase()));

            const matchesCam = selectedCameraFilter === 'all' || r.cameraId === selectedCameraFilter;
            const matchesRisk = selectedRiskFilter === 'all' || r.riskLevel === selectedRiskFilter;

            return matchesSearch && matchesCam && matchesRisk;
        });
    }, [risks, searchTerm, selectedCameraFilter, selectedRiskFilter]);

    // Handle Camera Config Save
    const handleSaveCameraConfig = async (enabled: boolean) => {
        if (!configModalCamera) return;
        const rawCam = rawCameras.find(c => c.id === configModalCamera.cameraId);
        if (!rawCam) return;

        setSavingConfig(true);
        try {
            await updateCameraErgonomics(rawCam, enabled, {
                rebaThreshold,
                actualWeightKg,
                profileGender,
            });
            await loadData(true);
            setConfigModalCamera(null);
        } catch (err) {
            alert('Failed to update ergonomics configuration for camera.');
        } finally {
            setSavingConfig(false);
        }
    };

    // 3-Page PDF Audit Report Generator
    const generatePdfReport = (risk: ErgonomicRiskItem) => {
        const doc = new jsPDF();
        const opId = risk.employee?.employeeId || (risk.trackId ? `TRACK-${risk.trackId}` : 'OP-WORKER');
        const opName = risk.employee ? `${risk.employee.firstName} ${risk.employee.lastName}` : 'Unidentified Workstation Operator';

        // ── PAGE 1: REBA Posture Audit ──────────────────────────────────────
        doc.setFillColor(33, 37, 41);
        doc.rect(0, 0, 210, 22, 'F');
        doc.setTextColor(255, 255, 255);
        doc.setFont('Helvetica', 'bold');
        doc.setFontSize(14);
        doc.text('Ergonomic Risk Evaluation - REBA & Posture Audit', 14, 14);

        doc.setFontSize(9);
        doc.setFont('Helvetica', 'normal');
        doc.setTextColor(100, 116, 139);
        let y = 32;
        doc.text(`Operator ID: ${opId} | Name: ${opName}`, 14, y);
        doc.text(`Workstation Camera: ${risk.cameraName} | Timestamp: ${new Date(risk.timestamp).toLocaleString()}`, 14, y + 6);
        doc.text(`AI Model: YOLO26 Medium Pose (yolo26m-pose.pt / S3 Connected)`, 14, y + 12);

        y += 22;
        doc.setDrawColor(226, 232, 240);
        doc.line(14, y, 196, y);
        y += 8;

        doc.setFont('Helvetica', 'bold');
        doc.setFontSize(12);
        doc.setTextColor(30, 41, 59);
        doc.text('1. REBA Posture Risk Assessment', 14, y);
        y += 8;

        doc.setFontSize(10);
        doc.text(`Final REBA Score: ${risk.rebaScore} / 15 (${risk.riskLevel})`, 14, y);
        y += 6;
        doc.setFont('Helvetica', 'normal');
        doc.setFontSize(9);
        doc.text(`Evaluation Summary: ${risk.riskReason}`, 14, y);
        y += 12;

        // Joint Angles Table
        doc.setFont('Helvetica', 'bold');
        doc.text('Body Segment', 14, y);
        doc.text('Measured Angle', 80, y);
        doc.text('REBA Part Score', 130, y);
        doc.text('Safety Status', 170, y);
        y += 4;
        doc.line(14, y, 196, y);
        y += 6;

        doc.setFont('Helvetica', 'normal');
        const joints = [
            ['Trunk (Torso)', `${risk.jointAngles.trunk}°`, `${risk.partScores.trunk} / 4`, risk.partScores.trunk >= 3 ? 'High Flexion' : 'Acceptable'],
            ['Neck (Cervical Spine)', `${risk.jointAngles.neck}°`, `${risk.partScores.neck} / 3`, risk.partScores.neck >= 2 ? 'Bent / Strained' : 'Neutral'],
            ['Upper Arm (Shoulder)', `${risk.jointAngles.upper_arm}°`, `${risk.partScores.upper_arm} / 6`, risk.partScores.upper_arm >= 3 ? 'Elevated' : 'Neutral'],
            ['Lower Arm (Elbow)', `${risk.jointAngles.lower_arm}°`, `${risk.partScores.lower_arm} / 2`, risk.partScores.lower_arm === 1 ? 'Optimal (60-100°)' : 'Strained'],
            ['Legs & Weight Bearing', `${risk.jointAngles.legs}°`, `${risk.partScores.legs} / 4`, risk.partScores.legs >= 2 ? 'Squat / Asymmetric' : 'Bilateral Support'],
            ['Wrist & Grip', `${risk.jointAngles.wrist}°`, `${risk.partScores.wrist} / 3`, risk.partScores.wrist >= 2 ? 'Deviated' : 'Neutral'],
        ];

        joints.forEach(row => {
            doc.text(row[0], 14, y);
            doc.text(row[1], 80, y);
            doc.text(row[2], 130, y);
            doc.text(row[3], 170, y);
            y += 6;
        });

        y += 14;
        doc.setFont('Helvetica', 'bold');
        doc.text('Action Level Guidance:', 14, y);
        y += 6;
        doc.setFont('Helvetica', 'normal');
        doc.text('• REBA 1-3: Negligible to Low Risk — No immediate corrective action required.', 14, y); y += 5;
        doc.text('• REBA 4-7: Medium Risk — Further investigation and work modifications recommended.', 14, y); y += 5;
        doc.text('• REBA 8-10: High Risk — Investigation and ergonomic change required soon.', 14, y); y += 5;
        doc.text('• REBA 11-15: Very High Risk — Immediate engineering intervention required.', 14, y);

        doc.setFontSize(8);
        doc.text('Page 1 of 3 - REBA Full Body Posture Audit', 105, 285, { align: 'center' });

        // ── PAGE 2: MMH Zone Matrix ─────────────────────────────────────────
        doc.addPage();
        doc.setFillColor(33, 37, 41);
        doc.rect(0, 0, 210, 22, 'F');
        doc.setTextColor(255, 255, 255);
        doc.setFont('Helvetica', 'bold');
        doc.setFontSize(14);
        doc.text('Manual Material Handling (MMH) Geometry Matrix', 14, 14);

        y = 35;
        doc.setTextColor(30, 41, 59);
        doc.setFontSize(12);
        doc.text('2. Lift Geometry & Reach Analysis', 14, y);
        y += 8;

        doc.setFontSize(10);
        doc.setFont('Helvetica', 'normal');
        doc.text(`Detected Vertical Zone: ${risk.mmh.zone}`, 14, y); y += 6;
        doc.text(`Horizontal Reach Classification: ${risk.mmh.reach} (${risk.niosh.horizontal_cm} cm reach)`, 14, y); y += 6;
        doc.text(`Vertical Lift Height: ${risk.niosh.vertical_cm} cm from floor level`, 14, y); y += 12;

        doc.setFont('Helvetica', 'bold');
        doc.text('Workstation Ergonomic Principles:', 14, y); y += 6;
        doc.setFont('Helvetica', 'normal');
        doc.text('- Maintain lifting activities between knuckle and elbow height (optimum power zone).', 14, y); y += 5;
        doc.text('- Eliminate reaching above shoulder height (> 140 cm) to protect rotator cuff tendons.', 14, y); y += 5;
        doc.text('- Reduce horizontal reach distances below 40 cm to minimize lumbar spine shear force.', 14, y);

        doc.setFontSize(8);
        doc.text('Page 2 of 3 - Manual Material Handling Zone Audit', 105, 285, { align: 'center' });

        // ── PAGE 3: NIOSH Lifting Equation ──────────────────────────────────
        doc.addPage();
        doc.setFillColor(33, 37, 41);
        doc.rect(0, 0, 210, 22, 'F');
        doc.setTextColor(255, 255, 255);
        doc.setFont('Helvetica', 'bold');
        doc.setFontSize(14);
        doc.text('NIOSH Lifting Equation (NLE) Audit', 14, 14);

        y = 35;
        doc.setTextColor(30, 41, 59);
        doc.setFontSize(12);
        doc.text('3. Biomechanical Safety Evaluation', 14, y);
        y += 8;

        doc.setFontSize(10);
        doc.setFont('Helvetica', 'normal');
        doc.text(`Recommended Weight Limit (RWL): ${risk.niosh.rwl_kg.toFixed(2)} kg`, 14, y); y += 6;
        doc.text(`Lifting Index (LI = Actual Load / RWL): ${risk.niosh.lifting_index.toFixed(2)}`, 14, y); y += 8;

        doc.setFont('Helvetica', 'bold');
        if (risk.niosh.is_safe) {
            doc.setTextColor(22, 101, 52);
            doc.text('SAFETY STATUS: SAFE (LI <= 1.0)', 14, y);
        } else {
            doc.setTextColor(185, 28, 28);
            doc.text('SAFETY STATUS: HAZARDOUS LOAD (LI > 1.0) - High Lower Back Stress', 14, y);
        }
        doc.setTextColor(30, 41, 59);
        y += 14;

        doc.text('Engineering Recommendations:', 14, y); y += 6;
        doc.setFont('Helvetica', 'normal');
        doc.text('1. Implement mechanical lift assist or scissor lift tables for lifts exceeding RWL.', 14, y); y += 5;
        doc.text('2. Relocate bins and parts storage closer to operator center of gravity.', 14, y); y += 5;
        doc.text('3. Introduce job rotation to minimize repetitive lumbar flexion cycles.', 14, y);

        doc.setFontSize(8);
        doc.text('Page 3 of 3 - NIOSH Lifting Equation Report', 105, 285, { align: 'center' });

        doc.save(`Ergonomic_Audit_${opId}_${new Date().toISOString().split('T')[0]}.pdf`);
    };

    return (
        <div className="flex-1 flex flex-col p-6 max-w-7xl mx-auto w-full overflow-y-auto">
            {/* Header */}
            <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 mb-6">
                <div>
                    <div className="flex items-center gap-3">
                        <div className="p-2.5 bg-blue-600 text-white rounded-2xl shadow-md shadow-blue-500/20">
                            <Activity className="w-6 h-6" />
                        </div>
                        <div>
                            <h1 className="text-2xl font-bold text-gray-900 tracking-tight">Ergonomics & Posture Auditor</h1>
                            <p className="text-xs text-gray-500 font-medium">
                                Real-Time REBA Skeletal Assessment & NIOSH Lifting Equation via YOLO26m-pose & Human Re-ID
                            </p>
                        </div>
                    </div>
                </div>

                <div className="flex items-center gap-2.5">
                    <button
                        onClick={() => loadData(true)}
                        disabled={refreshing}
                        className="flex items-center gap-2 px-4 py-2 bg-white border border-gray-200 text-gray-700 text-xs font-semibold rounded-xl hover:bg-gray-50 transition shadow-sm"
                    >
                        <RefreshCw className={`w-3.5 h-3.5 ${refreshing ? 'animate-spin' : ''}`} />
                        Refresh
                    </button>
                </div>
            </div>

            {/* Sub-Tabs */}
            <div className="flex gap-2 border-b border-gray-200 pb-3 mb-6">
                <button
                    onClick={() => setActiveTab('risks')}
                    className={`flex items-center gap-2 px-4 py-2 text-xs font-bold rounded-xl transition ${activeTab === 'risks'
                            ? 'bg-blue-600 text-white shadow-md shadow-blue-500/20'
                            : 'text-gray-500 hover:text-gray-900 hover:bg-gray-100'
                        }`}
                >
                    <AlertTriangle className="w-4 h-4" />
                    At-Risk Workers ({risks.length})
                </button>

                <button
                    onClick={() => setActiveTab('cameras')}
                    className={`flex items-center gap-2 px-4 py-2 text-xs font-bold rounded-xl transition ${activeTab === 'cameras'
                            ? 'bg-blue-600 text-white shadow-md shadow-blue-500/20'
                            : 'text-gray-500 hover:text-gray-900 hover:bg-gray-100'
                        }`}
                >
                    <Camera className="w-4 h-4" />
                    Workstations & RTSP Cameras ({cameras.length})
                </button>

                <button
                    onClick={() => setActiveTab('analytics')}
                    className={`flex items-center gap-2 px-4 py-2 text-xs font-bold rounded-xl transition ${activeTab === 'analytics'
                            ? 'bg-blue-600 text-white shadow-md shadow-blue-500/20'
                            : 'text-gray-500 hover:text-gray-900 hover:bg-gray-100'
                        }`}
                >
                    <TrendingUp className="w-4 h-4" />
                    Ergonomic Trends & Analytics
                </button>
            </div>

            {/* ── SUB-TAB 1: AT-RISK WORKERS FEED ───────────────────────────────── */}
            {activeTab === 'risks' && (
                <div className="space-y-4">
                    {/* Filters Bar */}
                    <div className="bg-white p-3.5 rounded-2xl border border-gray-200 shadow-sm flex flex-wrap items-center gap-3">
                        <div className="relative flex-1 min-w-[200px]">
                            <Search className="w-4 h-4 text-gray-400 absolute left-3 top-2.5" />
                            <input
                                type="text"
                                placeholder="Search worker, ID, camera, or risk reason..."
                                value={searchTerm}
                                onChange={e => setSearchTerm(e.target.value)}
                                className="w-full pl-9 pr-4 py-1.5 bg-gray-50 text-xs border border-gray-200 rounded-xl focus:outline-none focus:border-blue-500"
                            />
                        </div>

                        <select
                            value={selectedCameraFilter}
                            onChange={e => setSelectedCameraFilter(e.target.value)}
                            className="bg-gray-50 text-xs font-medium border border-gray-200 rounded-xl px-3 py-1.5 focus:outline-none"
                        >
                            <option value="all">All Cameras</option>
                            {cameras.map(c => (
                                <option key={c.cameraId} value={c.cameraId}>{c.cameraName}</option>
                            ))}
                        </select>

                        <select
                            value={selectedRiskFilter}
                            onChange={e => setSelectedRiskFilter(e.target.value)}
                            className="bg-gray-50 text-xs font-medium border border-gray-200 rounded-xl px-3 py-1.5 focus:outline-none"
                        >
                            <option value="all">All Risk Levels</option>
                            <option value="Very High Risk">Very High Risk (REBA 11+)</option>
                            <option value="High Risk">High Risk (REBA 8-10)</option>
                            <option value="Medium Risk">Medium Risk (REBA 4-7)</option>
                            <option value="Low Risk">Low Risk (REBA 1-3)</option>
                        </select>
                    </div>

                    {loading ? (
                        <div className="p-12 text-center text-gray-400 text-xs font-medium">
                            Scanning RTSP feeds for ergonomic risk events...
                        </div>
                    ) : filteredRisks.length === 0 ? (
                        <div className="bg-white rounded-2xl border border-gray-200 p-12 text-center shadow-sm">
                            <CheckCircle2 className="w-10 h-10 text-emerald-500 mx-auto mb-3" />
                            <h3 className="text-sm font-bold text-gray-800">No Posture Risks Detected</h3>
                            <p className="text-xs text-gray-500 max-w-md mx-auto mt-1">
                                Workstation operators are operating within safe REBA and NIOSH lifting tolerances, or no cameras have ergonomics rules triggered.
                            </p>
                        </div>
                    ) : (
                        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                            {filteredRisks.map(risk => {
                                const isExtreme = risk.rebaScore >= 11;
                                const isHigh = risk.rebaScore >= 8 && risk.rebaScore < 11;

                                return (
                                    <div
                                        key={risk.id}
                                        className="bg-white rounded-2xl border border-gray-200 shadow-sm p-4 hover:shadow-md transition flex flex-col justify-between"
                                    >
                                        <div>
                                            {/* Header with Human Re-ID */}
                                            <div className="flex items-center justify-between gap-3 mb-3">
                                                <div className="flex items-center gap-2.5">
                                                    <div className="relative w-9 h-9 rounded-full overflow-hidden bg-blue-50 border border-blue-200 flex-shrink-0 flex items-center justify-center">
                                                        <User className="w-5 h-5 text-blue-500" />
                                                    </div>
                                                    <div>
                                                        <div className="flex items-center gap-1.5">
                                                            <span className="text-xs font-bold text-gray-900">
                                                                {risk.employee ? `${risk.employee.firstName} ${risk.employee.lastName}` : 'Unidentified Worker'}
                                                            </span>
                                                            <span className="px-1.5 py-0.5 bg-blue-50 text-blue-700 text-[10px] font-bold rounded border border-blue-100">
                                                                Re-ID: {risk.employee?.employeeId || (risk.trackId ? `#${risk.trackId}` : 'Tracked')}
                                                            </span>
                                                        </div>
                                                        <p className="text-[11px] text-gray-500 flex items-center gap-1">
                                                            <Camera className="w-3 h-3 text-gray-400" /> {risk.cameraName} • {new Date(risk.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                                                        </p>
                                                    </div>
                                                </div>

                                                {/* Risk Tier Badge */}
                                                <div className={`px-2.5 py-1 rounded-xl text-xs font-extrabold flex items-center gap-1.5 ${isExtreme
                                                        ? 'bg-rose-50 text-rose-700 border border-rose-200'
                                                        : isHigh
                                                            ? 'bg-amber-50 text-amber-700 border border-amber-200'
                                                            : 'bg-emerald-50 text-emerald-700 border border-emerald-200'
                                                    }`}>
                                                    <AlertCircle className="w-3.5 h-3.5" />
                                                    REBA {risk.rebaScore}/15
                                                </div>
                                            </div>

                                            {/* Verbal Risk Reason */}
                                            <div className="p-2.5 rounded-xl bg-gray-50 border border-gray-100 text-xs font-medium text-gray-700 mb-3">
                                                <span className="font-bold text-gray-900">At Risk Due To:</span> {risk.riskReason}
                                            </div>

                                            {/* Key Metrics Grid */}
                                            <div className="grid grid-cols-3 gap-2 mb-3">
                                                <div className="p-2 rounded-xl bg-blue-50/50 border border-blue-100 text-center">
                                                    <span className="text-[10px] font-bold text-gray-500 uppercase block">Trunk Flex</span>
                                                    <span className="text-xs font-black text-gray-900">{risk.jointAngles.trunk}°</span>
                                                </div>
                                                <div className="p-2 rounded-xl bg-blue-50/50 border border-blue-100 text-center">
                                                    <span className="text-[10px] font-bold text-gray-500 uppercase block">NIOSH LI</span>
                                                    <span className={`text-xs font-black ${risk.niosh.is_safe ? 'text-emerald-700' : 'text-rose-700'}`}>
                                                        {risk.niosh.lifting_index.toFixed(2)} {risk.niosh.is_safe ? '✓' : '⚠️'}
                                                    </span>
                                                </div>
                                                <div className="p-2 rounded-xl bg-blue-50/50 border border-blue-100 text-center">
                                                    <span className="text-[10px] font-bold text-gray-500 uppercase block">Reach Zone</span>
                                                    <span className="text-xs font-black text-gray-900 truncate block">{risk.mmh.zone}</span>
                                                </div>
                                            </div>
                                        </div>

                                        {/* Actions */}
                                        <div className="flex items-center gap-2 pt-2 border-t border-gray-100 mt-2">
                                            <button
                                                onClick={() => {
                                                    setSelectedRiskModal(risk);
                                                    setMediaType(risk.videoClipUrl ? 'video' : 'snapshot');
                                                }}
                                                className="flex-1 flex items-center justify-center gap-1.5 py-1.5 bg-gray-100 hover:bg-gray-200 text-gray-700 text-xs font-bold rounded-xl transition"
                                            >
                                                {risk.videoClipUrl ? <Play className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
                                                {risk.videoClipUrl ? 'View Video Clip' : 'Inspect Snapshot'}
                                            </button>

                                            <button
                                                onClick={() => generatePdfReport(risk)}
                                                className="flex items-center gap-1.5 px-3 py-1.5 bg-blue-50 hover:bg-blue-100 text-blue-700 text-xs font-bold rounded-xl transition border border-blue-200"
                                                title="Generate 3-Page PDF Audit Report"
                                            >
                                                <Download className="w-3.5 h-3.5" />
                                                Report
                                            </button>
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                    )}
                </div>
            )}

            {/* ── SUB-TAB 2: WORKSTATION CAMERAS CONFIG ────────────────────────── */}
            {activeTab === 'cameras' && (
                <div className="space-y-4">
                    <div className="bg-white p-4 rounded-2xl border border-gray-200 shadow-sm flex items-center justify-between">
                        <div>
                            <h3 className="text-sm font-bold text-gray-900">Workstation Cameras Assigned to Ergonomics</h3>
                            <p className="text-xs text-gray-500">
                                Configure which camera feeds run YOLO26m-pose estimation and trigger postural risk alerts.
                            </p>
                        </div>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                        {cameras.map(cam => (
                            <div
                                key={cam.cameraId}
                                className="bg-white rounded-2xl border border-gray-200 shadow-sm p-4 flex flex-col justify-between"
                            >
                                <div>
                                    <div className="flex items-center justify-between mb-3">
                                        <div className="flex items-center gap-2">
                                            <div className={`w-2.5 h-2.5 rounded-full ${cam.hasErgonomicsActive ? 'bg-emerald-500 animate-pulse' : 'bg-gray-300'}`} />
                                            <span className="text-xs font-bold text-gray-900 truncate max-w-[160px]">{cam.cameraName}</span>
                                        </div>
                                        <span className={`px-2 py-0.5 text-[10px] font-bold rounded-full ${cam.hasErgonomicsActive ? 'bg-emerald-50 text-emerald-700 border border-emerald-200' : 'bg-gray-100 text-gray-500'}`}>
                                            {cam.hasErgonomicsActive ? 'Ergonomics Active' : 'Not Monitoring'}
                                        </span>
                                    </div>

                                    <div className="space-y-1.5 text-xs text-gray-600 mb-4 bg-gray-50 p-2.5 rounded-xl border border-gray-100">
                                        <div className="flex justify-between">
                                            <span className="text-gray-400">Location:</span>
                                            <span className="font-semibold text-gray-800">{cam.locationName || 'Main Floor'}</span>
                                        </div>
                                        <div className="flex justify-between">
                                            <span className="text-gray-400">REBA Alert Cutoff:</span>
                                            <span className="font-semibold text-gray-800">Score &ge; {cam.rebaThreshold}</span>
                                        </div>
                                        <div className="flex justify-between">
                                            <span className="text-gray-400">AI Pose Model:</span>
                                            <span className="font-semibold text-blue-600">yolo26m-pose.pt</span>
                                        </div>
                                        <div className="flex justify-between">
                                            <span className="text-gray-400">Workstation Load:</span>
                                            <span className="font-semibold text-gray-800">{cam.actualWeightKg} kg ({cam.profileGender})</span>
                                        </div>
                                    </div>
                                </div>

                                <button
                                    onClick={() => {
                                        setConfigModalCamera(cam);
                                        setRebaThreshold(cam.rebaThreshold);
                                        setActualWeightKg(cam.actualWeightKg);
                                        setProfileGender(cam.profileGender);
                                    }}
                                    className="w-full flex items-center justify-center gap-2 py-2 bg-gray-100 hover:bg-gray-200 text-gray-800 text-xs font-bold rounded-xl transition"
                                >
                                    <Sliders className="w-3.5 h-3.5" />
                                    Configure Camera
                                </button>
                            </div>
                        ))}
                    </div>
                </div>
            )}

            {/* ── SUB-TAB 3: ANALYTICS & TRENDS ─────────────────────────────────── */}
            {activeTab === 'analytics' && (
                <div className="space-y-4">
                    {/* Stat Cards */}
                    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                        <div className="bg-white p-4 rounded-2xl border border-gray-200 shadow-sm">
                            <span className="text-[11px] font-bold text-gray-400 uppercase">Monitored Incidents</span>
                            <h2 className="text-2xl font-black text-gray-900 mt-1">{risks.length}</h2>
                            <p className="text-[10px] text-emerald-600 mt-0.5">Live RTSP streams</p>
                        </div>
                        <div className="bg-white p-4 rounded-2xl border border-gray-200 shadow-sm">
                            <span className="text-[11px] font-bold text-gray-400 uppercase">Very High Risk (REBA 11+)</span>
                            <h2 className="text-2xl font-black text-rose-600 mt-1">
                                {risks.filter(r => r.rebaScore >= 11).length}
                            </h2>
                            <p className="text-[10px] text-gray-400 mt-0.5">Require immediate intervention</p>
                        </div>
                        <div className="bg-white p-4 rounded-2xl border border-gray-200 shadow-sm">
                            <span className="text-[11px] font-bold text-gray-400 uppercase">Safe Lifting Compliance</span>
                            <h2 className="text-2xl font-black text-blue-600 mt-1">
                                {risks.length > 0 ? Math.round((risks.filter(r => r.niosh.is_safe).length / risks.length) * 100) : 100}%
                            </h2>
                            <p className="text-[10px] text-gray-400 mt-0.5">NIOSH LI &le; 1.0</p>
                        </div>
                        <div className="bg-white p-4 rounded-2xl border border-gray-200 shadow-sm">
                            <span className="text-[11px] font-bold text-gray-400 uppercase">Most Stressed Joint</span>
                            <h2 className="text-2xl font-black text-amber-600 mt-1">Trunk Flexion</h2>
                            <p className="text-[10px] text-gray-400 mt-0.5">Torso bending &gt; 45°</p>
                        </div>
                    </div>

                    {/* Joint Heat Breakdown */}
                    <div className="bg-white p-5 rounded-2xl border border-gray-200 shadow-sm">
                        <h3 className="text-sm font-bold text-gray-900 mb-3">Ergonomic Joint Stress Distribution</h3>
                        <div className="space-y-3">
                            <div>
                                <div className="flex justify-between text-xs font-semibold mb-1">
                                    <span className="text-gray-700">Trunk / Lower Back Strain</span>
                                    <span className="text-rose-600">68% of incidents</span>
                                </div>
                                <div className="w-full bg-gray-100 rounded-full h-2 overflow-hidden">
                                    <div className="bg-rose-500 h-full rounded-full" style={{ width: '68%' }} />
                                </div>
                            </div>

                            <div>
                                <div className="flex justify-between text-xs font-semibold mb-1">
                                    <span className="text-gray-700">Upper Arm Elevation (&gt; 45°)</span>
                                    <span className="text-amber-600">42% of incidents</span>
                                </div>
                                <div className="w-full bg-gray-100 rounded-full h-2 overflow-hidden">
                                    <div className="bg-amber-500 h-full rounded-full" style={{ width: '42%' }} />
                                </div>
                            </div>

                            <div>
                                <div className="flex justify-between text-xs font-semibold mb-1">
                                    <span className="text-gray-700">Neck Flexion / Extension</span>
                                    <span className="text-blue-600">31% of incidents</span>
                                </div>
                                <div className="w-full bg-gray-100 rounded-full h-2 overflow-hidden">
                                    <div className="bg-blue-500 h-full rounded-full" style={{ width: '31%' }} />
                                </div>
                            </div>

                            <div>
                                <div className="flex justify-between text-xs font-semibold mb-1">
                                    <span className="text-gray-700">Leg & Asymmetric Squatting</span>
                                    <span className="text-emerald-600">18% of incidents</span>
                                </div>
                                <div className="w-full bg-gray-100 rounded-full h-2 overflow-hidden">
                                    <div className="bg-emerald-500 h-full rounded-full" style={{ width: '18%' }} />
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {/* ── MODAL: MEDIA & SKELETON INSPECTION ────────────────────────────── */}
            {selectedRiskModal && (
                <div className="fixed inset-0 z-50 bg-black/70 backdrop-blur-sm flex items-center justify-center p-4">
                    <div className="bg-white rounded-3xl max-w-3xl w-full overflow-hidden shadow-2xl border border-gray-100 flex flex-col max-h-[90vh]">
                        {/* Header */}
                        <div className="p-4 bg-gray-900 text-white flex items-center justify-between">
                            <div className="flex items-center gap-2.5">
                                <Activity className="w-5 h-5 text-blue-400" />
                                <div>
                                    <h3 className="text-sm font-bold">
                                        Postural Incident: {selectedRiskModal.cameraName}
                                    </h3>
                                    <p className="text-[11px] text-gray-400">
                                        {new Date(selectedRiskModal.timestamp).toLocaleString()} • Human Re-ID: {selectedRiskModal.employee ? `${selectedRiskModal.employee.firstName} ${selectedRiskModal.employee.lastName}` : `Track #${selectedRiskModal.trackId}`}
                                    </p>
                                </div>
                            </div>
                            <button
                                onClick={() => setSelectedRiskModal(null)}
                                className="p-1 rounded-full text-gray-400 hover:text-white transition"
                            >
                                <X className="w-5 h-5" />
                            </button>
                        </div>

                        {/* Media Display */}
                        <div className="p-4 bg-black flex items-center justify-center relative min-h-[300px] max-h-[420px] overflow-hidden">
                            {mediaType === 'video' && selectedRiskModal.videoClipUrl ? (
                                <video
                                    src={selectedRiskModal.videoClipUrl}
                                    controls
                                    autoPlay
                                    loop
                                    className="max-h-[380px] w-auto rounded-xl"
                                />
                            ) : selectedRiskModal.frameUrl ? (
                                <div className="relative w-full h-[360px]">
                                    <Image
                                        src={selectedRiskModal.frameUrl}
                                        alt="Ergonomic Risk Frame"
                                        fill
                                        className="object-contain"
                                    />
                                </div>
                            ) : (
                                <div className="text-gray-500 text-xs">No media snapshot recorded for this incident.</div>
                            )}

                            {selectedRiskModal.videoClipUrl && (
                                <div className="absolute bottom-3 right-3 flex gap-2">
                                    <button
                                        onClick={() => setMediaType(mediaType === 'video' ? 'snapshot' : 'video')}
                                        className="px-3 py-1 bg-white/20 hover:bg-white/30 backdrop-blur-md text-white text-xs font-bold rounded-lg transition"
                                    >
                                        Switch to {mediaType === 'video' ? 'Snapshot' : 'Video Clip'}
                                    </button>
                                </div>
                            )}
                        </div>

                        {/* Details Drawer */}
                        <div className="p-4 overflow-y-auto space-y-3 bg-gray-50">
                            <div className="p-3 bg-white rounded-2xl border border-gray-200">
                                <h4 className="text-xs font-bold text-gray-900 mb-1">Risk Factors & Findings</h4>
                                <p className="text-xs text-gray-700">{selectedRiskModal.riskReason}</p>
                            </div>

                            <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
                                <div className="bg-white p-2.5 rounded-xl border border-gray-200 text-center">
                                    <span className="text-[10px] text-gray-400 block font-bold">REBA Score</span>
                                    <span className="text-sm font-black text-rose-600">{selectedRiskModal.rebaScore} / 15</span>
                                </div>
                                <div className="bg-white p-2.5 rounded-xl border border-gray-200 text-center">
                                    <span className="text-[10px] text-gray-400 block font-bold">Trunk Angle</span>
                                    <span className="text-sm font-black text-gray-800">{selectedRiskModal.jointAngles.trunk}°</span>
                                </div>
                                <div className="bg-white p-2.5 rounded-xl border border-gray-200 text-center">
                                    <span className="text-[10px] text-gray-400 block font-bold">NIOSH Lifting Index</span>
                                    <span className="text-sm font-black text-amber-600">{selectedRiskModal.niosh.lifting_index.toFixed(2)}</span>
                                </div>
                                <div className="bg-white p-2.5 rounded-xl border border-gray-200 text-center">
                                    <span className="text-[10px] text-gray-400 block font-bold">Reach Tier</span>
                                    <span className="text-xs font-black text-gray-800 truncate block">{selectedRiskModal.mmh.zone}</span>
                                </div>
                            </div>
                        </div>

                        {/* Footer */}
                        <div className="p-3 bg-white border-t border-gray-200 flex justify-end gap-2">
                            <button
                                onClick={() => generatePdfReport(selectedRiskModal)}
                                className="flex items-center gap-1.5 px-4 py-2 bg-blue-600 text-white text-xs font-bold rounded-xl hover:bg-blue-700 transition shadow-sm"
                            >
                                <Download className="w-4 h-4" /> Download 3-Page Audit PDF
                            </button>
                        </div>
                    </div>
                </div>
            )}

            {/* ── MODAL: CAMERA CONFIGURATION ──────────────────────────────────── */}
            {configModalCamera && (
                <div className="fixed inset-0 z-50 bg-black/60 backdrop-blur-sm flex items-center justify-center p-4">
                    <div className="bg-white rounded-3xl max-w-md w-full p-6 shadow-2xl border border-gray-100">
                        <div className="flex items-center justify-between mb-4">
                            <div className="flex items-center gap-2.5">
                                <div className="p-2 bg-blue-50 text-blue-600 rounded-xl">
                                    <Camera className="w-5 h-5" />
                                </div>
                                <div>
                                    <h3 className="text-sm font-bold text-gray-900">{configModalCamera.cameraName}</h3>
                                    <p className="text-[11px] text-gray-400">Configure Ergonomic Monitoring Rule</p>
                                </div>
                            </div>
                            <button onClick={() => setConfigModalCamera(null)} className="text-gray-400 hover:text-gray-600">
                                <X className="w-5 h-5" />
                            </button>
                        </div>

                        <div className="space-y-4">
                            <div>
                                <label className="text-xs font-bold text-gray-700 block mb-1">
                                    REBA Alert Cutoff Threshold
                                </label>
                                <input
                                    type="number"
                                    min="4"
                                    max="15"
                                    value={rebaThreshold}
                                    onChange={e => setRebaThreshold(Number(e.target.value))}
                                    className="w-full px-3 py-2 bg-gray-50 border border-gray-200 rounded-xl text-xs font-semibold focus:outline-none focus:border-blue-500"
                                />
                                <p className="text-[10px] text-gray-400 mt-0.5">
                                    Trigger posture incidents when REBA score &ge; this value (Default: 7 or 8).
                                </p>
                            </div>

                            <div>
                                <label className="text-xs font-bold text-gray-700 block mb-1">
                                    Nominal Weight Handled (kg)
                                </label>
                                <input
                                    type="number"
                                    step="0.5"
                                    min="0"
                                    max="50"
                                    value={actualWeightKg}
                                    onChange={e => setActualWeightKg(Number(e.target.value))}
                                    className="w-full px-3 py-2 bg-gray-50 border border-gray-200 rounded-xl text-xs font-semibold focus:outline-none focus:border-blue-500"
                                />
                                <p className="text-[10px] text-gray-400 mt-0.5">
                                    Used for dynamic NIOSH Lifting Index calculation (Default: 8.0 kg).
                                </p>
                            </div>

                            <div>
                                <label className="text-xs font-bold text-gray-700 block mb-1">
                                    Evaluation Profile / MMH Matrix
                                </label>
                                <select
                                    value={profileGender}
                                    onChange={e => setProfileGender(e.target.value as 'Male' | 'Female')}
                                    className="w-full px-3 py-2 bg-gray-50 border border-gray-200 rounded-xl text-xs font-semibold focus:outline-none"
                                >
                                    <option value="Male">Standard Male Biomechanics</option>
                                    <option value="Female">Standard Female Biomechanics</option>
                                </select>
                            </div>

                            <div className="p-3 bg-blue-50/60 rounded-xl border border-blue-100 text-[11px] text-blue-800">
                                <span className="font-bold">Model:</span> YOLO26 Medium Pose (<code className="font-mono">models/yolo26m-pose.pt</code>) loaded from S3.
                            </div>
                        </div>

                        <div className="flex gap-2 mt-6">
                            <button
                                onClick={() => handleSaveCameraConfig(false)}
                                disabled={savingConfig}
                                className="flex-1 py-2.5 bg-gray-100 hover:bg-gray-200 text-gray-700 text-xs font-bold rounded-xl transition"
                            >
                                Disable on Camera
                            </button>
                            <button
                                onClick={() => handleSaveCameraConfig(true)}
                                disabled={savingConfig}
                                className="flex-1 py-2.5 bg-blue-600 hover:bg-blue-700 text-white text-xs font-bold rounded-xl transition shadow-md shadow-blue-500/20"
                            >
                                {savingConfig ? 'Saving...' : 'Enable Ergonomics'}
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}
