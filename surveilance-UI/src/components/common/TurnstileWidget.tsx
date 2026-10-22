'use client';

import { useEffect, useRef, useState, useImperativeHandle, forwardRef } from 'react';

declare global {
    interface Window {
        turnstile?: {
            render: (
                container: string | HTMLElement,
                options: {
                    sitekey: string;
                    callback?: (token: string) => void;
                    'error-callback'?: (code?: any) => void;
                    'expired-callback'?: () => void;
                    theme?: 'light' | 'dark' | 'auto';
                    size?: 'normal' | 'compact' | 'flexible';
                    action?: string;
                    cData?: string;
                }
            ) => string;
            reset: (widgetId?: string) => void;
            remove: (widgetId?: string) => void;
        };
    }
}

export interface TurnstileWidgetRef {
    reset: () => void;
}

interface TurnstileWidgetProps {
    onVerify: (token: string) => void;
    onExpire?: () => void;
    onError?: (error?: any) => void;
    theme?: 'light' | 'dark' | 'auto';
    size?: 'normal' | 'compact' | 'flexible';
    action?: string;
    className?: string;
}

const TURNSTILE_SITE_KEY =
    process.env.NEXT_PUBLIC_CLOUDFLARE_TURNSTILE_SITE_KEY || '0x4AAAAAAFgeekEOypFMwymF';

export const TurnstileWidget = forwardRef<TurnstileWidgetRef, TurnstileWidgetProps>(
    function TurnstileWidget(
        { onVerify, onExpire, onError, theme = 'light', size = 'normal', action, className = '' },
        ref
    ) {
        const containerRef = useRef<HTMLDivElement>(null);
        const widgetIdRef = useRef<string | null>(null);
        const [isReady, setIsReady] = useState(false);

        // Keep fresh references to callbacks without re-triggering the widget lifecycle
        const onVerifyRef = useRef(onVerify);
        const onExpireRef = useRef(onExpire);
        const onErrorRef = useRef(onError);

        useEffect(() => {
            onVerifyRef.current = onVerify;
            onExpireRef.current = onExpire;
            onErrorRef.current = onError;
        });

        // Expose reset method to parent component
        useImperativeHandle(ref, () => ({
            reset: () => {
                if (widgetIdRef.current && window.turnstile) {
                    try {
                        window.turnstile.reset(widgetIdRef.current);
                    } catch {
                        // ignore reset errors
                    }
                }
            },
        }));

        useEffect(() => {
            let isMounted = true;
            let checkInterval: ReturnType<typeof setInterval> | null = null;

            const renderWidget = () => {
                if (!isMounted || !containerRef.current || !window.turnstile) return;
                // If this instance has already rendered its widget, do not re-render
                if (widgetIdRef.current) return;

                try {
                    containerRef.current.innerHTML = '';
                    const id = window.turnstile.render(containerRef.current, {
                        sitekey: TURNSTILE_SITE_KEY,
                        callback: (token: string) => {
                            if (isMounted) {
                                setIsReady(true);
                                onVerifyRef.current?.(token);
                            }
                        },
                        'expired-callback': () => {
                            if (isMounted) {
                                onExpireRef.current?.();
                            }
                        },
                        'error-callback': (err: any) => {
                            if (isMounted) {
                                console.warn('[Turnstile] Challenge error:', err);
                                onErrorRef.current?.(err);
                            }
                        },
                        theme,
                        size,
                        action,
                    });
                    widgetIdRef.current = id;
                    setIsReady(true);
                } catch (e) {
                    console.error('[Turnstile] Render error:', e);
                }
            };

            if (window.turnstile) {
                renderWidget();
            } else {
                // Ensure script tag exists if not already loaded by layout
                const SCRIPT_ID = 'cf-turnstile-script';
                let script = document.getElementById(SCRIPT_ID) as HTMLScriptElement | null;
                if (!script) {
                    script = document.createElement('script');
                    script.id = SCRIPT_ID;
                    script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
                    script.async = true;
                    script.defer = true;
                    document.head.appendChild(script);
                }

                checkInterval = setInterval(() => {
                    if (window.turnstile) {
                        if (checkInterval) clearInterval(checkInterval);
                        renderWidget();
                    }
                }, 100);
            }

            return () => {
                isMounted = false;
                if (checkInterval) clearInterval(checkInterval);
                if (widgetIdRef.current && window.turnstile) {
                    try {
                        window.turnstile.remove(widgetIdRef.current);
                    } catch {
                        // ignore
                    }
                    widgetIdRef.current = null;
                }
            };
        }, [theme, size, action]);

        return (
            <div className={`turnstile-container relative flex justify-center items-center my-2.5 min-h-[65px] ${className}`}>
                <div ref={containerRef} className="flex justify-center items-center" />
                {!isReady && (
                    <div className="text-[11px] text-[#737373] animate-pulse flex items-center gap-1.5 py-2">
                        <span className="w-2 h-2 rounded-full bg-[#18181b]/30 animate-ping" />
                        <span>Verifying security check...</span>
                    </div>
                )}
            </div>
        );
    }
);
