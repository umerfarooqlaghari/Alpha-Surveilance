#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
# run.sh — Standalone launcher for the Human ReID Service
#
# Usage:
#   chmod +x run.sh
#   ./run.sh
#
# Prerequisites:
#   1. Copy .env.example → .env and fill in your DATABASE_URL
#   2. Ensure PostgreSQL is running with the pgvector extension available
# ──────────────────────────────────────────────────────────────────────────────
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# ── 1. Virtual environment ──────────────────────────────────────────────────
if [ ! -d "venv" ]; then
    echo "🔧 Creating virtual environment..."
    python3 -m venv venv
fi

# shellcheck disable=SC1091
source venv/bin/activate

# ── 2. Dependencies ─────────────────────────────────────────────────────────
echo "📦 Installing / verifying dependencies..."
pip install -q -r requirements.txt

# ── 3. Load .env (if present) ───────────────────────────────────────────────
if [ -f ".env" ]; then
    echo "🔑 Loading .env..."
    set -o allexport
    # shellcheck disable=SC1091
    source .env
    set +o allexport
else
    echo "⚠️  No .env file found. Copy .env.example → .env and set DATABASE_URL."
fi

# ── 4. Validate required vars ───────────────────────────────────────────────
if [ -z "${DATABASE_URL:-}" ]; then
    echo "❌  DATABASE_URL is not set. Aborting."
    exit 1
fi

PORT="${PORT:-8001}"

# ── 5. Start the server ─────────────────────────────────────────────────────
echo ""
echo "🚀 Starting Human ReID Service on http://0.0.0.0:${PORT}"
echo "   Docs: http://localhost:${PORT}/docs"
echo ""

exec uvicorn app.main:app --host 0.0.0.0 --port "$PORT" --reload
