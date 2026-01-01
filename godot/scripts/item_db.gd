extends Node
class_name ItemDB

## Simple data-driven repository for menu items.
## In a real project this node can be turned into an Autoload for global access.
var _items := {
    "beer": {
        "name": "Beer",
        "price": 6,
        "prep_actions": 1,
        "is_alcohol": true,
        "type": "drink"
    },
    "wine": {
        "name": "Glass of Wine",
        "price": 8,
        "prep_actions": 1,
        "is_alcohol": true,
        "type": "drink"
    },
    "cola": {
        "name": "Cola",
        "price": 4,
        "prep_actions": 1,
        "is_alcohol": false,
        "type": "drink"
    },
    "burger": {
        "name": "Burger",
        "price": 12,
        "prep_actions": 3,
        "is_alcohol": false,
        "type": "food"
    },
    "fries": {
        "name": "Fries",
        "price": 5,
        "prep_actions": 2,
        "is_alcohol": false,
        "type": "food"
    },
    "salad": {
        "name": "Salad",
        "price": 7,
        "prep_actions": 2,
        "is_alcohol": false,
        "type": "food"
    }
}

static func get_item(item_id: String) -> Dictionary:
    return ItemDB._items.get(item_id, {})

static func price_of(item_id: String) -> float:
    var data := get_item(item_id)
    return data.get("price", 0)

static func prep_actions(item_id: String) -> int:
    var data := get_item(item_id)
    return data.get("prep_actions", 1)

static func is_alcohol(item_id: String) -> bool:
    var data := get_item(item_id)
    return data.get("is_alcohol", false)

static func type_of(item_id: String) -> String:
    var data := get_item(item_id)
    return data.get("type", "")
