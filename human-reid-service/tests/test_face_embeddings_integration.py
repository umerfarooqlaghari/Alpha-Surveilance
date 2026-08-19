import base64
import os
import sys
import uuid
from datetime import datetime, timezone
from unittest import mock
import pytest
from fastapi.testclient import TestClient

# Ensure app is in path
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from app import main as main_mod
from app import schemas, models

TENANT = str(uuid.uuid4())
DIM = models.EMBEDDING_DIM


# Helper to generate a dummy 1x1 pixel base64 image (valid PNG)
def get_dummy_png_base64() -> str:
    from PIL import Image
    import io
    img = Image.new("RGB", (100, 100))
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return base64.b64encode(buf.getvalue()).decode("utf-8")



@pytest.fixture()
def client():
    with mock.patch.object(main_mod.database, "init_db"):
        with TestClient(main_mod.app) as client:
            yield client
    main_mod.app.dependency_overrides.clear()


class TestFaceEmbeddingsIntegration:
    @mock.patch("app.main.HAS_FACE_RECOGNITION", True)
    @mock.patch("face_recognition.face_locations")
    @mock.patch("face_recognition.face_encodings")
    def test_enroll_from_image_success(self, mock_encodings, mock_locations, client):
        # Arrange
        mock_locations.return_value = [(10, 60, 60, 10)]  # 50x50 face, area > min
        mock_encodings.return_value = [mock.MagicMock(tolist=lambda: [0.1] * DIM)]

        db_mock = mock.MagicMock()
        def mock_add(obj):
            obj.id = uuid.uuid4()
            obj.created_at = datetime.now(timezone.utc).replace(tzinfo=None)
        db_mock.add.side_effect = mock_add
        main_mod.app.dependency_overrides[main_mod.database.get_db] = lambda: db_mock

        payload = {
            "tenant_id": TENANT,
            "image_base64": get_dummy_png_base64(),
            "person_id": "emp-123",
            "metadata_json": {"source": "test"},
        }

        # Act
        response = client.post("/embeddings/enroll-image", json=payload)

        # Assert
        assert response.status_code == 200
        data = response.json()
        assert data["person_id"] == "emp-123"
        assert len(data["embedding"]) == DIM
        assert db_mock.add.called
        assert db_mock.commit.called

    @mock.patch("app.main.HAS_FACE_RECOGNITION", True)
    def test_enroll_from_image_invalid_base64(self, client):
        payload = {
            "tenant_id": TENANT,
            "image_base64": "invalid_base64_string!!!",
            "person_id": "emp-123",
        }
        response = client.post("/embeddings/enroll-image", json=payload)
        assert response.status_code == 400
        assert "not valid base64" in response.json()["detail"]

    @mock.patch("app.main.HAS_FACE_RECOGNITION", True)
    @mock.patch("face_recognition.face_locations")
    def test_enroll_from_image_no_face_detected(self, mock_locations, client):
        # Arrange: mock face_locations to return empty
        mock_locations.return_value = []

        payload = {
            "tenant_id": TENANT,
            "image_base64": get_dummy_png_base64(),
            "person_id": "emp-123",
        }

        response = client.post("/embeddings/enroll-image", json=payload)
        assert response.status_code == 422
        assert "No face detected" in response.json()["detail"]

    @mock.patch("app.main.HAS_FACE_RECOGNITION", True)
    @mock.patch("face_recognition.face_locations")
    def test_enroll_from_image_face_too_small(self, mock_locations, client):
        # Arrange: 10x10 face is smaller than 45x45 min limit
        mock_locations.return_value = [(10, 20, 20, 10)]

        payload = {
            "tenant_id": TENANT,
            "image_base64": get_dummy_png_base64(),
            "person_id": "emp-123",
        }

        response = client.post("/embeddings/enroll-image", json=payload)
        assert response.status_code == 422
        assert "Detected face too small" in response.json()["detail"]


class TestSyncEmbeddingsEndpoint:
    def test_sync_embeddings_returns_list(self, client):
        # Arrange
        db_mock = mock.MagicMock()
        mock_query = db_mock.query.return_value.filter.return_value
        mock_query.order_by.return_value.all.return_value = [
            models.PersonEmbedding(
                id=uuid.uuid4(),
                tenant_id=uuid.UUID(TENANT),
                embedding=[0.05] * DIM,
                person_id="emp-1",
                created_at=datetime.now(timezone.utc).replace(tzinfo=None)
            )
        ]
        main_mod.app.dependency_overrides[main_mod.database.get_db] = lambda: db_mock

        # Act
        response = client.get(f"/embeddings/sync?tenant_id={TENANT}")

        # Assert
        assert response.status_code == 200
        data = response.json()
        assert len(data) == 1
        assert data[0]["person_id"] == "emp-1"
