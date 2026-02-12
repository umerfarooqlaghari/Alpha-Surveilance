"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Cookies from "js-cookie";

export default function LoginPage() {
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [error, setError] = useState("");
    const router = useRouter();

    const handleLogin = async (e: React.FormEvent) => {
        e.preventDefault();
        setError("");

        try {
            const res = await fetch("/api/auth/login", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ email, password }),
            });

            if (res.ok) {
                const data = await res.json();

                // Direct Login (No OTP)
                if (data.token) {
                    Cookies.set("token", data.token, { expires: 1 / 24 });
                    router.push("/");
                } else {
                    // Fallback
                    Cookies.set("auth_email", email);
                    router.push("/auth/verify");
                }
            } else {
                const data = await res.json();
                setError(data.title || "Invalid credentials");
            }
        } catch (err) {
            setError("Login failed. Check server.");
        }
    };

    return (
        <div className="flex min-h-screen items-center justify-center bg-zinc-900 text-white">
            <div className="w-full max-w-md p-8 bg-black border border-zinc-800 rounded-lg shadow-xl">
                <h2 className="text-3xl font-bold mb-6 text-center text-red-500">
                    Admin Access
                </h2>
                <form onSubmit={handleLogin} className="space-y-6">
                    <div>
                        <label className="block text-sm font-medium text-zinc-400">Email</label>
                        <input
                            type="email"
                            value={email}
                            onChange={(e) => setEmail(e.target.value)}
                            className="mt-1 block w-full px-3 py-2 bg-zinc-900 border border-zinc-700 rounded-md text-white focus:outline-none focus:ring-2 focus:ring-red-500"
                            required
                        />
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-zinc-400">Password</label>
                        <input
                            type="password"
                            value={password}
                            onChange={(e) => setPassword(e.target.value)}
                            className="mt-1 block w-full px-3 py-2 bg-zinc-900 border border-zinc-700 rounded-md text-white focus:outline-none focus:ring-2 focus:ring-red-500"
                            required
                        />
                    </div>
                    {error && <p className="text-red-500 text-sm">{error}</p>}
                    <button
                        type="submit"
                        className="w-full flex justify-center py-2 px-4 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-red-600 hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500"
                    >
                        Send Login Code
                    </button>
                </form>
            </div>
        </div>
    );
}
