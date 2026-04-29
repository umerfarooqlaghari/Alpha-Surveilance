'use client';

import { useEffect, useRef, useState, use } from 'react';
import Script from 'next/script';
import { Camera, CheckCircle, Loader2, AlertCircle, ShieldCheck } from 'lucide-react';

export default function EnrollPage({ params }: { params: Promise<{ token: string }> }) {
    // React 19 unwrapping of params
    const resolvedParams = use(params);
    const token = resolvedParams.token;

    const [status, setStatus] = useState<'verifying' | 'loading_models' | 'starting_camera' | 'scanning' | 'submitting' | 'success' | 'error'>('verifying');
    const [errorMsg, setErrorMsg] = useState('');
    const [employeeInfo, setEmployeeInfo] = useState<{ employeeName: string, tenantName: string, status: string } | null>(null);
    const [guidance, setGuidance] = useState('Position your face in the oval');
    const [scriptLoaded, setScriptLoaded] = useState(false);

    const videoRef = useRef<HTMLVideoElement>(null);
    const canvasRef = useRef<HTMLCanvasElement>(null);
    const streamRef = useRef<MediaStream | null>(null);

    // Verify token
    useEffect(() => {
        const verifyToken = async () => {
            try {
                const bffUrl = process.env.NEXT_PUBLIC_BFF_URL || 'http://localhost:5002';
                const res = await fetch(`${bffUrl}/api/face-scan/verify-token?token=${token}`);
                if (!res.ok) {
                    throw new Error('Invalid or expired enrollment link.');
                }
                const data = await res.json();
                if (data.status === 'Completed') {
                    setStatus('error');
                    setErrorMsg('Face scan has already been completed.');
                    return;
                }
                setEmployeeInfo(data);
                setStatus('loading_models');
            } catch (err: any) {
                setStatus('error');
                setErrorMsg(err.message || 'Verification failed.');
            }
        };
        verifyToken();
    }, [token]);

    useEffect(() => {
        if (status === 'loading_models' && scriptLoaded) {
            initFaceApi();
        }
    }, [status, scriptLoaded]);

    const initFaceApi = async () => {
        try {
            // @ts-ignore
            const faceapi = window.faceapi;
            if (!faceapi) throw new Error('face-api.js failed to load');

            const MODEL_URL = 'https://cdn.jsdelivr.net/npm/@vladmandic/face-api/model/';
            await Promise.all([
                faceapi.nets.tinyFaceDetector.loadFromUri(MODEL_URL),
                faceapi.nets.faceLandmark68Net.loadFromUri(MODEL_URL),
                faceapi.nets.faceRecognitionNet.loadFromUri(MODEL_URL)
            ]);

            setStatus('starting_camera');
            startCamera();
        } catch (error: any) {
            setStatus('error');
            setErrorMsg('Failed to load AI models. ' + error.message);
        }
    };

    const startCamera = async () => {
        try {
            const stream = await navigator.mediaDevices.getUserMedia({
                video: { facingMode: 'user', width: { ideal: 640 }, height: { ideal: 480 } },
                audio: false
            });
            streamRef.current = stream;
            if (videoRef.current) {
                videoRef.current.srcObject = stream;
            }
        } catch (error) {
            setStatus('error');
            setErrorMsg('Camera access denied or unavailable.');
        }
    };

    const handleVideoPlay = () => {
        setStatus('scanning');
        // @ts-ignore
        const faceapi = window.faceapi;
        let consecutiveGoodFrames = 0;
        let isProcessing = false;

        const scanInterval = setInterval(async () => {
            if (isProcessing || status === 'submitting' || status === 'success') return;
            isProcessing = true;

            try {
                if (!videoRef.current || videoRef.current.paused || videoRef.current.ended) {
                    isProcessing = false;
                    return;
                }

                const detections = await faceapi.detectAllFaces(videoRef.current, new faceapi.TinyFaceDetectorOptions({ inputSize: 224, scoreThreshold: 0.6 }))
                    .withFaceLandmarks()
                    .withFaceDescriptors();

                // Handle canvas drawing
                if (canvasRef.current) {
                    const displaySize = { width: videoRef.current.videoWidth, height: videoRef.current.videoHeight };
                    faceapi.matchDimensions(canvasRef.current, displaySize);
                    const resizedDetections = faceapi.resizeResults(detections, displaySize);
                    canvasRef.current.getContext('2d')?.clearRect(0, 0, canvasRef.current.width, canvasRef.current.height);
                    
                    // Draw a guide oval (larger)
                    const ctx = canvasRef.current.getContext('2d');
                    if (ctx) {
                        ctx.beginPath();
                        ctx.ellipse(displaySize.width / 2, displaySize.height / 2, 140, 180, 0, 0, 2 * Math.PI);
                        ctx.lineWidth = 4;
                        ctx.strokeStyle = detections.length === 1 && consecutiveGoodFrames > 0 ? '#10b981' : '#fff';
                        ctx.stroke();
                        ctx.fillStyle = 'rgba(0,0,0,0.5)';
                        // invert mask
                        ctx.rect(displaySize.width, 0, -displaySize.width, displaySize.height);
                        ctx.fill('evenodd');
                    }
                }

                if (detections.length === 0) {
                    setGuidance('No face detected. Look at the camera.');
                    consecutiveGoodFrames = 0;
                } else if (detections.length > 1) {
                    setGuidance('Multiple faces detected. Please ensure only you are in frame.');
                    consecutiveGoodFrames = 0;
                } else {
                    const det = detections[0];
                    const box = det.detection.box;
                    const displaySize = { width: videoRef.current.videoWidth, height: videoRef.current.videoHeight };
                    
                    // Simple alignment checks (looser)
                    const isCenteredX = box.x + box.width / 2 > displaySize.width * 0.25 && box.x + box.width / 2 < displaySize.width * 0.75;
                    const isCenteredY = box.y + box.height / 2 > displaySize.height * 0.2 && box.y + box.height / 2 < displaySize.height * 0.8;
                    const isRightSize = box.width > displaySize.width * 0.2 && box.height > displaySize.height * 0.2;

                    if (!isRightSize) {
                        setGuidance('Move closer');
                        consecutiveGoodFrames = 0;
                    } else if (!isCenteredX || !isCenteredY) {
                        setGuidance('Center your face in the oval');
                        consecutiveGoodFrames = 0;
                    } else {
                        setGuidance('Hold still...');
                        consecutiveGoodFrames++;

                        if (consecutiveGoodFrames > 10) { // About 1.0 seconds at 10fps
                            clearInterval(scanInterval);
                            submitEmbedding(det.descriptor);
                        }
                    }
                }
            } catch (err) {
                console.error(err);
            }
            isProcessing = false;
        }, 100);
    };

    const submitEmbedding = async (descriptor: Float32Array) => {
        setStatus('submitting');
        
        // Capture photo for reference
        let photoDataUrl = '';
        if (videoRef.current) {
            const canvas = document.createElement('canvas');
            canvas.width = videoRef.current.videoWidth;
            canvas.height = videoRef.current.videoHeight;
            canvas.getContext('2d')?.drawImage(videoRef.current, 0, 0);
            photoDataUrl = canvas.toDataURL('image/jpeg', 0.8);
        }

        // Stop camera
        if (streamRef.current) {
            streamRef.current.getTracks().forEach(t => t.stop());
        }

        try {
            const embeddingArray = Array.from(descriptor);
            const bffUrl = process.env.NEXT_PUBLIC_BFF_URL || 'http://localhost:5002';
            
            const res = await fetch(`${bffUrl}/api/face-scan/submit`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    token,
                    embedding: embeddingArray,
                    photoUrl: photoDataUrl
                })
            });

            if (!res.ok) {
                const text = await res.text();
                throw new Error(text || 'Submission failed');
            }

            setStatus('success');
        } catch (error: any) {
            setStatus('error');
            setErrorMsg('Failed to submit scan: ' + error.message);
        }
    };

    if (status === 'success') {
        return (
            <div className="min-h-screen bg-gray-900 flex flex-col items-center justify-center p-6 text-center">
                <div className="w-24 h-24 bg-green-500/20 rounded-full flex items-center justify-center mb-6">
                    <ShieldCheck className="w-12 h-12 text-green-500" />
                </div>
                <h1 className="text-3xl font-bold text-white mb-2">Enrollment Complete</h1>
                <p className="text-gray-400 mb-8 max-w-md">Your face scan has been successfully registered to {employeeInfo?.tenantName}. You may now close this window.</p>
                <div className="bg-gray-800 rounded-xl p-4 inline-block text-left border border-gray-700">
                    <p className="text-sm text-gray-400">Enrolled as</p>
                    <p className="font-semibold text-white">{employeeInfo?.employeeName}</p>
                </div>
            </div>
        );
    }

    if (status === 'error') {
        return (
            <div className="min-h-screen bg-gray-900 flex flex-col items-center justify-center p-6 text-center">
                <div className="w-20 h-20 bg-red-500/20 rounded-full flex items-center justify-center mb-6">
                    <AlertCircle className="w-10 h-10 text-red-500" />
                </div>
                <h1 className="text-2xl font-bold text-white mb-2">Enrollment Failed</h1>
                <p className="text-red-400 mb-8">{errorMsg}</p>
            </div>
        );
    }

    return (
        <div className="min-h-screen bg-gray-900 text-white flex flex-col items-center p-4">
            <Script 
                src="https://cdn.jsdelivr.net/npm/@vladmandic/face-api/dist/face-api.js" 
                strategy="afterInteractive"
                onLoad={() => {
                    setScriptLoaded(true);
                }}
            />

            <div className="w-full max-w-md mt-8 mb-6 text-center">
                <h1 className="text-2xl font-bold">Face Scan Enrollment</h1>
                {employeeInfo && (
                    <p className="text-gray-400 text-sm mt-1">For {employeeInfo.employeeName} ({employeeInfo.tenantName})</p>
                )}
            </div>

            <div className="relative w-full max-w-md aspect-[3/4] bg-black rounded-3xl overflow-hidden shadow-2xl border-4 border-gray-800">
                {(status === 'verifying' || status === 'loading_models' || status === 'starting_camera') && (
                    <div className="absolute inset-0 flex flex-col items-center justify-center bg-gray-900 z-20">
                        <Loader2 className="w-10 h-10 animate-spin text-blue-500 mb-4" />
                        <p className="text-sm font-medium text-gray-300">
                            {status === 'verifying' && 'Verifying secure link...'}
                            {status === 'loading_models' && 'Loading AI models (may take a moment)...'}
                            {status === 'starting_camera' && 'Starting camera...'}
                        </p>
                    </div>
                )}
                
                {status === 'submitting' && (
                    <div className="absolute inset-0 flex flex-col items-center justify-center bg-gray-900/90 z-20 backdrop-blur-sm">
                        <Loader2 className="w-10 h-10 animate-spin text-green-500 mb-4" />
                        <p className="text-sm font-medium text-green-400">Processing and securing scan...</p>
                    </div>
                )}

                <video 
                    ref={videoRef} 
                    className="absolute inset-0 w-full h-full object-cover mirror-mode"
                    autoPlay 
                    playsInline 
                    muted 
                    onPlay={handleVideoPlay}
                />
                <canvas 
                    ref={canvasRef} 
                    className="absolute inset-0 w-full h-full pointer-events-none"
                />

                {status === 'scanning' && (
                    <div className="absolute bottom-8 left-0 right-0 flex justify-center z-10">
                        <div className={`px-6 py-3 rounded-full font-medium shadow-lg backdrop-blur-md transition-colors ${
                            guidance === 'Hold still...' ? 'bg-green-500/90 text-white' : 'bg-black/60 text-white'
                        }`}>
                            {guidance}
                        </div>
                    </div>
                )}
            </div>

            <div className="mt-8 text-center text-sm text-gray-500 max-w-xs">
                <p><Camera className="inline-block w-4 h-4 mr-1 mb-0.5"/> Follow the on-screen instructions. Ensure you are in a well-lit environment.</p>
            </div>

            <style dangerouslySetInnerHTML={{__html: `
                .mirror-mode {
                    transform: scaleX(-1);
                }
            `}} />
        </div>
    );
}
