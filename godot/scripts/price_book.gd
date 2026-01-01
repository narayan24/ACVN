extends Node
class_name PriceBook

## Thin wrapper kept for compatibility - delegates pricing to the item database.
static func price_of(item_id: String) -> float:
    return ItemDB.price_of(item_id)
