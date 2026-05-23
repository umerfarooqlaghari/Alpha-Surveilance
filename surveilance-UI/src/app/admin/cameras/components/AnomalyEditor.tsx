'use client';

import React, { useEffect, useMemo, useRef, useState } from 'react';
import { Sliders, X } from 'lucide-react';

interface AnomalyEditorProps {
    /** Current raw JSON value (round-tripped to/from `ruleConfigurationJson`). */
    value: string;
    /** Called with the new canonical JSON whenever the user edits. */
    onChange: (json: string) => void;
    /**
     * Suggested labels from the SOP — typically the same chips the SuperAdmin
     * already added in the trigger-labels editor. Used purely as one-click
     * shortcuts; the user may still type any custom label.
     */
    suggestedLabels?: string[];
}

interface ParsedAnomaly {
    minScore: number;
    targetLabels: string[];
    unknownType?: string;
    raw?: string;
}

function parseValue(raw: string): ParsedAnomaly {
    const fallback: ParsedAnomaly = { minScore: 0.5, targetLabels: [] };
    if (!raw || !raw.trim()) return fallback;
    try {
        const obj = JSON.parse(raw);
        if (!obj || typeof obj !== 'object') return { ...fallback, raw };
        const type = String(obj.type || '').toLowerCase();
        if (type && type !== 'anomaly') {
            return { ...fallback, unknownType: String(obj.type), raw };
        }
        const minScore = typeof obj.min_score === 'number'
            && obj.min_score >= 0 && obj.min_score <= 1
            ? obj.min_score
            : 0.5;
        const labels = Array.isArray(obj.target_labels)
            ? obj.target_labels.map((l: unknown) => String(l).trim()).filter(Boolean)
            : [];
        return { minScore, targetLabels: labels, raw };
    } catch {
        return { ...fallback, raw };
    }
}

function serialize(state: ParsedAnomaly): string {
    // Empty target_labels with default score = "no policy" — emit empty.
    if (!state.targetLabels.length && state.minScore <= 0) return '';
    return JSON.stringify({
        type: 'anomaly',
        min_score: Number(state.minScore.toFixed(3)),
        target_labels: state.targetLabels,
    });
}

/**
 * SuperAdmin-only editor for `type: "anomaly"` rules. These rules are non-
 * spatial — they filter detections by confidence and an optional label
 * whitelist. Pairs with [vision-inference-service/rules/anomaly.py].
 */
export default function AnomalyEditor({ value, onChange, suggestedLabels = [] }: AnomalyEditorProps) {
    const initial = useMemo(() => parseValue(value), []); // eslint-disable-line react-hooks/exhaustive-deps
    const [minScore, setMinScore] = useState<number>(initial.minScore);
    const [targetLabels, setTargetLabels] = useState<string[]>(initial.targetLabels);
    const [inputValue, setInputValue] = useState('');
    const userTouched = useRef(false);
    // Issue #5: keep a stable ref to onChange so the effect below never closes
    // over a stale callback when the parent re-renders with a new function ref.
    const onChangeRef = useRef(onChange);
    useEffect(() => { onChangeRef.current = onChange; });

    useEffect(() => {
        if (!userTouched.current) return;
        onChangeRef.current(serialize({ minScore, targetLabels }));
    }, [minScore, targetLabels]);

    const addLabel = (label: string) => {
        const t = label.trim().toLowerCase();
        if (!t) return;
        if (targetLabels.includes(t)) return;
        userTouched.current = true;
        setTargetLabels(prev => [...prev, t]);
    };

    const removeLabel = (label: string) => {
        userTouched.current = true;
        setTargetLabels(prev => prev.filter(l => l !== label));
    };

    const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
        if (e.key === 'Enter' || e.key === ',') {
            e.preventDefault();
            addLabel(inputValue);
            setInputValue('');
        } else if (e.key === 'Backspace' && !inputValue && targetLabels.length) {
            // Backspace on empty input pops the last chip.
            userTouched.current = true;
            setTargetLabels(prev => prev.slice(0, -1));
        }
    };

    const flush = () => {
        if (inputValue.trim()) {
            addLabel(inputValue);
            setInputValue('');
        }
    };

    const remainingSuggested = suggestedLabels
        .map(l => l.trim().toLowerCase())
        .filter(l => l && !targetLabels.includes(l));

    return (
        <div className="space-y-3">
            {initial.unknownType ? (
                <p className="text-[11px] text-amber-600 italic">
                    Existing config has type &quot;{initial.unknownType}&quot;; saving will rewrite it as an anomaly rule.
                </p>
            ) : null}

            {/* Confidence threshold */}
            <div>
                <label className="block text-[10px] font-bold text-gray-700 uppercase mb-1.5 flex items-center gap-1">
                    <Sliders className="w-3 h-3" />
                    Minimum confidence — {Math.round(minScore * 100)}%
                </label>
                <input
                    type="range"
                    min={0}
                    max={1}
                    step={0.01}
                    value={minScore}
                    onChange={(e) => {
                        userTouched.current = true;
                        setMinScore(Number(e.target.value));
                    }}
                    className="w-full accent-blue-600"
                />
                <p className="text-[10px] text-gray-500 mt-1">
                    Detections below this score are suppressed. Higher = fewer false positives, more missed defects.
                </p>
            </div>

            {/* Target labels chip input */}
            <div>
                <label className="block text-[10px] font-bold text-gray-700 uppercase mb-1.5">
                    Target labels (optional whitelist)
                </label>
                <div className="min-h-[36px] px-3 py-2 bg-white border border-gray-300 rounded-lg focus-within:ring-2 focus-within:ring-blue-400 flex flex-wrap gap-1.5 items-center text-black">
                    {targetLabels.map(tag => (
                        <span key={tag} className="flex items-center gap-1 px-2 py-0.5 bg-blue-100 text-blue-800 text-xs font-mono font-bold rounded border border-blue-200">
                            {tag}
                            <button
                                type="button"
                                onClick={() => removeLabel(tag)}
                                className="text-blue-400 hover:text-blue-700"
                                aria-label={`Remove ${tag}`}
                            >
                                <X className="w-2.5 h-2.5" />
                            </button>
                        </span>
                    ))}
                    <input
                        type="text"
                        value={inputValue}
                        onChange={(e) => setInputValue(e.target.value)}
                        onKeyDown={handleKeyDown}
                        onBlur={flush}
                        placeholder={targetLabels.length === 0 ? 'Type label, press Enter…' : 'Add more…'}
                        className="flex-1 min-w-[100px] outline-none text-xs bg-transparent"
                    />
                </div>
                <p className="text-[10px] text-gray-500 mt-1">
                    Leave empty to accept any label from the model. Use this to narrow alerts to specific defect classes (e.g. &quot;burnt&quot;, &quot;missing-label&quot;).
                </p>

                {remainingSuggested.length > 0 ? (
                    <div className="mt-2">
                        <p className="text-[10px] text-gray-500 mb-1">Quick add from SOP labels:</p>
                        <div className="flex flex-wrap gap-1.5">
                            {remainingSuggested.map(label => (
                                <button
                                    key={label}
                                    type="button"
                                    onClick={() => addLabel(label)}
                                    className="px-2 py-0.5 text-xs font-mono bg-gray-100 hover:bg-blue-100 text-gray-700 hover:text-blue-800 border border-gray-300 rounded"
                                >
                                    + {label}
                                </button>
                            ))}
                        </div>
                    </div>
                ) : null}
            </div>
        </div>
    );
}
