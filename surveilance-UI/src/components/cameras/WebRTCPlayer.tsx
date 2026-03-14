'use client';

import React, { useEffect, useState, useRef } from 'react';
import { Camera, AlertCircle, Power } from 'lucide-react';
import type { CameraResponse } from '@/types/admin';

// Dynamically import the WHEP web component only on the client-side to prevent Next.js SSR crashes
if (typeof window !== 'undefined') {
    // @ts-ignore
    import('@eyevinn/whep-video-component').catch(console.error);
}

interface WebRTCPlayerProps {
    camera: CameraResponse;
    onToggleStream: (id: string, isStreaming: boolean) => Promise<void>;
}

export default function WebRTCPlayer({ camera, onToggleStream }: WebRTCPlayerProps) {
    const [error, setError] = useState<string | null>(null);
    const [isPlaying, setIsPlaying] = useState(false);
    const [isToggling, setIsToggling] = useState(false);
    const containerRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        if (!camera.isStreaming || !camera.whepUrl) {
            setIsPlaying(false);
            return;
        }

        // The @eyevinn/whep-video-component auto-negotiates the SDP on mount.
        // We just assume it is playing if it is streaming, the component will handle the WebRTC lifecycle.
        setIsPlaying(true);
        setError(null);

    }, [camera.whepUrl, camera.isStreaming]);

    const handleToggle = async () => {
        setIsToggling(true);
        try {
            await onToggleStream(camera.id, !camera.isStreaming);
        } finally {
            setIsToggling(false);
        }
    };

    return (
        <div ref={containerRef} className="relative rounded-lg border-4 border-gray-900 bg-black shadow-xl flex flex-col items-center justify-center min-h-[250px] group aspect-video overflow-hidden box-border">
            {camera.isStreaming && camera.whepUrl ? (
                // Use the Eyevinn WHEP web component for optimal Cloudflare playback and reconnects
                React.createElement('whep-video', {
                    src: camera.whepUrl,
                    autoplay: "true",
                    muted: "true",
                    playsinline: "true",
                    class: `w-full h-full object-cover transition-opacity duration-300 ${isPlaying ? 'opacity-100' : 'opacity-0'}`
                })
            ) : null}

            {/* Offline State */}
            {!camera.isStreaming && !error && (
                <div className="absolute inset-0 flex flex-col items-center justify-center text-gray-500 bg-gray-950">
                    <Power className="w-10 h-10 mb-3 opacity-30" />
                    <span className="text-sm font-semibold tracking-wider text-gray-500">STREAM OFFLINE</span>
                </div>
            )}

            {/* Connecting State */}
            {camera.isStreaming && !isPlaying && !error && (
                <div className="absolute inset-0 flex flex-col items-center justify-center text-gray-400 bg-gray-900/80 backdrop-blur-sm">
                    <span className="animate-spin mb-3 w-6 h-6 border-2 border-gray-500 border-t-white rounded-full"></span>
                    <span className="text-sm font-medium">Connecting...</span>
                </div>
            )}

            {/* Error State */}
            {camera.isStreaming && error && (
                <div className="absolute inset-0 flex flex-col items-center justify-center text-red-500 bg-gray-950 p-4 text-center">
                    <AlertCircle className="w-8 h-8 mb-2 opacity-80" />
                    <span className="text-xs font-semibold">{error}</span>
                </div>
            )}

            {/* Overlay Info Header */}
            <div className={`absolute top-0 left-0 right-0 p-3 bg-gradient-to-b from-black/90 to-transparent flex justify-between items-start pointer-events-none transition-opacity duration-300 ${camera.isStreaming ? 'opacity-0 group-hover:opacity-100' : 'opacity-100'}`}>
                <div className="flex items-center text-white text-sm font-medium gap-2">
                    <Camera className="w-4 h-4 text-gray-400" />
                    <span className="drop-shadow-md truncate max-w-[150px]">{camera.name || camera.cameraId}</span>
                </div>
                {camera.isStreaming && isPlaying && (
                    <div className="flex items-center gap-1.5 px-2 py-0.5 bg-red-600/90 backdrop-blur text-[10px] font-bold text-white uppercase tracking-wider rounded">
                        <span className="w-1.5 h-1.5 bg-white rounded-full animate-pulse"></span>
                        LIVE
                    </div>
                )}
            </div>

            {/* Stream Controls (Power Button) */}
            <div className={`absolute bottom-3 right-3 transition-opacity duration-200 pointer-events-auto ${camera.isStreaming ? 'opacity-0 group-hover:opacity-100' : 'opacity-100'}`}>
                <button
                    onClick={handleToggle}
                    disabled={isToggling}
                    title={camera.isStreaming ? "Turn stream off" : "Turn stream on"}
                    className={`p-2 rounded-full shadow-lg text-white transition-all transform hover:scale-105 active:scale-95
                        ${camera.isStreaming ? 'bg-red-500/90 hover:bg-red-600' : 'bg-green-600/90 hover:bg-green-500'} 
                        ${isToggling ? 'opacity-50 cursor-not-allowed' : ''}
                    `}
                >
                    {isToggling ? (
                        <span className="animate-spin w-4 h-4 border-2 border-white border-t-transparent rounded-full block"></span>
                    ) : (
                        <Power className="w-4 h-4" />
                    )}
                </button>
            </div>

            {/* TV glare overlay */}
            <div className="absolute inset-0 pointer-events-none bg-gradient-to-br from-white/5 to-transparent rounded-lg mix-blend-screen"></div>
        </div>
    );
}
