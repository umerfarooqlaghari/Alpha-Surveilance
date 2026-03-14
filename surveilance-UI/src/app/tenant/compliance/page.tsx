'use client';

import { useState, useEffect, useCallback } from 'react';
import {
    Shield, Search, ChevronRight, CheckCircle2,
    AlertTriangle, Save, Send, X, Loader2,
    Users, Wrench, Eye, TrendingDown, ClipboardCheck,
    Download, List, ClipboardList
} from 'lucide-react';
import { getViolations } from '@/lib/api/tenant/violations';
import type { Violation } from '@/lib/api/tenant/violations';
import {
    getAuditByViolation, getAudits, createAudit, updateAudit,
} from '@/lib/api/tenant/violationAudits';
import type { ViolationAuditResponse, ViolationAuditRequest, AuditRecordStatus } from '@/lib/api/tenant/violationAudits';
import { useAuth } from '@/contexts/AuthContext';

// ── Constants ──────────────────────────────────────────────────────────────

const SEVERITY_CLASS: Record<string, string> = {
    Critical: 'bg-red-100 text-red-700 border-red-200',
    High: 'bg-orange-100 text-orange-700 border-orange-200',
    Medium: 'bg-amber-100 text-amber-700 border-amber-200',
    Low: 'bg-blue-100 text-blue-700 border-blue-200',
};

const STATUS_CONFIG = {
    0: { label: 'Draft', cls: 'bg-gray-100 text-gray-600 border-gray-200' },
    1: { label: 'Submitted', cls: 'bg-blue-100 text-blue-700 border-blue-200' },
    2: { label: 'Reviewed', cls: 'bg-emerald-100 text-emerald-700 border-emerald-200' },
} as const;

const getStatusCfg = (status: number | string) => {
    if (typeof status === 'string') {
        if (status.toLowerCase() === 'draft' || status === '0') return STATUS_CONFIG[0];
        if (status.toLowerCase() === 'submitted' || status === '1') return STATUS_CONFIG[1];
        if (status.toLowerCase() === 'reviewed' || status === '2') return STATUS_CONFIG[2];
    }
    return STATUS_CONFIG[status as 0 | 1 | 2] ?? STATUS_CONFIG[0];
};

// ── Shared sub-components ──────────────────────────────────────────────────

function SectionHeader({ icon: Icon, title, subtitle }: { icon: any; title: string; subtitle: string }) {
    return (
        <div className="flex items-start gap-3 pb-4 border-b border-gray-100 mb-5">
            <div className="p-2 bg-slate-100 rounded-lg"><Icon className="w-4 h-4 text-slate-700" /></div>
            <div>
                <h3 className="text-sm font-semibold text-gray-900">{title}</h3>
                <p className="text-xs text-gray-500 mt-0.5">{subtitle}</p>
            </div>
        </div>
    );
}

function Label({ children }: { children: React.ReactNode }) {
    return <label className="block text-xs font-medium text-gray-700 mb-1.5">{children}</label>;
}

function TA({ value, onChange, placeholder, rows = 3 }: { value: string; onChange: (v: string) => void; placeholder?: string; rows?: number }) {
    return (
        <textarea value={value} onChange={e => onChange(e.target.value)} placeholder={placeholder} rows={rows}
            className="w-full px-3 py-2 text-sm border border-gray-200 rounded-lg focus:ring-2 focus:ring-slate-500 focus:border-transparent resize-none text-gray-800 placeholder-gray-400 bg-white" />
    );
}

function TI({ value, onChange, placeholder, type = 'text' }: { value: string; onChange: (v: string) => void; placeholder?: string; type?: string }) {
    return (
        <input type={type} value={value} onChange={e => onChange(e.target.value)} placeholder={placeholder}
            className="w-full px-3 py-2 text-sm border border-gray-200 rounded-lg focus:ring-2 focus:ring-slate-500 focus:border-transparent text-gray-800 placeholder-gray-400 bg-white" />
    );
}

// ── Empty / populate form ──────────────────────────────────────────────────

function emptyForm(violationId: string): ViolationAuditRequest {
    return {
        violationId, status: 0, executiveSummary: '', rootCauseAnalysis: '', contributingFactors: '',
        stakeholdersAffected: '', estimatedImpact: '', measuresTaken: '', resolvedBy: '', resolvedAt: '',
        preventionMeasures: '', followUpActions: '', reviewedBy: '', reviewedAt: '', internalNotes: ''
    };
}

function auditToForm(a: ViolationAuditResponse): ViolationAuditRequest {
    return {
        violationId: a.violationId, status: a.status,
        executiveSummary: a.executiveSummary ?? '',
        rootCauseAnalysis: a.rootCauseAnalysis ?? '',
        contributingFactors: a.contributingFactors ?? '',
        stakeholdersAffected: a.stakeholdersAffected ?? '',
        estimatedImpact: a.estimatedImpact ?? '',
        measuresTaken: a.measuresTaken ?? '',
        resolvedBy: a.resolvedBy ?? '',
        resolvedAt: a.resolvedAt ? a.resolvedAt.split('T')[0] : '',
        preventionMeasures: a.preventionMeasures ?? '',
        followUpActions: a.followUpActions ?? '',
        reviewedBy: a.reviewedBy ?? '',
        reviewedAt: a.reviewedAt ? a.reviewedAt.split('T')[0] : '',
        internalNotes: a.internalNotes ?? '',
    };
}

// ── PDF generation (navy slate theme, logo in cover + footer) ─────────────

function generatePdf(audit: ViolationAuditResponse, violation: Violation | undefined, tenantName: string, logoUrl?: string) {
    const ts = (s?: string) => s ? new Date(s).toLocaleString() : '—';
    const val = (s?: string | null) => s?.trim() || '<em style="color:#a8a29e">Not specified</em>';
    const sev = violation?.severity?.toString() || 'Unknown';
    const cfg = getStatusCfg(audit.status);
    const logo = logoUrl || `${window.location.origin}/alpha-logo.jpg`;
    const now = new Date().toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });

    const html = `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8"/>
<title>Compliance Audit Report — ${audit.id.substring(0, 8).toUpperCase()}</title>
<style>
  *{box-sizing:border-box;margin:0;padding:0}
  body{font-family:'Segoe UI',Arial,sans-serif;color:#1e293b;background:#fff;font-size:13px;line-height:1.65}
  .cover{background:linear-gradient(135deg,#0f172a 0%,#1e293b 55%,#334155 100%);color:#fff;padding:48px 56px 44px;page-break-after:always;position:relative}
  .cover-logo{position:absolute;top:32px;right:48px;height:52px;object-fit:contain;opacity:.92;border-radius:6px;background:#fff;padding:4px}
  .cover-brand{font-size:11px;opacity:.75;text-transform:uppercase;letter-spacing:2.5px;margin-bottom:30px;color:#cbd5e1}
  .cover h1{font-size:30px;font-weight:700;letter-spacing:-.5px;margin-bottom:6px}
  .cover .subtitle{font-size:14px;opacity:.8;margin-bottom:28px;color:#94a3b8}
  .cover hr{border:none;border-top:1px solid rgba(255,255,255,.15);margin-bottom:26px}
  .meta{display:flex;gap:40px;flex-wrap:wrap}
  .meta-item label{font-size:10px;opacity:.65;text-transform:uppercase;letter-spacing:1px;display:block}
  .meta-item p{font-size:14px;font-weight:700;margin-top:2px}
  .badge{display:inline-block;padding:3px 10px;border-radius:9999px;font-size:11px;font-weight:600;border:1px solid}
  .b-sub{background:#e0f2fe;color:#0284c7;border-color:#bae6fd}
  .b-rev{background:#dcfce7;color:#16a34a;border-color:#bbf7d0}
  .b-dra{background:#f1f5f9;color:#475569;border-color:#e2e8f0}
  .b-crit{background:#fee2e2;color:#dc2626;border-color:#fecaca}
  .b-high{background:#ffedd5;color:#ea580c;border-color:#fed7aa}
  .b-med{background:#fef3c7;color:#d97706;border-color:#fde68a}
  .b-low{background:#f0fdf4;color:#16a34a;border-color:#bbf7d0}
  .page{padding:40px 56px}
  h2{font-size:17px;font-weight:700;color:#0f172a;margin-bottom:4px}
  .section{border:1px solid #e2e8f0;border-radius:10px;margin-bottom:22px;overflow:hidden}
  .sh{background:#f8fafc;border-bottom:1px solid #e2e8f0;padding:11px 18px;display:flex;align-items:center;gap:10px}
  .sn{width:26px;height:26px;background:#334155;border-radius:7px;display:flex;align-items:center;justify-content:center;color:#fff;font-size:13px;font-weight:700;flex-shrink:0}
  .st{font-size:13px;font-weight:700;color:#0f172a}
  .sd{font-size:11px;color:#64748b}
  .sb{padding:16px 18px}
  .row{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:10px}
  .f label{font-size:10px;color:#64748b;text-transform:uppercase;letter-spacing:.8px;font-weight:600;display:block;margin-bottom:3px}
  .f p{font-size:13px;color:#0f172a}
  .f.full{grid-column:1/-1}
  .div{height:1px;background:#f1f5f9;margin:10px 0}
  table.vm{width:100%;border-collapse:collapse;margin-bottom:8px}
  table.vm td{padding:8px 12px;font-size:12px;border:1px solid #e2e8f0}
  table.vm td:first-child{background:#f8fafc;font-weight:600;width:160px;color:#475569}
  .footer{margin-top:32px;padding-top:14px;border-top:2px solid #cbd5e1;display:flex;justify-content:space-between;align-items:center;font-size:11px;color:#94a3b8;gap:12px}
  .fb{display:flex;align-items:center;gap:8px}
  .fl{height:20px;object-fit:contain;opacity:.85;border-radius:2px}
  @media print{body,*.cover{-webkit-print-color-adjust:exact;print-color-adjust:exact}}
</style>
</head>
<body>

<div class="cover">
  <img src="${logo}" class="cover-logo" alt="${tenantName}" onerror="this.style.display='none'"/>
  <div class="cover-brand">${tenantName} · Compliance Division</div>
  <h1>Violation Audit Report</h1>
  <div class="subtitle">Formal compliance record for regulatory and audit purposes</div>
  <hr/>
  <div class="meta">
    <div class="meta-item"><label>Report ID</label><p>#${audit.id.substring(0, 12).toUpperCase()}</p></div>
    <div class="meta-item"><label>Audit Status</label><p>${cfg.label.toUpperCase()}</p></div>
    <div class="meta-item"><label>Generated</label><p>${now}</p></div>
  </div>
</div>

<div class="page">
  <div style="margin-bottom:28px">
    <h2>Violation Details</h2>
    <p style="font-size:12px;color:#78716c;margin-bottom:14px">Original incident that triggered this compliance audit</p>
    <table class="vm">
      <tr><td>Violation Type</td><td>${violation?.violationTypeName || violation?.type || '—'}</td></tr>
      <tr><td>Severity</td><td><span class="badge b-${sev.toLowerCase() === 'critical' ? 'crit' : sev.toLowerCase() === 'high' ? 'high' : sev.toLowerCase() === 'medium' ? 'med' : 'low'}">${sev}</span></td></tr>
      <tr><td>Camera</td><td>${violation?.cameraName || violation?.cameraId || '—'}</td></tr>
      <tr><td>Detected At</td><td>${violation ? new Date(violation.timestamp).toLocaleString() : '—'}</td></tr>
      <tr><td>Violation ID</td><td style="font-family:monospace;font-size:11px">${audit.violationId}</td></tr>
      <tr><td>Audit Status</td><td><span class="badge ${audit.status === 2 ? 'b-rev' : audit.status === 1 ? 'b-sub' : 'b-dra'}">${cfg.label}</span></td></tr>
    </table>
  </div>

  <div class="section">
    <div class="sh"><div class="sn">1</div><div><div class="st">Incident Summary</div><div class="sd">Executive overview for stakeholders and management</div></div></div>
    <div class="sb"><div class="f full"><label>Executive Summary</label><p>${val(audit.executiveSummary)}</p></div></div>
  </div>

  <div class="section">
    <div class="sh"><div class="sn">2</div><div><div class="st">Root Cause Analysis</div><div class="sd">Underlying cause and contributing factors</div></div></div>
    <div class="sb">
      <div class="f full"><label>Root Cause</label><p>${val(audit.rootCauseAnalysis)}</p></div>
      <div class="div"></div>
      <div class="f full"><label>Contributing Factors</label><p>${val(audit.contributingFactors)}</p></div>
    </div>
  </div>

  <div class="section">
    <div class="sh"><div class="sn">3</div><div><div class="st">Impact Assessment</div><div class="sd">Scope of impact on people, operations and business</div></div></div>
    <div class="sb">
      <div class="row">
        <div class="f"><label>Stakeholders / Persons Affected</label><p>${val(audit.stakeholdersAffected)}</p></div>
        <div class="f"><label>Estimated Impact</label><p>${val(audit.estimatedImpact)}</p></div>
      </div>
    </div>
  </div>

  <div class="section">
    <div class="sh"><div class="sn">4</div><div><div class="st">Response &amp; Resolution</div><div class="sd">Corrective actions taken to resolve the violation</div></div></div>
    <div class="sb">
      <div class="f full"><label>Measures Taken</label><p>${val(audit.measuresTaken)}</p></div>
      <div class="div"></div>
      <div class="row">
        <div class="f"><label>Resolved By</label><p>${val(audit.resolvedBy)}</p></div>
        <div class="f"><label>Resolution Date</label><p>${audit.resolvedAt ? new Date(audit.resolvedAt).toLocaleDateString('en-GB', { day: '2-digit', month: 'long', year: 'numeric' }) : '—'}</p></div>
      </div>
    </div>
  </div>

  <div class="section">
    <div class="sh"><div class="sn">5</div><div><div class="st">Prevention Measures</div><div class="sd">Steps to prevent recurrence and reduce future risk</div></div></div>
    <div class="sb">
      <div class="f full"><label>Prevention Strategy</label><p>${val(audit.preventionMeasures)}</p></div>
      <div class="div"></div>
      <div class="f full"><label>Follow-Up Actions</label><p>${val(audit.followUpActions)}</p></div>
    </div>
  </div>

  <div class="section">
    <div class="sh"><div class="sn">6</div><div><div class="st">Sign-off &amp; Review</div><div class="sd">Management approval and internal audit notes</div></div></div>
    <div class="sb">
      <div class="row">
        <div class="f"><label>Reviewed By</label><p>${val(audit.reviewedBy)}</p></div>
        <div class="f"><label>Review Date</label><p>${audit.reviewedAt ? new Date(audit.reviewedAt).toLocaleDateString('en-GB', { day: '2-digit', month: 'long', year: 'numeric' }) : '—'}</p></div>
      </div>
      <div class="div"></div>
      <div class="f full"><label>Internal Notes (Confidential)</label><p>${val(audit.internalNotes)}</p></div>
    </div>
  </div>

  <div class="section">
    <div class="sh"><div class="sn" style="background:#0f172a">✓</div><div><div class="st">Audit Trail</div><div class="sd">Timestamped record of creation and last update</div></div></div>
    <div class="sb">
      <div class="row">
        <div class="f"><label>Audit Created</label><p>${ts(audit.createdAt)}</p></div>
        <div class="f"><label>Last Updated</label><p>${ts(audit.updatedAt)}</p></div>
        <div class="f"><label>Created By (User ID)</label><p>${audit.createdByUserId || '—'}</p></div>
        <div class="f"><label>Audit Record ID</label><p style="font-family:monospace;font-size:11px">${audit.id}</p></div>
      </div>
    </div>
  </div>

  <div class="footer">
    <div class="fb">
      <img src="${logo}" class="fl" alt="" onerror="this.style.display='none'"/>
      <span style="font-weight:600;color:#0f172a">${tenantName}</span>
      <span>· Compliance Report · Generated ${new Date().toLocaleString()}</span>
    </div>
    <span>CONFIDENTIAL — Authorised personnel only</span>
  </div>
</div>
</body>
</html>`;

    const win = window.open('', '_blank', 'width=920,height=720');
    if (!win) return;
    win.document.open();
    win.document.write(html);
    win.document.close();
    win.onload = () => { win.focus(); win.print(); };
}

// ══════════════════════════════════════════════════════════════════════════
// ── AUDIT RECORDS TAB ─────────────────────────────────────────────────────
// ══════════════════════════════════════════════════════════════════════════

function AuditRecordsTab({ violations, refreshKey, tenant }: { violations: Violation[]; refreshKey: number; tenant: any }) {
    const [audits, setAudits] = useState<ViolationAuditResponse[]>([]);
    const [loading, setLoading] = useState(true);
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState<'all' | '1' | '2'>('all');

    useEffect(() => {
        setLoading(true);
        getAudits()
            .then(setAudits)
            .catch(() => { })
            .finally(() => setLoading(false));
    }, [refreshKey]); // re-fetch when parent signals a save

    const vMap = new Map(violations.map(v => [v.id, v]));

    const filtered = audits
        .filter(a => {
            const v = vMap.get(a.violationId);
            const matchSearch = !search ||
                v?.violationTypeName?.toLowerCase().includes(search.toLowerCase()) ||
                v?.cameraName?.toLowerCase().includes(search.toLowerCase()) ||
                a.resolvedBy?.toLowerCase().includes(search.toLowerCase()) ||
                a.executiveSummary?.toLowerCase().includes(search.toLowerCase());
            const matchStatus = statusFilter === 'all' || String(a.status) === statusFilter;
            return matchSearch && matchStatus;
        })
        .sort((a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime());

    return (
        <div className="flex-1 min-h-0 overflow-y-auto p-8">
            <div className="flex gap-3 mb-6">
                <div className="relative flex-1 max-w-sm">
                    <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
                    <input type="text" placeholder="Search by type, camera, resolver…" value={search}
                        onChange={e => setSearch(e.target.value)}
                        className="w-full pl-9 pr-3 py-2 text-sm border border-gray-200 rounded-lg focus:ring-2 focus:ring-amber-500 text-gray-700" />
                </div>
                {(['all', '1', '2'] as const).map(s => (
                    <button key={s} onClick={() => setStatusFilter(s)}
                        className={`px-4 py-2 text-xs font-medium rounded-lg border transition-colors ${statusFilter === s ? 'bg-slate-700 text-white border-slate-700' : 'bg-white text-gray-600 border-gray-200 hover:border-slate-400'}`}>
                        {s === 'all' ? 'All' : s === '1' ? 'Submitted' : 'Reviewed'}
                    </button>
                ))}
            </div>

            {loading ? (
                <div className="flex justify-center pt-16"><Loader2 className="w-6 h-6 animate-spin text-slate-500" /></div>
            ) : filtered.length === 0 ? (
                <div className="text-center py-16 text-gray-400">
                    <ClipboardList className="w-10 h-10 mx-auto mb-3 opacity-30" />
                    <p className="text-sm">No audit records found.</p>
                </div>
            ) : (
                <div className="space-y-3">
                    {filtered.map(audit => {
                        const v = vMap.get(audit.violationId);
                        const sev = v?.severity?.toString() || 'Low';
                        const cfg = getStatusCfg(audit.status);
                        return (
                            <div key={audit.id} className="bg-white rounded-xl border border-gray-200 px-6 py-5 flex items-start gap-5 hover:border-slate-300 hover:shadow-sm transition-all">
                                <div className={`mt-1 w-2.5 h-2.5 rounded-full flex-shrink-0 ${audit.status === 2 ? 'bg-emerald-500' : audit.status === 1 ? 'bg-blue-500' : 'bg-gray-400'}`} />
                                <div className="flex-1 min-w-0">
                                    <div className="flex items-center gap-2 flex-wrap mb-1.5">
                                        <span className="text-sm font-semibold text-gray-900">{v?.violationTypeName || v?.type || 'Unknown Violation'}</span>
                                        <span className={`px-2 py-0.5 rounded-full text-[10px] font-semibold border ${SEVERITY_CLASS[sev] ?? 'bg-gray-100 text-gray-600 border-gray-200'}`}>{sev}</span>
                                        <span className={`px-2 py-0.5 rounded-full text-[10px] font-semibold border ${cfg.cls}`}>{cfg.label}</span>
                                    </div>
                                    <p className="text-xs text-gray-500 mb-2">
                                        {v?.cameraName || '—'} · Detected {v ? new Date(v.timestamp).toLocaleString() : '—'}
                                    </p>
                                    {audit.executiveSummary && (
                                        <p className="text-xs text-gray-600 line-clamp-2 mb-2 italic">"{audit.executiveSummary}"</p>
                                    )}
                                    <div className="flex flex-wrap gap-4 text-xs text-gray-500">
                                        {audit.resolvedBy && <span>✓ Resolved by <span className="font-medium text-gray-700">{audit.resolvedBy}</span></span>}
                                        {audit.resolvedAt && <span>on {new Date(audit.resolvedAt).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })}</span>}
                                        {audit.reviewedBy && <span>· Reviewed by <span className="font-medium text-gray-700">{audit.reviewedBy}</span></span>}
                                        <span className="text-gray-400">Updated {new Date(audit.updatedAt).toLocaleString()}</span>
                                    </div>
                                </div>
                                <button onClick={() => generatePdf(audit, v, tenant?.tenantName || 'Alpha Security', tenant?.logoUrl)} title="Download PDF Report"
                                    className="flex items-center gap-2 px-3 py-2 text-xs font-medium text-slate-700 bg-slate-50 border border-slate-200 rounded-lg hover:bg-slate-100 transition-colors flex-shrink-0">
                                    <Download className="w-3.5 h-3.5" /> PDF
                                </button>
                            </div>
                        );
                    })}
                </div>
            )}
        </div>
    );
}

// ══════════════════════════════════════════════════════════════════════════
// ── MAIN COMPLIANCE PAGE ──────────────────────────────────────────────────
// ══════════════════════════════════════════════════════════════════════════

export default function CompliancePage() {
    const { tenant } = useAuth();
    const [activeTab, setActiveTab] = useState<'form' | 'records'>('form');
    // Incremented after each successful save → forces AuditRecordsTab to re-fetch
    const [recordsRefreshKey, setRecordsRefreshKey] = useState(0);

    const [violations, setViolations] = useState<Violation[]>([]);
    const [violationsLoading, setViolationsLoading] = useState(true);

    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState<'all' | 'pending' | 'audited'>('all');
    const [selected, setSelected] = useState<Violation | null>(null);
    const [existingAudit, setExistingAudit] = useState<ViolationAuditResponse | null>(null);
    const [form, setForm] = useState<ViolationAuditRequest | null>(null);
    const [saving, setSaving] = useState(false);
    const [drawerLoading, setDrawerLoading] = useState(false);
    const [toast, setToast] = useState<{ msg: string; type: 'success' | 'error' } | null>(null);

    useEffect(() => {
        getViolations()
            .then(d => setViolations(d ?? []))
            .catch(() => { })
            .finally(() => setViolationsLoading(false));
    }, []);

    const openDrawer = useCallback(async (v: Violation) => {
        setSelected(v);
        setExistingAudit(null);
        setForm(null);
        setDrawerLoading(true);
        try {
            const audit = await getAuditByViolation(v.id);
            if (audit) { setExistingAudit(audit); setForm(auditToForm(audit)); }
            else { setForm(emptyForm(v.id)); }
        } catch { setForm(emptyForm(v.id)); }
        finally { setDrawerLoading(false); }
    }, []);

    const closeDrawer = () => { setSelected(null); setForm(null); setExistingAudit(null); };
    const setField = (key: keyof ViolationAuditRequest, value: string | number) =>
        setForm(f => f ? { ...f, [key]: value } : f);

    const showToast = (msg: string, type: 'success' | 'error') => {
        setToast({ msg, type });
        setTimeout(() => setToast(null), 3500);
    };

    const save = async (statusOverride?: AuditRecordStatus) => {
        if (!form) return;
        setSaving(true);
        try {
            const payload: ViolationAuditRequest = {
                ...form, status: statusOverride ?? form.status,
                resolvedAt: form.resolvedAt || undefined,
                reviewedAt: form.reviewedAt || undefined,
            };
            const saved = existingAudit
                ? await updateAudit(existingAudit.id, payload)
                : await createAudit(payload);
            setExistingAudit(saved);
            setForm(auditToForm(saved));
            const data = await getViolations();
            setViolations(data ?? []);
            // Tell AuditRecordsTab to re-fetch with latest statuses
            setRecordsRefreshKey(k => k + 1);
            showToast(
                statusOverride === 1 ? 'Audit submitted — violation marked as Audited.' :
                    statusOverride === 2 ? 'Audit reviewed and signed off.' : 'Draft saved.',
                'success'
            );
        } catch { showToast('Failed to save. Please try again.', 'error'); }
        finally { setSaving(false); }
    };

    const filtered = violations.filter(v => {
        const matchSearch = !search ||
            v.type?.toLowerCase().includes(search.toLowerCase()) ||
            v.violationTypeName?.toLowerCase().includes(search.toLowerCase()) ||
            v.cameraName?.toLowerCase().includes(search.toLowerCase());
        const matchStatus =
            statusFilter === 'all' ||
            (statusFilter === 'pending' && v.status !== 'Audited') ||
            (statusFilter === 'audited' && v.status === 'Audited');
        return matchSearch && matchStatus;
    });

    const pendingCount = violations.filter(v => v.status !== 'Audited').length;
    const auditedCount = violations.filter(v => v.status === 'Audited').length;

    return (
        <div className="flex flex-col h-full -m-8">

            {/* ── Top bar with tabs ─────────────────────────────────────── */}
            <div className="bg-white border-b border-gray-200 px-8 pt-6 pb-0 flex-shrink-0">
                <div className="flex items-center gap-3 mb-4">
                    <Shield className="w-5 h-5 text-slate-700" />
                    <h1 className="text-lg font-bold text-gray-900">Compliance</h1>
                </div>
                <div className="flex gap-1">
                    {[
                        { key: 'form', label: 'Audit Violations', icon: ClipboardCheck },
                        { key: 'records', label: 'Audit Records', icon: List },
                    ].map(({ key, label, icon: Icon }) => (
                        <button key={key} onClick={() => setActiveTab(key as typeof activeTab)}
                            className={`flex items-center gap-2 px-5 py-2.5 text-sm font-medium border-b-2 transition-colors ${activeTab === key
                                ? 'border-slate-700 text-slate-800'
                                : 'border-transparent text-gray-500 hover:text-gray-700'}`}>
                            <Icon className="w-4 h-4" />{label}
                        </button>
                    ))}
                </div>
            </div>

            {/* ── TAB: Audit Violations ────────────────────────────────── */}
            {activeTab === 'form' && (
                <div className="flex flex-1 min-h-0">
                    {/* Left panel */}
                    <div className="w-[400px] flex-shrink-0 flex flex-col bg-white border-r border-gray-200 overflow-hidden">
                        <div className="px-5 pt-4 pb-3 border-b border-gray-100">
                            <div className="flex gap-3">
                                <div className="flex-1 bg-slate-50 rounded-lg px-3 py-2 border border-slate-200">
                                    <div className="text-lg font-bold text-slate-700">{pendingCount}</div>
                                    <div className="text-xs text-slate-600">Pending</div>
                                </div>
                                <div className="flex-1 bg-emerald-50 rounded-lg px-3 py-2 border border-emerald-100">
                                    <div className="text-lg font-bold text-emerald-700">{auditedCount}</div>
                                    <div className="text-xs text-emerald-600">Resolved</div>
                                </div>
                                <div className="flex-1 bg-stone-50 rounded-lg px-3 py-2 border border-stone-200">
                                    <div className="text-lg font-bold text-stone-700">{violations.length}</div>
                                    <div className="text-xs text-stone-600">Total</div>
                                </div>
                            </div>
                        </div>

                        <div className="px-4 pt-3 pb-2 space-y-2">
                            <div className="relative">
                                <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400" />
                                <input type="text" placeholder="Search violations…" value={search}
                                    onChange={e => setSearch(e.target.value)}
                                    className="w-full pl-9 pr-3 py-2 text-sm border border-gray-200 rounded-lg focus:ring-2 focus:ring-slate-500 text-gray-700" />
                            </div>
                            <div className="flex gap-1.5">
                                {(['all', 'pending', 'audited'] as const).map(s => (
                                    <button key={s} onClick={() => setStatusFilter(s)}
                                        className={`flex-1 py-1.5 text-xs font-medium rounded-md border capitalize transition-colors ${statusFilter === s ? 'bg-slate-700 text-white border-slate-700' : 'bg-white text-gray-600 border-gray-200 hover:border-slate-400'}`}>
                                        {s}
                                    </button>
                                ))}
                            </div>
                        </div>

                        <div className="flex-1 overflow-y-auto px-4 pb-4 space-y-2">
                            {violationsLoading ? (
                                <div className="flex justify-center pt-10"><Loader2 className="w-5 h-5 animate-spin text-slate-500" /></div>
                            ) : filtered.length === 0 ? (
                                <div className="text-center py-10 text-sm text-gray-400">No violations found.</div>
                            ) : filtered.map(v => {
                                const isAudited = v.status === 'Audited';
                                const isActive = selected?.id === v.id;
                                const sev = v.severity?.toString() || 'Low';
                                return (
                                    <button key={v.id} onClick={() => openDrawer(v)}
                                        className={`w-full text-left rounded-xl border p-3.5 transition-all ${isActive ? 'border-slate-400 bg-slate-50 shadow-sm' : 'border-gray-200 bg-white hover:border-slate-200 hover:bg-gray-50'}`}>
                                        <div className="flex items-start justify-between gap-2">
                                            <div className="flex-1 min-w-0">
                                                <div className="flex items-center gap-2 mb-1.5">
                                                    <span className={`px-2 py-0.5 rounded-full text-[10px] font-semibold border ${SEVERITY_CLASS[sev] ?? 'bg-gray-100 text-gray-600 border-gray-200'}`}>{sev}</span>
                                                    <span className={`px-2 py-0.5 rounded-full text-[10px] font-medium border ${isAudited ? 'bg-emerald-50 text-emerald-700 border-emerald-200' : 'bg-slate-50 text-slate-700 border-slate-200'}`}>
                                                        {isAudited ? '✓ Audited' : '○ Pending'}
                                                    </span>
                                                </div>
                                                <div className="text-sm font-semibold text-gray-800 truncate">{v.violationTypeName || v.type || 'Unknown'}</div>
                                                <div className="text-xs text-gray-500 mt-0.5 truncate">{v.cameraName || v.cameraId || '—'} · {new Date(v.timestamp).toLocaleString()}</div>
                                            </div>
                                            <ChevronRight className={`w-4 h-4 flex-shrink-0 mt-1 ${isActive ? 'text-slate-500' : 'text-gray-300'}`} />
                                        </div>
                                    </button>
                                );
                            })}
                        </div>
                    </div>

                    {/* Right panel */}
                    {!selected ? (
                        <div className="flex-1 flex flex-col items-center justify-center gap-3 bg-gray-50 text-center px-8">
                            <div className="p-5 bg-white rounded-2xl shadow-sm border border-gray-200"><Shield className="w-10 h-10 text-slate-300 mx-auto" /></div>
                            <h2 className="text-lg font-semibold text-gray-700">Select a violation</h2>
                            <p className="text-sm text-gray-400 max-w-xs">Choose a violation from the list to begin or continue its compliance audit trail.</p>
                        </div>
                    ) : (
                        <div className="flex-1 flex flex-col overflow-hidden bg-gray-50">
                            <div className="bg-white border-b border-gray-200 px-8 py-5 flex items-start justify-between flex-shrink-0">
                                <div>
                                    <div className="flex items-center gap-2 mb-1">
                                        <h2 className="text-base font-bold text-gray-900">{selected.violationTypeName || selected.type}</h2>
                                        {existingAudit && (() => { const c = getStatusCfg(existingAudit.status); return <span className={`px-2 py-0.5 rounded-full text-[10px] font-semibold border ${c.cls}`}>{c.label}</span>; })()}
                                    </div>
                                    <p className="text-xs text-gray-500">
                                        {selected.cameraName || '—'} · {new Date(selected.timestamp).toLocaleString()} · Severity: <span className="font-medium">{selected.severity?.toString() || 'Unknown'}</span>
                                    </p>
                                </div>
                                <div className="flex items-center gap-2">
                                    {existingAudit && (
                                        <button onClick={() => generatePdf(existingAudit, selected, tenant?.tenantName || 'Alpha Security', tenant?.logoUrl)}
                                            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-slate-700 bg-slate-50 border border-slate-200 rounded-lg hover:bg-slate-100">
                                            <Download className="w-3.5 h-3.5" /> PDF
                                        </button>
                                    )}
                                    <button onClick={closeDrawer} className="p-2 hover:bg-gray-100 rounded-lg text-gray-400"><X className="w-4 h-4" /></button>
                                </div>
                            </div>

                            <div className="flex-1 overflow-y-auto px-8 py-6 space-y-8">
                                {drawerLoading ? (
                                    <div className="flex justify-center pt-16"><Loader2 className="w-6 h-6 animate-spin text-slate-500" /></div>
                                ) : form && (
                                    <>
                                        <div className="bg-white rounded-xl border border-gray-200 p-6">
                                            <SectionHeader icon={ClipboardCheck} title="Incident Summary" subtitle="Concise overview for executives and auditors" />
                                            <Label>Executive Summary</Label>
                                            <TA value={form.executiveSummary ?? ''} onChange={v => setField('executiveSummary', v)} placeholder="Provide a clear, concise summary of what happened, when, and where..." rows={4} />
                                        </div>
                                        <div className="bg-white rounded-xl border border-gray-200 p-6">
                                            <SectionHeader icon={Eye} title="Root Cause Analysis" subtitle="Identify the underlying factors that led to this violation" />
                                            <div className="space-y-4">
                                                <div><Label>Root Cause</Label><TA value={form.rootCauseAnalysis ?? ''} onChange={v => setField('rootCauseAnalysis', v)} placeholder="Describe the primary root cause..." rows={4} /></div>
                                                <div><Label>Contributing Factors</Label><TA value={form.contributingFactors ?? ''} onChange={v => setField('contributingFactors', v)} placeholder="List secondary contributing factors..." rows={3} /></div>
                                            </div>
                                        </div>
                                        <div className="bg-white rounded-xl border border-gray-200 p-6">
                                            <SectionHeader icon={Users} title="Impact Assessment" subtitle="Identify who was affected and the extent of the impact" />
                                            <div className="space-y-4">
                                                <div><Label>Stakeholders / Persons Affected</Label><TA value={form.stakeholdersAffected ?? ''} onChange={v => setField('stakeholdersAffected', v)} placeholder="Names, roles, or departments affected..." rows={3} /></div>
                                                <div><Label>Estimated Impact</Label><TA value={form.estimatedImpact ?? ''} onChange={v => setField('estimatedImpact', v)} placeholder="Financial, operational, or safety impact..." rows={2} /></div>
                                            </div>
                                        </div>
                                        <div className="bg-white rounded-xl border border-gray-200 p-6">
                                            <SectionHeader icon={Wrench} title="Response & Resolution" subtitle="Document the actions taken to address the violation" />
                                            <div className="space-y-4">
                                                <div><Label>Measures Taken</Label><TA value={form.measuresTaken ?? ''} onChange={v => setField('measuresTaken', v)} placeholder="Describe corrective actions..." rows={4} /></div>
                                                <div className="grid grid-cols-2 gap-4">
                                                    <div><Label>Resolved By</Label><TI value={form.resolvedBy ?? ''} onChange={v => setField('resolvedBy', v)} placeholder="Name or employee ID" /></div>
                                                    <div><Label>Resolution Date</Label><TI type="date" value={form.resolvedAt ?? ''} onChange={v => setField('resolvedAt', v)} /></div>
                                                </div>
                                            </div>
                                        </div>
                                        <div className="bg-white rounded-xl border border-gray-200 p-6">
                                            <SectionHeader icon={TrendingDown} title="Prevention Measures" subtitle="How will this be prevented in the future?" />
                                            <div className="space-y-4">
                                                <div><Label>Prevention Strategy</Label><TA value={form.preventionMeasures ?? ''} onChange={v => setField('preventionMeasures', v)} placeholder="Long-term controls, process changes, or training..." rows={4} /></div>
                                                <div><Label>Follow-Up Actions</Label><TA value={form.followUpActions ?? ''} onChange={v => setField('followUpActions', v)} placeholder="Scheduled reviews, inspections, or policy updates..." rows={2} /></div>
                                            </div>
                                        </div>
                                        <div className="bg-white rounded-xl border border-gray-200 p-6">
                                            <SectionHeader icon={CheckCircle2} title="Sign-off & Review" subtitle="Management review and internal notes" />
                                            <div className="space-y-4">
                                                <div className="grid grid-cols-2 gap-4">
                                                    <div><Label>Reviewed By</Label><TI value={form.reviewedBy ?? ''} onChange={v => setField('reviewedBy', v)} placeholder="Reviewer name or ID" /></div>
                                                    <div><Label>Review Date</Label><TI type="date" value={form.reviewedAt ?? ''} onChange={v => setField('reviewedAt', v)} /></div>
                                                </div>
                                                <div><Label>Internal Notes</Label><TA value={form.internalNotes ?? ''} onChange={v => setField('internalNotes', v)} placeholder="Confidential notes for internal audit purposes..." rows={3} /></div>
                                            </div>
                                        </div>
                                        {existingAudit && (
                                            <div className="text-xs text-gray-400 text-center pb-2">
                                                Last updated: {new Date(existingAudit.updatedAt).toLocaleString()}
                                            </div>
                                        )}
                                    </>
                                )}
                            </div>

                            {form && !drawerLoading && (
                                <div className="bg-white border-t border-gray-200 px-8 py-4 flex items-center justify-between gap-3 flex-shrink-0">
                                    <div className="flex items-center gap-2">
                                        <AlertTriangle className="w-4 h-4 text-slate-500" />
                                        <span className="text-xs text-gray-500">Submitting marks the violation as <span className="font-medium text-gray-700">Audited</span>.</span>
                                    </div>
                                    <div className="flex gap-2">
                                        <button onClick={() => save(0)} disabled={saving}
                                            className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 disabled:opacity-50">
                                            {saving ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Save className="w-3.5 h-3.5" />}Save Draft
                                        </button>
                                        <button onClick={() => save(1)} disabled={saving}
                                            className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-slate-700 rounded-lg hover:bg-slate-800 disabled:opacity-50">
                                            {saving ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Send className="w-3.5 h-3.5" />}
                                            {existingAudit && existingAudit.status >= 1 ? 'Update Submission' : 'Submit Audit'}
                                        </button>
                                        {existingAudit && existingAudit.status >= 1 && (
                                            <button onClick={() => save(2)} disabled={saving}
                                                className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-emerald-600 rounded-lg hover:bg-emerald-700 disabled:opacity-50">
                                                {saving ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <CheckCircle2 className="w-3.5 h-3.5" />}Mark Reviewed
                                            </button>
                                        )}
                                    </div>
                                </div>
                            )}
                        </div>
                    )}
                </div>
            )}

            {/* ── TAB: Audit Records ────────────────────────────────────── */}
            {activeTab === 'records' && (
                <AuditRecordsTab violations={violations} refreshKey={recordsRefreshKey} tenant={tenant} />
            )}

            {/* Toast */}
            {toast && (
                <div className={`fixed bottom-6 right-6 z-50 flex items-center gap-2 px-4 py-3 rounded-xl shadow-lg border text-sm font-medium ${toast.type === 'success' ? 'bg-white border-emerald-200 text-emerald-700' : 'bg-white border-red-200 text-red-700'}`}>
                    {toast.type === 'success' ? <CheckCircle2 className="w-4 h-4" /> : <AlertTriangle className="w-4 h-4" />}
                    {toast.msg}
                </div>
            )}
        </div>
    );
}
