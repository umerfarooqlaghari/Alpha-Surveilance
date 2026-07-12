"""Tests for the production-hardening fixes (R1, R2, R4, R5, G1, G4, G5).

A real Postgres is not assumed to be available: the DB layer is mocked where
needed and the tests focus on the service's HTTP behaviour, SQL construction
and recovery logic.
"""
import os
import sys
import uuid
from datetime import datetime, timezone
from unittest import mock

# Must be set before app.database is imported (module-level engine creation).
os.environ.setdefault("DATABASE_URL", "postgresql://reid:reid@localhost:5432/reid_test")

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

import pytest
from fastapi.routing import APIRoute
from fastapi.testclient import TestClient
from sqlalchemy.dialects import postgresql

from app import database, models, schemas
from app import main as main_mod

TENANT = str(uuid.uuid4())
DIM = models.EMBEDDING_DIM


def _vec(n: int) -> list[float]:
    return [0.01] * n


# ---------------------------------------------------------------------------
# R1 — degraded mode recovery + rate limiting
# ---------------------------------------------------------------------------
class TestHealthRecovery:
    def test_health_503_then_recovers_with_rate_limit(self):
        with mock.patch.object(main_mod.database, "init_db") as init_db:
            init_db.side_effect = RuntimeError("db down at boot")
            with TestClient(main_mod.app) as client:
                # Startup failed -> degraded.
                assert client.get("/health").status_code == 503
                # Startup attempt counts as the last attempt; /health within the
                # retry window must NOT re-run init_db (rate limiting).
                assert init_db.call_count == 1

                # DB "comes back", but we are still inside the retry window.
                init_db.side_effect = None
                assert client.get("/health").status_code == 503
                assert init_db.call_count == 1

                # Expire the rate-limit window -> next /health re-attempts and
                # flips db_ready without a process restart (no kill loop).
                main_mod.app.state.last_db_attempt -= (
                    main_mod.DB_RETRY_INTERVAL_SECONDS + 1
                )
                resp = client.get("/health")
                assert resp.status_code == 200
                assert resp.json() == {"status": "healthy", "service": "human-reid"}
                assert init_db.call_count == 2
                # Recovery attempt must be a single quick try, not the 5x3s ladder.
                assert init_db.call_args.kwargs["max_retries"] == 1

                # Once healthy, no further init_db churn.
                assert client.get("/health").status_code == 200
                assert init_db.call_count == 2

    def test_db_endpoints_return_503_with_clear_message_while_degraded(self):
        with mock.patch.object(main_mod.database, "init_db") as init_db:
            init_db.side_effect = RuntimeError("db down")
            with TestClient(main_mod.app) as client:
                r = client.post(
                    "/search",
                    json={"tenant_id": TENANT, "embedding": _vec(DIM)},
                )
                assert r.status_code == 503
                assert "degraded" in r.json()["detail"]

                r = client.delete(
                    f"/embeddings/person/emp-1?tenant_id={TENANT}"
                )
                assert r.status_code == 503

    def test_health_ok_when_startup_succeeds(self):
        with mock.patch.object(main_mod.database, "init_db"):
            with TestClient(main_mod.app) as client:
                assert client.get("/health").status_code == 200


# ---------------------------------------------------------------------------
# R4 — embedding length validation (422 on wrong-length vectors)
# ---------------------------------------------------------------------------
class TestEmbeddingValidation:
    @pytest.fixture()
    def client(self):
        with mock.patch.object(main_mod.database, "init_db"):
            with TestClient(main_mod.app) as client:
                yield client
        main_mod.app.dependency_overrides.clear()

    def test_create_embedding_rejects_64d_vector(self, client):
        r = client.post(
            "/embeddings",
            json={"tenant_id": TENANT, "embedding": _vec(64)},
        )
        assert r.status_code == 422
        assert f"exactly {DIM} dimensions" in str(r.json())

    def test_search_rejects_wrong_length_vector(self, client):
        r = client.post(
            "/search",
            json={"tenant_id": TENANT, "embedding": _vec(DIM + 1)},
        )
        assert r.status_code == 422

    def test_correct_length_passes_schema_validation(self):
        req = schemas.EmbeddingCreate(tenant_id=TENANT, embedding=_vec(DIM))
        assert len(req.embedding) == DIM
        # G5: metadata default is a fresh dict per instance, not shared.
        a = schemas.EmbeddingCreate(tenant_id=TENANT, embedding=_vec(DIM))
        b = schemas.EmbeddingCreate(tenant_id=TENANT, embedding=_vec(DIM))
        a.metadata_json["k"] = "v"
        assert b.metadata_json == {}


# ---------------------------------------------------------------------------
# G1 — single delete route + invalid UUID handled as 400
# ---------------------------------------------------------------------------
class TestDeleteRoute:
    def test_duplicate_delete_route_removed(self):
        delete_routes = [
            r
            for r in main_mod.app.routes
            if isinstance(r, APIRoute) and "DELETE" in r.methods
        ]
        assert len(delete_routes) == 1
        # This is the path FaceScanController.cs calls.
        assert delete_routes[0].path == "/embeddings/person/{person_id}"

    def test_invalid_tenant_uuid_returns_400_not_500(self):
        with mock.patch.object(main_mod.database, "init_db"):
            with TestClient(main_mod.app) as client:
                r = client.delete("/embeddings/person/emp-1?tenant_id=not-a-uuid")
                assert r.status_code == 400
                assert "not a valid UUID" in r.json()["detail"]

    def test_valid_delete_uses_mocked_session(self):
        session = mock.MagicMock()
        session.query.return_value.filter.return_value.delete.return_value = 3
        with mock.patch.object(main_mod.database, "init_db"):
            main_mod.app.dependency_overrides[database.get_db] = lambda: session
            try:
                with TestClient(main_mod.app) as client:
                    r = client.delete(f"/embeddings/person/emp-1?tenant_id={TENANT}")
                    assert r.status_code == 200
                    assert r.json() == {"deleted": 3, "person_id": "emp-1"}
                    session.commit.assert_called_once()
            finally:
                main_mod.app.dependency_overrides.clear()


# ---------------------------------------------------------------------------
# R5/G2 — search pushes threshold + per-person dedup into SQL, dedup BEFORE limit
# ---------------------------------------------------------------------------
class TestSearchSql:
    def _statement_sql(self, top_k=3, threshold=0.8):
        req = schemas.SearchRequest(
            tenant_id=TENANT, embedding=_vec(DIM), top_k=top_k, threshold=threshold
        )
        stmt = main_mod._build_search_statement(req)
        compiled = stmt.compile(dialect=postgresql.dialect())
        return str(compiled), compiled.params

    def test_sql_contains_distinct_on_threshold_and_limit_in_order(self):
        sql, params = self._statement_sql(top_k=3, threshold=0.8)
        assert "DISTINCT ON" in sql
        assert "<=>" in sql  # pgvector cosine distance operator
        assert "LIMIT" in sql
        # Dedup must happen in the inner query, limit in the outer query:
        assert sql.index("DISTINCT ON") < sql.index("LIMIT")
        # Outer ordering is by score descending.
        assert "ORDER BY best_per_person.score DESC" in sql
        # Threshold applied in SQL as distance <= 1 - threshold (= 0.2).
        float_params = [v for v in params.values() if isinstance(v, float)]
        assert any(abs(v - 0.2) < 1e-9 for v in float_params)
        # top_k applied as the LIMIT parameter.
        assert 3 in params.values()

    def test_search_endpoint_response_shape_unchanged(self):
        """The vision caller parses item['person_id'] and item['score'] from a
        JSON list — assert that exact shape survives the SQL rewrite."""
        row = {
            "id": uuid.uuid4(),
            "person_id": "emp-42",
            "score": 0.91,
            "frame_url": None,
            "created_at": datetime.now(timezone.utc),
        }
        session = mock.MagicMock()
        session.execute.return_value.mappings.return_value.all.return_value = [row]
        with mock.patch.object(main_mod.database, "init_db"):
            main_mod.app.dependency_overrides[database.get_db] = lambda: session
            try:
                with TestClient(main_mod.app) as client:
                    r = client.post(
                        "/search",
                        json={"tenant_id": TENANT, "embedding": _vec(DIM)},
                    )
                    assert r.status_code == 200
                    body = r.json()
                    assert isinstance(body, list) and len(body) == 1
                    assert body[0]["person_id"] == "emp-42"
                    assert body[0]["score"] == pytest.approx(0.91)
                    assert set(body[0].keys()) == {
                        "id",
                        "person_id",
                        "score",
                        "frame_url",
                        "created_at",
                    }
            finally:
                main_mod.app.dependency_overrides.clear()


# ---------------------------------------------------------------------------
# R2 — engine configuration
# ---------------------------------------------------------------------------
class TestEngineConfig:
    def test_connect_timeout_and_pool_limits(self):
        assert database.engine.pool._pre_ping is True
        assert database.engine.pool.size() == database.DB_POOL_SIZE
        assert database.engine.pool._max_overflow == database.DB_MAX_OVERFLOW
        assert database.engine.pool._timeout == database.DB_POOL_TIMEOUT_SECONDS
        # connect_timeout must be passed to create_engine for Postgres URLs.
        # (The pool asserts above prove _engine_kwargs reached the engine;
        # connect_args itself is only observable inside the pool creator.)
        assert (
            database._engine_kwargs["connect_args"]["connect_timeout"]
            == database.DB_CONNECT_TIMEOUT_SECONDS
        )


# ---------------------------------------------------------------------------
# R4 — schema drift detection in init_db
# ---------------------------------------------------------------------------
class TestSchemaDriftCheck:
    def _conn_returning_dim(self, dim):
        conn = mock.MagicMock()
        conn.execute.return_value.scalar.return_value = dim
        ctx = mock.MagicMock()
        ctx.__enter__ = mock.MagicMock(return_value=conn)
        ctx.__exit__ = mock.MagicMock(return_value=False)
        return ctx

    def test_mismatched_live_dimension_raises_clear_error(self):
        with mock.patch.object(
            database.engine, "connect", return_value=self._conn_returning_dim(512)
        ):
            with pytest.raises(RuntimeError, match="SCHEMA DRIFT"):
                database._verify_embedding_dimension()

    def test_matching_dimension_passes(self):
        with mock.patch.object(
            database.engine,
            "connect",
            return_value=self._conn_returning_dim(models.EMBEDDING_DIM),
        ):
            database._verify_embedding_dimension()  # must not raise

    def test_hnsw_version_gate(self):
        assert database._pgvector_supports_hnsw("0.5.0") is True
        assert database._pgvector_supports_hnsw("0.8.1") is True
        assert database._pgvector_supports_hnsw("0.4.4") is False


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-v"]))
