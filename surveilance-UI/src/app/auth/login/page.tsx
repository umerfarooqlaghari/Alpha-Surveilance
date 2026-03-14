"use client";

import { useState, useEffect } from "react";
import { useRouter } from "next/navigation";
import Cookies from "js-cookie";
import { Eye, EyeOff } from "lucide-react";

const LS_KEY = "auth_remember";

export default function LoginPage() {
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [rememberMe, setRememberMe] = useState(false);
    const [showPw, setShowPw] = useState(false);
    const [error, setError] = useState("");
    const router = useRouter();

    // Restore saved credentials
    useEffect(() => {
        try {
            const saved = localStorage.getItem(LS_KEY);
            if (saved) {
                const { em, pw } = JSON.parse(saved);
                setEmail(em || "");
                setPassword(pw || "");
                setRememberMe(true);
            }
        } catch { /* ignore */ }
    }, []);

    const handleLogin = async (e: React.FormEvent) => {
        e.preventDefault();
        setError("");

        if (rememberMe) {
            localStorage.setItem(LS_KEY, JSON.stringify({ em: email, pw: password }));
        } else {
            localStorage.removeItem(LS_KEY);
        }

        try {
            const res = await fetch("/api/auth/login", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ email, password }),
            });

            if (res.ok) {
                const data = await res.json();
                if (data.token) {
                    Cookies.set("token", data.token, { expires: rememberMe ? 7 : 1 / 24 });
                    router.push("/");
                } else {
                    Cookies.set("auth_email", email);
                    router.push("/auth/verify");
                }
            } else {
                const data = await res.json();
                setError(data.title || "Invalid credentials");
            }
        } catch {
            setError("Login failed. Check server.");
        }
    };

    return (
        <div className="flex min-h-screen items-center justify-center bg-zinc-900 text-white">
            <div className="w-full max-w-md p-8 bg-black border border-zinc-800 rounded-lg shadow-xl">
                <h2 className="text-3xl font-bold mb-6 text-center text-red-500">Admin Access</h2>

                <form onSubmit={handleLogin} className="space-y-5">
                    {/* Email */}
                    <div>
                        <label className="block text-sm font-medium text-zinc-400 mb-1">Email</label>
                        <input type="email" value={email} onChange={e => setEmail(e.target.value)} required
                            className="mt-1 block w-full px-3 py-2 bg-zinc-900 border border-zinc-700 rounded-md text-white focus:outline-none focus:ring-2 focus:ring-red-500" />
                    </div>

                    {/* Password with toggle */}
                    <div>
                        <label className="block text-sm font-medium text-zinc-400 mb-1">Password</label>
                        <div className="relative mt-1">
                            <input type={showPw ? "text" : "password"} value={password}
                                onChange={e => setPassword(e.target.value)} required
                                className="block w-full px-3 py-2 pr-10 bg-zinc-900 border border-zinc-700 rounded-md text-white focus:outline-none focus:ring-2 focus:ring-red-500" />
                            <button type="button" tabIndex={-1} onClick={() => setShowPw(p => !p)}
                                className="absolute right-3 top-1/2 -translate-y-1/2 text-zinc-500 hover:text-zinc-300 transition-colors">
                                {showPw ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                            </button>
                        </div>
                    </div>

                    {/* Remember Me */}
                    <div className="flex items-center gap-2.5">
                        <input id="rememberMe" type="checkbox" checked={rememberMe}
                            onChange={e => setRememberMe(e.target.checked)}
                            className="w-4 h-4 rounded border-zinc-600 text-red-500 bg-zinc-800 focus:ring-red-500 cursor-pointer" />
                        <label htmlFor="rememberMe" className="text-sm text-zinc-400 cursor-pointer select-none">
                            Remember me on this device
                        </label>
                    </div>

                    {error && <p className="text-red-500 text-sm">{error}</p>}

                    <button type="submit"
                        className="w-full flex justify-center py-2 px-4 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-red-600 hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500">
                        Send Login Code
                    </button>
                </form>
            </div>
        </div>
    );
}
