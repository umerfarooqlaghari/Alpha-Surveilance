"""
rules/spatial.py
Provides mathematical tools for processing bounding box relationships.
Crucial for complex rule triggers like "person WITHOUT helmet" where the system
needs to know if a helmet exists *inside* or *overlapping* a specific person.
"""

def iou_percentage(boxA: dict, boxB: dict) -> float:
    """
    Intersection Over Union (IoU).
    Calculates the overlap percentage (0.0 to 1.0) between two boxes.
    Requires boxes in format: {"xmin": int, "ymin": int, "xmax": int, "ymax": int}
    """
    # Determine the coordinates of the intersection rectangle
    xA = max(boxA["xmin"], boxB["xmin"])
    yA = max(boxA["ymin"], boxB["ymin"])
    xB = min(boxA["xmax"], boxB["xmax"])
    yB = min(boxA["ymax"], boxB["ymax"])

    # Compute the area of intersection
    interArea = max(0, xB - xA) * max(0, yB - yA)

    if interArea == 0:
        return 0.0

    # Compute the area of both rectangles
    boxAArea = (boxA["xmax"] - boxA["xmin"]) * (boxA["ymax"] - boxA["ymin"])
    boxBArea = (boxB["xmax"] - boxB["xmin"]) * (boxB["ymax"] - boxB["ymin"])

    # Calculate IoU
    iou = interArea / float(boxAArea + boxBArea - interArea)
    return iou


def is_contained(outer_box: dict, inner_box: dict, margin_px: int = 15) -> bool:
    """
    Returns True if the inner_box is completely inside the outer_box.
    Provides a small margin of error (margin_px) to account for slight bounding box bleeding.
    """
    if (inner_box["xmin"] >= outer_box["xmin"] - margin_px and
        inner_box["xmax"] <= outer_box["xmax"] + margin_px and
        inner_box["ymin"] >= outer_box["ymin"] - margin_px and
        inner_box["ymax"] <= outer_box["ymax"] + margin_px):
        return True
    return False


def get_head_zone(person_box: dict, top_percentage: float = 0.25) -> dict:
    """
    Given a bounding box for a 'person', returns a smaller bounding box
    representing exactly the top N% of that person.
    Useful for checking if a hairnet or helmet is in the correct region.
    """
    height = person_box["ymax"] - person_box["ymin"]
    return {
        "xmin": person_box["xmin"],
        "xmax": person_box["xmax"],
        "ymin": person_box["ymin"],
        "ymax": int(person_box["ymin"] + (height * top_percentage))
    }


def get_hand_zone(person_box: dict, bottom_percentage: float = 0.40) -> dict:
    """
    Given a bounding box for a 'person', returns a smaller bounding box
    representing the lower N% of that person (where hands usually rest or move).
    Useful for checking glove compliance.
    """
    height = person_box["ymax"] - person_box["ymin"]
    return {
        "xmin": person_box["xmin"],
        "xmax": person_box["xmax"],
        "ymin": int(person_box["ymax"] - (height * bottom_percentage)),
        "ymax": person_box["ymax"]
    }

def get_overlap_ratio(zone: dict, target_box: dict) -> float:
    """
    Calculates how much of target_box is inside the zone.
    Returns value from 0.0 (no overlap) to 1.0 (target_box entirely inside zone).
    Unlike IoU, this focuses purely on whether the target_box fits in the desired area.
    """
    xA = max(zone["xmin"], target_box["xmin"])
    yA = max(zone["ymin"], target_box["ymin"])
    xB = min(zone["xmax"], target_box["xmax"])
    yB = min(zone["ymax"], target_box["ymax"])

    interArea = max(0, xB - xA) * max(0, yB - yA)
    targetArea = (target_box["xmax"] - target_box["xmin"]) * (target_box["ymax"] - target_box["ymin"])
    
    if targetArea == 0:
        return 0.0
        
    return interArea / float(targetArea)
