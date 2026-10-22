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
        onloadTurnstileCallback?: () => void;
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

            const renderWidget = () => {
                if (!containerRef.current || !window.turnstile || !isMounted) return;

                // Remove existing widget before re-rendering in the same container
                if (widgetIdRef.current) {
                    try {
                        window.turnstile.remove(widgetIdRef.current);
                    } catch {
                        // ignore
                    }
                    widgetIdRef.current = null;
                }

                try {
                    const id = window.turnstile.render(containerRef.current, {
                        sitekey: TURNSTILE_SITE_KEY,
                        callback: (token: string) => {
                            if (isMounted) {
                                onVerify(token);
                            }
                        },
                        'expired-callback': () => {
                            if (isMounted && onExpire) {
                                onExpire();
                            }
                        },
                        'error-callback': (err) => {
                            if (isMounted && onError) {
                                onError(err);
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

            // Check if Cloudflare Turnstile script is loaded
            if (window.turnstile) {
                renderWidget();
            } else {
                // Ensure script tag exists
                const existingScript = document.getElementById('cf-turnstile-script');
                if (!existingScript) {
                    const script = document.createElement('script');
                    script.id = 'cf-turnstile-script';
                    script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
                    script.async = true;
                    script.defer = true;
                    script.onload = () => {
                        if (isMounted) renderWidget();
                    };
                    document.head.appendChild(script);
                } else {
                    // Script tag is already in head, poll briefly until window.turnstile appears
                    const interval = setInterval(() => {
                        if (window.turnstile) {
                            clearInterval(interval);
                            if (isMounted) renderWidget();
                        }
                    }, 50);

                    return () => {
                        isMounted = false;
                        clearInterval(interval);
                    };
                }
            }

            return () => {
                isMounted = false;
                if (widgetIdRef.current && window.turnstile) {
                    try {
                        window.turnstile.remove(widgetIdRef.current);
                    } catch {
                        // ignore
                    }
                    widgetIdRef.current = null;
                }
            };
        }, [theme, size, action, onVerify, onExpire, onError]);

        return (
            <div className={`turnstile-container flex justify-center items-center my-2.5 min-h-[65px] ${className}`}>
                <div ref={containerRef} />
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
