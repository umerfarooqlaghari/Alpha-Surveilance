"use client";

import { useState, useEffect } from "react";
import { useRouter } from "next/navigation";
import Cookies from "js-cookie";

export default function VerifyPage() {
    const [otp, setOtp] = useState("");
    const [error, setError] = useState("");
    const router = useRouter();

    useEffect(() => {
        if (!Cookies.get("auth_email")) {
            router.push("/auth/login");
        }
    }, [router]);

    const handleVerify = async (e: React.FormEvent) => {
        e.preventDefault();
        setError("");

        const email = Cookies.get("auth_email");

        try {
            const res = await fetch("/api/auth/verify-otp", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ email, otp }),
            });

            if (res.ok) {
                const data = await res.json();
                const token = data.token;

                // Save Token
                Cookies.set("token", token, { expires: 1 / 24 }); // 1 hour
                Cookies.remove("auth_email"); // Clean up temp email

                router.push("/");
            } else {
                const data = await res.json();
                setError(data.title || "Invalid Code");
            }
        } catch (err) {
            setError("Verification failed.");
        }
    };

    return (
        <div className="flex min-h-screen items-center justify-center bg-zinc-900 text-white">
            <div className="w-full max-w-md p-8 bg-black border border-zinc-800 rounded-lg shadow-xl">
                <h2 className="text-3xl font-bold mb-6 text-center text-red-500">
                    Two-Factor Authentication
                </h2>
                <p className="text-center text-zinc-400 mb-6">
                    Enter the 6-digit code sent to your email.
                </p>
                <form onSubmit={handleVerify} className="space-y-6">
                    <div>
                        <label className="block text-sm font-medium text-zinc-400">Security Code</label>
                        <input
                            type="text"
                            maxLength={6}
                            value={otp}
                            onChange={(e) => setOtp(e.target.value)}
                            className="mt-1 block w-full px-3 py-2 bg-zinc-900 border border-zinc-700 rounded-md text-white text-center text-2xl tracking-widest focus:outline-none focus:ring-2 focus:ring-red-500"
                            required
                        />
                    </div>
                    {error && <p className="text-red-500 text-sm">{error}</p>}
                    <button
                        type="submit"
                        className="w-full flex justify-center py-2 px-4 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-green-600 hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500"
                    >
                        Verify & Dashboard
                    </button>
                </form>
            </div>
        </div>
    );
}
