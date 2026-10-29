"""
app/in_memory_store.py
======================
High-performance, in-memory vector store for Human Re-ID.

Replaces the computational and RAM overhead of a full PostgreSQL + pgvector container
on edge hardware. Pre-normalizes vectors so cosine similarity is computed via a single
NumPy BLAS matrix-vector dot product in < 0.2 ms for up to thousands of employees.
"""

from __future__ import annotations

import json
import logging
import os
import threading
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Set
from uuid import UUID, uuid4
import numpy as np

try:
    from .models import EMBEDDING_DIM
except Exception:
    EMBEDDING_DIM = int(os.getenv("EMBEDDING_DIM", "128"))

logger = logging.getLogger(__name__)


class TenantVectorIndex:
    """Maintains pre-normalized embedding matrix and metadata for a single tenant."""

    def __init__(self, tenant_id: UUID):
        self.tenant_id = tenant_id
        self.ids: List[UUID] = []
        self.person_ids: List[Optional[str]] = []
        self.frame_urls: List[Optional[str]] = []
        self.created_ats: List[datetime] = []
        # Pre-normalized vectors stored individually for O(1) append
        self._vectors: List[np.ndarray] = []
        self._matrix: Optional[np.ndarray] = None
        self._dirty: bool = False

    @property
    def matrix(self) -> Optional[np.ndarray]:
        """Lazily compiled contiguous 2D NumPy matrix for SIMD dot products."""
        if not self._vectors:
            self._matrix = None
            self._dirty = False
            return None
        if self._dirty or self._matrix is None:
            if len(self._vectors) == 1:
                self._matrix = self._vectors[0].reshape(1, EMBEDDING_DIM)
            else:
                self._matrix = np.vstack(self._vectors)
            self._dirty = False
        return self._matrix

    @matrix.setter
    def matrix(self, val: Optional[np.ndarray]) -> None:
        self._matrix = val
        self._dirty = False

    def size(self) -> int:
        return len(self.ids)

    def rebuild(self, records: List[Dict[str, Any]]) -> None:
        """Replace all records for this tenant with a new batch."""
        new_ids: List[UUID] = []
        new_person_ids: List[Optional[str]] = []
        new_frame_urls: List[Optional[str]] = []
        new_created_ats: List[datetime] = []
        raw_vecs: List[List[float]] = []

        for r in records:
            emb = r.get("embedding")
            if not emb or len(emb) != EMBEDDING_DIM:
                continue
            new_ids.append(r.get("id") if isinstance(r.get("id"), UUID) else UUID(str(r.get("id") or uuid4())))
            new_person_ids.append(r.get("person_id"))
            new_frame_urls.append(r.get("frame_url"))
            c_at = r.get("created_at")
            if isinstance(c_at, datetime):
                new_created_ats.append(c_at)
            elif isinstance(c_at, str):
                try:
                    new_created_ats.append(datetime.fromisoformat(c_at))
                except Exception:
                    new_created_ats.append(datetime.now(timezone.utc))
            else:
                new_created_ats.append(datetime.now(timezone.utc))
            raw_vecs.append(emb)

        if raw_vecs:
            mat = np.array(raw_vecs, dtype=np.float32)
            norms = np.linalg.norm(mat, axis=1, keepdims=True)
            norm_mat = mat / np.maximum(norms, 1e-7)
            self._matrix = norm_mat
            self._vectors = [row for row in norm_mat]
            self._dirty = False
        else:
            self._matrix = None
            self._vectors = []
            self._dirty = False

        self.ids = new_ids
        self.person_ids = new_person_ids
        self.frame_urls = new_frame_urls
        self.created_ats = new_created_ats

    def add(
        self,
        id_val: UUID,
        person_id: Optional[str],
        embedding: List[float],
        frame_url: Optional[str] = None,
        created_at: Optional[datetime] = None,
    ) -> None:
        """Add a single record in amortized O(1) time without full matrix reallocation."""
        if len(embedding) != EMBEDDING_DIM:
            raise ValueError(f"Expected embedding dimension {EMBEDDING_DIM}, got {len(embedding)}")

        vec = np.array(embedding, dtype=np.float32)
        norm = np.linalg.norm(vec)
        norm_vec = vec / max(float(norm), 1e-7)

        self.ids.append(id_val)
        self.person_ids.append(person_id)
        self.frame_urls.append(frame_url)
        self.created_ats.append(created_at or datetime.now(timezone.utc))
        self._vectors.append(norm_vec)
        self._dirty = True

    def delete_person(self, person_id: str) -> int:
        """Remove all vectors enrolled for a specific person_id."""
        if not self.ids or not self._vectors:
            return 0

        keep_indices = [i for i, pid in enumerate(self.person_ids) if pid != person_id]
        deleted_count = len(self.ids) - len(keep_indices)

        if deleted_count == 0:
            return 0

        if not keep_indices:
            self.ids = []
            self.person_ids = []
            self.frame_urls = []
            self.created_ats = []
            self._vectors = []
            self._matrix = None
            self._dirty = False
            return deleted_count

        self.ids = [self.ids[i] for i in keep_indices]
        self.person_ids = [self.person_ids[i] for i in keep_indices]
        self.frame_urls = [self.frame_urls[i] for i in keep_indices]
        self.created_ats = [self.created_ats[i] for i in keep_indices]
        self._vectors = [self._vectors[i] for i in keep_indices]
        if self._matrix is not None and not self._dirty:
            self._matrix = self._matrix[keep_indices]
        else:
            self._dirty = True
        return deleted_count

    def search(
        self,
        query_embedding: List[float],
        threshold: float = 0.75,
        top_k: int = 5,
    ) -> List[Dict[str, Any]]:
        """
        Sub-millisecond cosine similarity search.
        Deduplicates per person_id keeping only the closest vector (mirroring SQL DISTINCT ON).
        Returns results sorted by score descending, capped at top_k.
        """
        mat = self.matrix
        if mat is None or len(self.ids) == 0:
            return []
        if not query_embedding or len(query_embedding) != EMBEDDING_DIM:
            return []

        q = np.array(query_embedding, dtype=np.float32)
        if not np.isfinite(q).all():
            return []
        q_norm = np.linalg.norm(q)
        if q_norm < 1e-7:
            return []
        q = q / q_norm

        # Matrix-vector dot product computes cosine similarities across all rows simultaneously
        scores = np.dot(mat, q)

        # Group by person_id (DISTINCT ON person_id) keeping highest score
        best_per_person: Dict[str, tuple[float, int]] = {}
        for idx, (pid, score) in enumerate(zip(self.person_ids, scores)):
            sc = float(score)
            if sc < threshold:
                continue
            key = pid if pid is not None else f"anon_{self.ids[idx]}"
            if key not in best_per_person or sc > best_per_person[key][0]:
                best_per_person[key] = (sc, idx)

        if not best_per_person:
            return []

        # Sort descending by score
        sorted_matches = sorted(best_per_person.values(), key=lambda x: x[0], reverse=True)[:top_k]

        results = []
        for score_val, idx in sorted_matches:
            results.append({
                "id": self.ids[idx],
                "person_id": self.person_ids[idx],
                "score": float(score_val),
                "frame_url": self.frame_urls[idx],
                "created_at": self.created_ats[idx],
            })
        return results


class InMemoryEmbeddingStore:
    """Thread-safe multi-tenant in-memory embedding repository."""

    def __init__(self):
        self._tenants: Dict[UUID, TenantVectorIndex] = {}
        self._loaded_tenants: Set[UUID] = set()
        self._lock = threading.RLock()

    def is_tenant_loaded(self, tenant_id: UUID) -> bool:
        """Check if tenant vector records have been loaded into RAM (even if 0 records)."""
        with self._lock:
            return tenant_id in self._loaded_tenants or tenant_id in self._tenants

    def has_tenant(self, tenant_id: UUID) -> bool:
        with self._lock:
            index = self._tenants.get(tenant_id)
            return index is not None and index.size() > 0

    def size(self, tenant_id: Optional[UUID] = None) -> int:
        with self._lock:
            if tenant_id is not None:
                index = self._tenants.get(tenant_id)
                return index.size() if index else 0
            return sum(idx.size() for idx in self._tenants.values())

    def load_tenant_records(self, tenant_id: UUID, records: List[Dict[str, Any]]) -> None:
        """Bulk load or refresh embeddings for a tenant."""
        with self._lock:
            self._loaded_tenants.add(tenant_id)
            index = self._tenants.setdefault(tenant_id, TenantVectorIndex(tenant_id))
            index.rebuild(records)
            logger.info("Loaded %d in-memory embeddings for tenant %s", index.size(), tenant_id)

    def add_embedding(
        self,
        tenant_id: UUID,
        person_id: Optional[str],
        embedding: List[float],
        id_val: Optional[UUID] = None,
        frame_url: Optional[str] = None,
        created_at: Optional[datetime] = None,
    ) -> None:
        """Enroll a single embedding into the in-memory store."""
        with self._lock:
            index = self._tenants.setdefault(tenant_id, TenantVectorIndex(tenant_id))
            index.add(
                id_val=id_val or uuid4(),
                person_id=person_id,
                embedding=embedding,
                frame_url=frame_url,
                created_at=created_at,
            )

    def delete_person(self, tenant_id: UUID, person_id: str) -> int:
        """Delete all vectors enrolled for person_id in tenant."""
        with self._lock:
            index = self._tenants.get(tenant_id)
            if not index:
                return 0
            return index.delete_person(person_id)

    def search(
        self,
        tenant_id: UUID,
        query_embedding: List[float],
        threshold: float = 0.75,
        top_k: int = 5,
    ) -> List[Dict[str, Any]]:
        """Perform sub-millisecond similarity search against in-memory index."""
        with self._lock:
            index = self._tenants.get(tenant_id)
            if not index:
                return []
            return index.search(query_embedding, threshold=threshold, top_k=top_k)

    def save_to_disk(self, file_path: str) -> None:
        """Persist state to disk for zero-DB offline edge startup."""
        with self._lock:
            data = {}
            for tid, idx in self._tenants.items():
                if idx.size() == 0 or idx.matrix is None:
                    continue
                data[str(tid)] = {
                    "ids": [str(i) for i in idx.ids],
                    "person_ids": idx.person_ids,
                    "frame_urls": idx.frame_urls,
                    "created_ats": [ca.isoformat() for ca in idx.created_ats],
                    "matrix": idx.matrix.tolist(),
                }
            tmp_path = f"{file_path}.tmp"
            with open(tmp_path, "w", encoding="utf-8") as f:
                json.dump(data, f)
            os.replace(tmp_path, file_path)
            logger.info("Saved %d tenants to local embedding cache: %s", len(data), file_path)

    def load_from_disk(self, file_path: str) -> bool:
        """Restore index from disk snapshot."""
        if not os.path.exists(file_path):
            return False
        with self._lock:
            try:
                with open(file_path, "r", encoding="utf-8") as f:
                    data = json.load(f)
                for tid_str, tdata in data.items():
                    tid = UUID(tid_str)
                    records = []
                    ids = tdata.get("ids", [])
                    pids = tdata.get("person_ids", [])
                    furls = tdata.get("frame_urls", [])
                    cats = tdata.get("created_ats", [])
                    mat = tdata.get("matrix", [])
                    n_records = min(len(ids), len(mat))
                    for i in range(n_records):
                        try:
                            rec_id = UUID(ids[i])
                        except Exception:
                            rec_id = uuid4()
                        records.append({
                            "id": rec_id,
                            "person_id": pids[i] if i < len(pids) else None,
                            "frame_url": furls[i] if i < len(furls) else None,
                            "created_at": cats[i] if i < len(cats) else datetime.now(timezone.utc).isoformat(),
                            "embedding": mat[i],
                        })
                    self.load_tenant_records(tid, records)
                return True
            except Exception as e:
                logger.warning("Failed to load local embedding cache from %s: %s", file_path, e)
                return False

    def clear(self) -> None:
        with self._lock:
            self._tenants.clear()
            self._loaded_tenants.clear()


# Global in-memory embedding store singleton
embedding_store = InMemoryEmbeddingStore()
