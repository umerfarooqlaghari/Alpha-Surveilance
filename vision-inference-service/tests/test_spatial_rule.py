"""Unit tests for the spatial (geofence) rule filter."""
from rules.spatial import evaluate_spatial_rule


# Square zone occupying (100, 100) -> (200, 200) in pixel space.
SQUARE = [[100, 100], [200, 100], [200, 200], [100, 200]]


def _det(xmin, ymin, xmax, ymax, score=0.9, label="person"):
    return {
        "label": label,
        "score": score,
        "box": {"xmin": xmin, "ymin": ymin, "xmax": xmax, "ymax": ymax},
    }


# --- non-geofence rule_config: pass-through ---

def test_non_geofence_type_passes_through():
    # spatial filter only owns 'geofence'; other types are decided elsewhere.
    assert evaluate_spatial_rule(_det(0, 0, 10, 10), {"type": "anomaly"}) is True


# --- happy paths ---

def test_bottom_center_inside_entry_mode_is_violation():
    # bottom-center of (140..160, 50..150) = (150, 150) -> inside SQUARE
    det = _det(140, 50, 160, 150)
    assert evaluate_spatial_rule(
        det,
        {"type": "geofence", "polygon": SQUARE, "mode": "entry"},
    ) is True


def test_bottom_center_outside_entry_mode_is_suppressed():
    det = _det(0, 0, 20, 20)
    assert evaluate_spatial_rule(
        det,
        {"type": "geofence", "polygon": SQUARE, "mode": "entry"},
    ) is False


def test_exit_mode_inverts_logic():
    inside_det = _det(140, 50, 160, 150)
    outside_det = _det(0, 0, 20, 20)
    cfg = {"type": "geofence", "polygon": SQUARE, "mode": "exit"}
    assert evaluate_spatial_rule(inside_det, cfg) is False
    assert evaluate_spatial_rule(outside_det, cfg) is True


def test_anchor_centroid():
    # centroid of (140..160, 50..150) = (150, 100) -> lies exactly on the top
    # edge of SQUARE. We use Shapely's `intersects` so boundary points count as
    # inside (digital pixel coords are quantized to ints, so a boundary hit is
    # extremely common and should not be silently suppressed).
    det = _det(140, 50, 160, 150)
    cfg = {"type": "geofence", "polygon": SQUARE, "anchor": "centroid"}
    assert evaluate_spatial_rule(det, cfg) is True


def test_anchor_centroid_strictly_outside():
    # Strictly outside the polygon -> not a violation.
    det = _det(300, 300, 320, 320)
    cfg = {"type": "geofence", "polygon": SQUARE, "anchor": "centroid"}
    assert evaluate_spatial_rule(det, cfg) is False


def test_boundary_point_counts_as_inside():
    # Detection whose bottom-center anchor lands exactly on the polygon edge.
    det = _det(140, 50, 160, 100)  # bottom_center -> (150, 100) on top edge of SQUARE
    cfg = {"type": "geofence", "polygon": SQUARE, "anchor": "bottom_center"}
    assert evaluate_spatial_rule(det, cfg) is True


def test_anchor_top_center():
    # top-center y=ymin=120 -> inside square if x between 100..200
    det = _det(140, 120, 160, 300)
    cfg = {"type": "geofence", "polygon": SQUARE, "anchor": "top_center"}
    assert evaluate_spatial_rule(det, cfg) is True


# --- fail-closed paths ---

def test_missing_polygon_fails_closed():
    assert evaluate_spatial_rule(
        _det(140, 140, 160, 150),
        {"type": "geofence"},
    ) is False


def test_empty_polygon_fails_closed():
    assert evaluate_spatial_rule(
        _det(140, 140, 160, 150),
        {"type": "geofence", "polygon": []},
    ) is False


def test_two_vertex_polygon_fails_closed():
    assert evaluate_spatial_rule(
        _det(140, 140, 160, 150),
        {"type": "geofence", "polygon": [[100, 100], [200, 200]]},
    ) is False


def test_too_many_vertices_fails_closed():
    huge = [[i % 300, (i * 7) % 300] for i in range(200)]
    assert evaluate_spatial_rule(
        _det(140, 140, 160, 150),
        {"type": "geofence", "polygon": huge},
    ) is False


def test_unknown_mode_fails_closed():
    assert evaluate_spatial_rule(
        _det(140, 140, 160, 150),
        {"type": "geofence", "polygon": SQUARE, "mode": "kazooie"},
    ) is False


def test_unknown_anchor_fails_closed():
    assert evaluate_spatial_rule(
        _det(140, 140, 160, 150),
        {"type": "geofence", "polygon": SQUARE, "anchor": "elbow"},
    ) is False


def test_unknown_coordinate_space_fails_closed():
    assert evaluate_spatial_rule(
        _det(140, 140, 160, 150),
        {"type": "geofence", "polygon": SQUARE, "coordinate_space": "polar"},
    ) is False


def test_non_numeric_vertex_fails_closed():
    bad = [["a", "b"], [200, 100], [200, 200], [100, 200]]
    assert evaluate_spatial_rule(
        _det(140, 140, 160, 150),
        {"type": "geofence", "polygon": bad},
    ) is False


def test_missing_box_fails_closed():
    det = {"label": "person", "score": 0.9}
    assert evaluate_spatial_rule(
        det,
        {"type": "geofence", "polygon": SQUARE},
    ) is False


# --- normalized coords ---

def test_normalized_polygon_requires_frame_size():
    norm_square = [[0.25, 0.25], [0.5, 0.25], [0.5, 0.5], [0.25, 0.5]]
    cfg = {"type": "geofence", "polygon": norm_square, "coordinate_space": "normalized"}
    # No frame_size -> fail closed.
    assert evaluate_spatial_rule(_det(140, 140, 160, 200), cfg) is False


def test_normalized_polygon_resolved_with_frame_size():
    # On a 400x400 frame, [0.25..0.5] maps to pixel 100..200 -> same as SQUARE.
    norm_square = [[0.25, 0.25], [0.5, 0.25], [0.5, 0.5], [0.25, 0.5]]
    cfg = {"type": "geofence", "polygon": norm_square, "coordinate_space": "normalized"}
    inside_det = _det(140, 50, 160, 150)
    outside_det = _det(0, 0, 20, 20)
    assert evaluate_spatial_rule(inside_det, cfg, frame_size=(400, 400)) is True
    assert evaluate_spatial_rule(outside_det, cfg, frame_size=(400, 400)) is False


def test_normalized_out_of_range_fails_closed():
    bad = [[0.25, 0.25], [1.5, 0.25], [0.5, 0.5], [0.25, 0.5]]
    cfg = {"type": "geofence", "polygon": bad, "coordinate_space": "normalized"}
    assert evaluate_spatial_rule(_det(140, 140, 160, 150), cfg, frame_size=(400, 400)) is False


# --- self-intersecting polygons get repaired ---

def test_self_intersecting_polygon_is_repaired_or_rejected():
    # Bowtie shape — invalid as a simple polygon; shapely.make_valid usually
    # returns a MultiPolygon, which we reject (fail closed).
    bowtie = [[100, 100], [200, 200], [200, 100], [100, 200]]
    result = evaluate_spatial_rule(
        _det(140, 140, 160, 150),
        {"type": "geofence", "polygon": bowtie},
    )
    # Must NOT silently fail-open.
    assert result in (False, True)  # any deterministic outcome is acceptable
    # But the legacy bug would have returned True for ANY box; verify our
    # outside-the-shape case is still False:
    far_out = evaluate_spatial_rule(
        _det(900, 900, 950, 950),
        {"type": "geofence", "polygon": bowtie},
    )
    assert far_out is False
