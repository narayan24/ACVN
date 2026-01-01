extends Node
class_name NPC

var bill_total: float = 0.0
var current_order: Array = []
var tip_rate := 0.1
var is_paying := false

func on_order_placed(order: Array) -> void:
    current_order = order.duplicate()
    is_paying = false

## Called whenever a delivery of items arrives.
func on_served(delivery: Array) -> void:
    if delivery.is_empty():
        return
    for item_id in delivery:
        var price := PriceBook.price_of(item_id)
        bill_total += price
    # remove delivered items from the pending list if present
    for item_id in delivery:
        if current_order.has(item_id):
            current_order.erase(item_id)

func complete_payment(game_state) -> void:
    var tip := int(round(bill_total * tip_rate))
    var base_amount := bill_total + tip
    if base_amount <= 0:
        return
    if game_state and game_state.has_method("add_money"):
        game_state.add_money(base_amount)
    is_paying = true
    print("MONEY + %s = %s" % [base_amount, game_state.money if game_state and game_state.has_property("money") else base_amount])
    bill_total = 0
    current_order.clear()
