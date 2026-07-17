from typing import Dict, Iterable, Optional


RESTAURANT_TRIGGER_ALIASES: Dict[str, str] = {
    "person without hairnet": "no-hairnet",
    "person-without-hairnet": "no-hairnet",
    "no hairnet": "no-hairnet",
    "no-hairnet": "no-hairnet",
    "missing hairnet": "no-hairnet",
    "missing-hairnet": "no-hairnet",
    "person without mask": "no-mask",
    "person-without-mask": "no-mask",
    "no mask": "no-mask",
    "no-mask": "no-mask",
    "missing mask": "no-mask",
    "missing-mask": "no-mask",
    "no face cover": "no-mask",
    "no-face-cover": "no-mask",
    "person without glove": "no-glove",
    "person without gloves": "no-glove",
    "person-without-glove": "no-glove",
    "person-without-gloves": "no-glove",
    "no glove": "no-glove",
    "no gloves": "no-glove",
    "no-glove": "no-glove",
    "no-gloves": "no-glove",
    "missing glove": "no-glove",
    "missing gloves": "no-glove",
    "missing-glove": "no-glove",
    "missing-gloves": "no-glove",
    "incorrect mask": "incorrect-mask",
    "incorrect-mask": "incorrect-mask",
    "improper mask": "incorrect-mask",
    "improper-mask": "incorrect-mask",
    "mask below nose": "incorrect-mask",
    "mask-below-nose": "incorrect-mask",
}

PEST_TRIGGER_ALIASES: Dict[str, str] = {
    "cockroach": "cockroach",
    "cock-roach": "cockroach",
    "lizard": "lizard",
    "gecko": "lizard",
    "rat": "rat",
    "mouse": "rat",
    "rodent": "rat",
}


def canonical_label(label: str) -> str:
    return str(label or "").strip().lower().replace("_", "-").replace(" ", "-")


def restaurant_targets(rule_labels: Iterable[str]) -> Dict[str, str]:
    labels = [str(label).strip().lower() for label in rule_labels if str(label).strip()]
    if not labels:
        return {
            "no-hairnet": "no-hairnet",
            "no-mask": "no-mask",
            "no-glove": "no-glove",
            "incorrect-mask": "incorrect-mask",
        }

    targets: Dict[str, str] = {}
    for trigger in labels:
        canonical = RESTAURANT_TRIGGER_ALIASES.get(trigger.replace("_", "-"))
        if canonical:
            targets[canonical] = trigger
    return targets


def pest_targets(rule_labels: Iterable[str]) -> Dict[str, str]:
    labels = [str(label).strip().lower() for label in rule_labels if str(label).strip()]
    if not labels:
        return {
            "cockroach": "cockroach",
            "lizard": "lizard",
            "rat": "rat",
        }

    targets: Dict[str, str] = {}
    for trigger in labels:
        canonical = PEST_TRIGGER_ALIASES.get(canonical_label(trigger))
        if canonical:
            targets[canonical] = trigger
    return targets


def infer_rule_family(model_type: str, trigger_labels: Iterable[str]) -> Optional[str]:
    normalized_model_type = str(model_type or "").strip()
    if normalized_model_type == "OpenVocabGrounding":
        return "open-vocab"

    restaurant = restaurant_targets(trigger_labels)
    pest = pest_targets(trigger_labels)

    has_restaurant = bool(restaurant)
    has_pest = bool(pest)

    if has_restaurant and not has_pest:
        return "restaurant-ppe"
    if has_pest and not has_restaurant:
        return "pest-detection"
    return None